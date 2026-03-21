namespace MyAutoMapper.Parameters;

public sealed class ParameterBinder : IParameterBinder
{
    private readonly Dictionary<string, object?> _values = [];

    public IParameterBinder Set<T>(string name, T value)
    {
        _values[name] = value;
        return this;
    }

    public IParameterBinder Set<T>(ParameterSlot<T> slot, T value)
    {
        _values[slot.Name] = value;
        return this;
    }

    public bool TryGetValue(string name, out object? value)
        => _values.TryGetValue(name, out value);

    public IReadOnlyDictionary<string, object?> Values => _values;
}
