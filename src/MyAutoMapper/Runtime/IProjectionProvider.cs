using System.Linq.Expressions;
using MyAutoMapper.Parameters;

namespace MyAutoMapper.Runtime;

public interface IProjectionProvider
{
    Expression<Func<TSource, TDest>> GetProjection<TSource, TDest>();
    Expression<Func<TSource, TDest>> GetProjection<TSource, TDest>(IParameterBinder parameters);
}
