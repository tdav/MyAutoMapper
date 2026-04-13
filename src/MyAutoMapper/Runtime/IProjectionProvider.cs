using System.Linq.Expressions;
using SmAutoMapper.Parameters;

namespace SmAutoMapper.Runtime;

public interface IProjectionProvider
{
    Expression<Func<TSource, TDest>> GetProjection<TSource, TDest>();
    Expression<Func<TSource, TDest>> GetProjection<TSource, TDest>(IParameterBinder parameters);
}
