using System.Reflection;
using System.Reflection.Emit;
using System.Collections.Concurrent;

namespace SmAutoMapper.Parameters;

internal sealed class ClosureHolderFactory
{
    private static readonly ModuleBuilder ModuleBuilder;
    private static int _typeCounter;
    private static readonly ConcurrentDictionary<string, HolderTypeInfo> _cache = new();

    static ClosureHolderFactory()
    {
        var assemblyName = new AssemblyName("MyAutoMapper.DynamicHolders");
        var assemblyBuilder = AssemblyBuilder.DefineDynamicAssembly(
            assemblyName, AssemblyBuilderAccess.Run);
        ModuleBuilder = assemblyBuilder.DefineDynamicModule("MainModule");
    }

    /// <summary>
    /// Creates or retrieves a holder type for the given parameter slots.
    /// Returns HolderTypeInfo containing the generated Type, a factory to create instances,
    /// and a mapping from parameter name to PropertyInfo.
    /// </summary>
    public HolderTypeInfo GetOrCreateHolderType(IReadOnlyList<IParameterSlot> parameterSlots)
    {
        // Create a cache key from sorted parameter names and types
        var key = string.Join("|", parameterSlots
            .OrderBy(p => p.Name)
            .Select(p => $"{p.Name}:{p.ValueType.FullName}"));

        return _cache.GetOrAdd(key, _ => CreateHolderType(parameterSlots));
    }

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

    public HolderTypeInfo(Type holderType, IReadOnlyDictionary<string, PropertyInfo> propertyMap)
    {
        HolderType = holderType;
        PropertyMap = propertyMap;
    }

    /// <summary>
    /// Creates a new holder instance with the given parameter values.
    /// </summary>
    public object CreateInstance(IReadOnlyDictionary<string, object?> parameterValues)
    {
        var instance = Activator.CreateInstance(HolderType)!;
        foreach (var (name, value) in parameterValues)
        {
            if (PropertyMap.TryGetValue(name, out var property))
            {
                property.SetValue(instance, value);
            }
        }
        return instance;
    }

    /// <summary>
    /// Creates a default holder instance (all properties have default values).
    /// </summary>
    public object CreateDefaultInstance()
        => Activator.CreateInstance(HolderType)!;
}
