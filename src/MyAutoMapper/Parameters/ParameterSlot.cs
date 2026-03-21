namespace MyAutoMapper.Parameters;

public sealed class ParameterSlot<T> : IParameterSlot
{
    public string Name { get; }
    public Type ValueType => typeof(T);
    public Guid Id { get; } = Guid.NewGuid();

    public ParameterSlot(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
    }
}
