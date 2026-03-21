namespace MyAutoMapper.Compilation;

public readonly record struct TypePair(Type SourceType, Type DestinationType)
{
    public override int GetHashCode() => HashCode.Combine(SourceType, DestinationType);
}
