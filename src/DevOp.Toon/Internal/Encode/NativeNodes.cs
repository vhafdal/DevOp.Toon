#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace DevOp.Toon.Internal.Encode;

internal abstract class NativeNode
{
}

internal sealed class NativePrimitiveNode : NativeNode
{
    public NativePrimitiveNode(object? value)
    {
        Value = value;
    }

    public object? Value { get; }
}

internal readonly struct DeferredDateTimeValue
{
    public DeferredDateTimeValue(DateTime value)
    {
        Value = value;
    }

    public DateTime Value { get; }
}

internal readonly struct DeferredDateTimeOffsetValue
{
    public DeferredDateTimeOffsetValue(DateTimeOffset value)
    {
        Value = value;
    }

    public DateTimeOffset Value { get; }
}

#if NET6_0_OR_GREATER
internal readonly struct DeferredDateOnlyValue
{
    public DeferredDateOnlyValue(DateOnly value)
    {
        Value = value;
    }

    public DateOnly Value { get; }
}

internal readonly struct DeferredTimeOnlyValue
{
    public DeferredTimeOnlyValue(TimeOnly value)
    {
        Value = value;
    }

    public TimeOnly Value { get; }
}
#endif

internal sealed class NativeObjectNode : NativeNode, IEnumerable<KeyValuePair<string, NativeNode?>>
{
    private readonly List<KeyValuePair<string, NativeNode?>> properties = new();
    private readonly Dictionary<string, int> propertyIndexes = new(StringComparer.Ordinal);

    public int Count => properties.Count;

    public IEnumerable<string> Keys
    {
        get
        {
            for (int i = 0; i < properties.Count; i++)
            {
                yield return properties[i].Key;
            }
        }
    }

    public NativeNode? this[string key]
    {
        get
        {
            if (propertyIndexes.TryGetValue(key, out var index))
            {
                return properties[index].Value;
            }

            throw new KeyNotFoundException(key);
        }
        set
        {
            if (propertyIndexes.TryGetValue(key, out var index))
            {
                properties[index] = new KeyValuePair<string, NativeNode?>(key, value);
                return;
            }

            propertyIndexes[key] = properties.Count;
            properties.Add(new KeyValuePair<string, NativeNode?>(key, value));
        }
    }

    public bool ContainsKey(string key) => propertyIndexes.ContainsKey(key);

    public void Add(string key, NativeNode? value)
    {
        this[key] = value;
    }

    public IEnumerator<KeyValuePair<string, NativeNode?>> GetEnumerator() => properties.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

internal sealed class NativeArrayNode : NativeNode, IEnumerable<NativeNode?>
{
    private readonly List<NativeNode?> items = new();

    public int Count => items.Count;

    public NativeNode? this[int index] => items[index];

    public void Add(NativeNode? value)
    {
        items.Add(value);
    }

    public IEnumerator<NativeNode?> GetEnumerator() => items.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
