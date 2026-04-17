namespace SmAutoMapper.Runtime;

public interface IMapper
{
    TDest Map<TSource, TDest>(TSource source);
}
