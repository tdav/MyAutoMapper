using System.Linq.Expressions;
using SmAutoMapper.Compilation;
using SmAutoMapper.Parameters;

namespace SmAutoMapper.Runtime;

public sealed class ProjectionProvider : IProjectionProvider
{
    private readonly MapperConfiguration _configuration;

    public ProjectionProvider(MapperConfiguration configuration)
    {
        _configuration = configuration;
    }

    public Expression<Func<TSource, TDest>> GetProjection<TSource, TDest>()
    {
        var typeMap = _configuration.GetTypeMap<TSource, TDest>();
        return (Expression<Func<TSource, TDest>>)(typeMap.ProjectionExpression
            ?? throw new InvalidOperationException(
                $"No projection expression compiled for {typeof(TSource).Name} -> {typeof(TDest).Name}."));
    }

    public Expression<Func<TSource, TDest>> GetProjection<TSource, TDest>(IParameterBinder parameters)
    {
        var typeMap = _configuration.GetTypeMap<TSource, TDest>();

        if (typeMap.ProjectionExpression is null)
            throw new InvalidOperationException(
                $"No projection expression compiled for {typeof(TSource).Name} -> {typeof(TDest).Name}.");

        // If no parameterized mappings, return the cached expression as-is
        if (typeMap.ClosureHolderType is null || typeMap.HolderPropertyMap is null)
        {
            return (Expression<Func<TSource, TDest>>)typeMap.ProjectionExpression;
        }

        // Create a new holder instance using the cached HolderPropertyMap (no runtime reflection)
        var holderType = typeMap.ClosureHolderType;
        var newHolder = Activator.CreateInstance(holderType)!;

        foreach (var (name, value) in parameters.Values)
        {
            if (typeMap.HolderPropertyMap.TryGetValue(name, out var property))
            {
                // Allow null values — don't skip them
                property.SetValue(newHolder, value);
            }
        }

        // Use ClosureValueInjector to swap the holder constant in the expression
        return ClosureValueInjector.InjectParameters<TSource, TDest>(
            (Expression<Func<TSource, TDest>>)typeMap.ProjectionExpression,
            holderType,
            newHolder);
    }
}
