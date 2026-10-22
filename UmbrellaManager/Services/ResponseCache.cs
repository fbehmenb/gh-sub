namespace UmbrellaManager.Services;

public sealed class ResponseCache
{
    private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);

    public void Clear()
    {
        lock (_values)
        {
            _values.Clear();
        }
    }

    public bool TryGet(string key, out string value)
    {
        lock (_values)
        {
            return _values.TryGetValue(key, out value!);
        }
    }

    public void Set(string key, string value)
    {
        lock (_values)
        {
            _values[key] = value;
        }
    }
}
