namespace SmAutoMapper.Parameters;

public interface IParameterSlot
{
    string Name { get; }
    Type ValueType { get; }
    Guid Id { get; }
}
