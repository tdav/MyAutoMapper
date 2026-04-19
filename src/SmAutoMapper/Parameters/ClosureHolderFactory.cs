using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;

namespace SmAutoMapper.Parameters;

internal sealed class ClosureHolderFactory
{
    private static readonly ModuleBuilder ModuleBuilder;
    private static int _typeCounter;
    private static readonly ConcurrentDictionary<string, HolderTypeInfo> _cache = new();

    [UnconditionalSuppressMessage(
        "AOT",
        "IL3050:RequiresDynamicCode",
        Justification = "Static initializer is reachable only via public API marked [RequiresDynamicCode]; " +
                        "the library is IsAotCompatible=false and relies on runtime type generation.")]
    static ClosureHolderFactory()
    {
        var assemblyName = new AssemblyName("SmAutoMapper.DynamicHolders");
        var assemblyBuilder = AssemblyBuilder.DefineDynamicAssembly(
            assemblyName, AssemblyBuilderAccess.Run);
        ModuleBuilder = assemblyBuilder.DefineDynamicModule("MainModule");
    }

    /// <summary>
    /// Creates or retrieves a holder type for the given parameter slots.
    /// Returns HolderTypeInfo containing the generated Type, a factory to create instances,
    /// and a mapping from parameter name to PropertyInfo.
    /// </summary>
    [RequiresDynamicCode("SmAutoMapper uses Reflection.Emit to generate closure holder types at runtime.")]
    [RequiresUnreferencedCode("SmAutoMapper uses reflection over mapped types; members may be trimmed.")]
    public HolderTypeInfo GetOrCreateHolderType(IReadOnlyList<IParameterSlot> parameterSlots)
    {
        // Create a cache key from sorted parameter names and types
        var key = string.Join("|", parameterSlots
            .OrderBy(p => p.Name)
            .Select(p => $"{p.Name}:{p.ValueType.FullName}"));

        return _cache.GetOrAdd(key, _ => CreateHolderType(parameterSlots));
    }

    [RequiresDynamicCode("SmAutoMapper uses Reflection.Emit to generate closure holder types at runtime.")]
    [RequiresUnreferencedCode("SmAutoMapper uses reflection over mapped types; members may be trimmed.")]
    private static HolderTypeInfo CreateHolderType(IReadOnlyList<IParameterSlot> parameterSlots)
    {
        var typeNum = Interlocked.Increment(ref _typeCounter);
        var typeName = $"ClosureHolder_{typeNum}";

        var typeBuilder = ModuleBuilder.DefineType(
            typeName,
            TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.Sealed,
            typeof(object));

        var propertyMap = new Dictionary<string, PropertyInfo>();

        foreach (var slot in parameterSlots)
        {
            // Define backing field
            var fieldBuilder = typeBuilder.DefineField(
                $"__{slot.Name}", slot.ValueType, FieldAttributes.Private);

            // Define property
            var propertyBuilder = typeBuilder.DefineProperty(
                slot.Name, PropertyAttributes.None, slot.ValueType, null);

            // Getter
            var getterBuilder = typeBuilder.DefineMethod(
                $"get_{slot.Name}",
                MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
                slot.ValueType, Type.EmptyTypes);
            var getIl = getterBuilder.GetILGenerator();
            getIl.Emit(OpCodes.Ldarg_0);
            getIl.Emit(OpCodes.Ldfld, fieldBuilder);
            getIl.Emit(OpCodes.Ret);
            propertyBuilder.SetGetMethod(getterBuilder);

            // Setter
            var setterBuilder = typeBuilder.DefineMethod(
                $"set_{slot.Name}",
                MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
                null, [slot.ValueType]);
            var setIl = setterBuilder.GetILGenerator();
            setIl.Emit(OpCodes.Ldarg_0);
            setIl.Emit(OpCodes.Ldarg_1);
            setIl.Emit(OpCodes.Stfld, fieldBuilder);
            setIl.Emit(OpCodes.Ret);
            propertyBuilder.SetSetMethod(setterBuilder);
        }

        var holderType = typeBuilder.CreateType()!;

        // Build property map from the created type
        foreach (var slot in parameterSlots)
        {
            propertyMap[slot.Name] = holderType.GetProperty(slot.Name)!;
        }

        return new HolderTypeInfo(holderType, propertyMap);
    }
}

internal sealed class HolderTypeInfo
{
    public Type HolderType { get; }
    public IReadOnlyDictionary<string, PropertyInfo> PropertyMap { get; }
    public Func<object> Factory { get; }
    public IReadOnlyDictionary<string, Action<object, object?>> Setters { get; }

    [RequiresDynamicCode("SmAutoMapper uses Reflection.Emit to generate closure holder types at runtime.")]
    [RequiresUnreferencedCode("SmAutoMapper uses reflection over mapped types; members may be trimmed.")]
    public HolderTypeInfo(Type holderType, IReadOnlyDictionary<string, PropertyInfo> propertyMap)
    {
        HolderType = holderType;
        PropertyMap = propertyMap;
        Factory = CompileFactory(holderType);
        Setters = CompileSetters(holderType, propertyMap);
    }

    public object CreateInstance(IReadOnlyDictionary<string, object?> parameterValues)
    {
        var instance = Factory();
        foreach (var (name, value) in parameterValues)
        {
            if (Setters.TryGetValue(name, out var set))
            {
                set(instance, value);
            }
        }
        return instance;
    }

    public object CreateDefaultInstance() => Factory();

    [RequiresDynamicCode("SmAutoMapper uses Reflection.Emit to generate closure holder types at runtime.")]
    [RequiresUnreferencedCode("SmAutoMapper uses reflection over mapped types; members may be trimmed.")]
    private static Func<object> CompileFactory(Type holderType)
    {
        var ctor = holderType.GetConstructor(Type.EmptyTypes)
            ?? throw new InvalidOperationException(
                $"Generated holder type {holderType.FullName} has no parameterless constructor.");
        var newExpr = Expression.New(ctor);
        var body = Expression.Convert(newExpr, typeof(object));
        return Expression.Lambda<Func<object>>(body).Compile();
    }

    [RequiresDynamicCode("SmAutoMapper uses Reflection.Emit to generate closure holder types at runtime.")]
    [RequiresUnreferencedCode("SmAutoMapper uses reflection over mapped types; members may be trimmed.")]
    private static IReadOnlyDictionary<string, Action<object, object?>> CompileSetters(
        Type holderType,
        IReadOnlyDictionary<string, PropertyInfo> propertyMap)
    {
        var result = new Dictionary<string, Action<object, object?>>(propertyMap.Count);
        foreach (var (name, property) in propertyMap)
        {
            var instanceParam = Expression.Parameter(typeof(object), "instance");
            var valueParam = Expression.Parameter(typeof(object), "value");

            var typedInstance = Expression.Convert(instanceParam, holderType);
            var typedValue = Expression.Convert(valueParam, property.PropertyType);

            var assign = Expression.Assign(
                Expression.Property(typedInstance, property),
                typedValue);

            result[name] = Expression.Lambda<Action<object, object?>>(
                assign, instanceParam, valueParam).Compile();
        }
        return result;
    }
}
