namespace MyAutoMapper.Runtime;

public sealed class MappingContext
{
    private readonly Dictionary<string, object?> _items = [];

    public MappingContext Set(string key, object? value)
    {
        _items[key] = value;
        return this;
    }

    public T? Get<T>(string key)
    {
        if (_items.TryGetValue(key, out var value) && value is T typed)
            return typed;
        return default;
    }

    public bool TryGetValue(string key, out object? value)
        => _items.TryGetValue(key, out value);
}
