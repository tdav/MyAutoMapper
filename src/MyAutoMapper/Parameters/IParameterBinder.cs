namespace MyAutoMapper.Parameters;

public interface IParameterBinder
{
    IParameterBinder Set<T>(string name, T value);
    IParameterBinder Set<T>(ParameterSlot<T> slot, T value);
    bool TryGetValue(string name, out object? value);
    IReadOnlyDictionary<string, object?> Values { get; }
}
