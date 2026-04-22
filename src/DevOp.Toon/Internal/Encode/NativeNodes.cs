#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace DevOp.Toon.Internal.Encode;

/// <summary>
/// Base type for the internal TOON value graph used by encoders and decoders before conversion to the public <see cref="ToonNode"/> model.
/// </summary>
internal abstract class NativeNode
{
}

/// <summary>
/// Represents a scalar internal node, including <see langword="null"/>, strings, booleans, numbers, enums, and deferred date/time wrappers.
/// </summary>
internal sealed class NativePrimitiveNode : NativeNode
{
    /// <summary>
    /// Initializes a new scalar node with the supplied raw CLR value.
    /// </summary>
    /// <param name="value">The scalar value to preserve until primitive encoding or typed materialization.</param>
    public NativePrimitiveNode(object? value)
    {
        Value = value;
    }

    /// <summary>
    /// Gets the raw scalar value carried by the node.
    /// </summary>
    public object? Value { get; }
}

/// <summary>
/// Wraps a <see cref="DateTime"/> so normalization can preserve the original CLR type until the primitive encoder formats it.
/// </summary>
internal readonly struct DeferredDateTimeValue
{
    /// <summary>
    /// Initializes a new deferred <see cref="DateTime"/> value.
    /// </summary>
    /// <param name="value">The date/time value to format during primitive encoding.</param>
    public DeferredDateTimeValue(DateTime value)
    {
        Value = value;
    }

    /// <summary>
    /// Gets the date/time value to format.
    /// </summary>
    public DateTime Value { get; }
}

/// <summary>
/// Wraps a <see cref="DateTimeOffset"/> so normalization can preserve the original CLR type until the primitive encoder formats it.
/// </summary>
internal readonly struct DeferredDateTimeOffsetValue
{
    /// <summary>
    /// Initializes a new deferred <see cref="DateTimeOffset"/> value.
    /// </summary>
    /// <param name="value">The date/time offset value to format during primitive encoding.</param>
    public DeferredDateTimeOffsetValue(DateTimeOffset value)
    {
        Value = value;
    }

    /// <summary>
    /// Gets the date/time offset value to format.
    /// </summary>
    public DateTimeOffset Value { get; }
}

#if NET6_0_OR_GREATER
/// <summary>
/// Wraps a <see cref="DateOnly"/> so normalization can preserve the original CLR type until the primitive encoder formats it.
/// </summary>
internal readonly struct DeferredDateOnlyValue
{
    /// <summary>
    /// Initializes a new deferred <see cref="DateOnly"/> value.
    /// </summary>
    /// <param name="value">The date-only value to format during primitive encoding.</param>
    public DeferredDateOnlyValue(DateOnly value)
    {
        Value = value;
    }

    /// <summary>
    /// Gets the date-only value to format.
    /// </summary>
    public DateOnly Value { get; }
}

/// <summary>
/// Wraps a <see cref="TimeOnly"/> so normalization can preserve the original CLR type until the primitive encoder formats it.
/// </summary>
internal readonly struct DeferredTimeOnlyValue
{
    /// <summary>
    /// Initializes a new deferred <see cref="TimeOnly"/> value.
    /// </summary>
    /// <param name="value">The time-only value to format during primitive encoding.</param>
    public DeferredTimeOnlyValue(TimeOnly value)
    {
        Value = value;
    }

    /// <summary>
    /// Gets the time-only value to format.
    /// </summary>
    public TimeOnly Value { get; }
}
#endif

/// <summary>
/// Represents an ordered object node with ordinal key lookup and insertion-order enumeration.
/// </summary>
internal sealed class NativeObjectNode : NativeNode, IEnumerable<KeyValuePair<string, NativeNode?>>
{
    private readonly List<KeyValuePair<string, NativeNode?>> properties = new();
    private readonly Dictionary<string, int> propertyIndexes = new(StringComparer.Ordinal);

    /// <summary>
    /// Gets the number of properties in the object.
    /// </summary>
    public int Count => properties.Count;

    /// <summary>
    /// Enumerates property keys in insertion order.
    /// </summary>
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

    /// <summary>
    /// Gets or sets a property by key, preserving insertion order when replacing an existing value.
    /// </summary>
    /// <param name="key">The property key.</param>
    /// <returns>The stored property value.</returns>
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

    /// <summary>
    /// Returns whether the object contains the specified key.
    /// </summary>
    /// <param name="key">The property key to test.</param>
    /// <returns><see langword="true"/> when the key exists; otherwise, <see langword="false"/>.</returns>
    public bool ContainsKey(string key) => propertyIndexes.ContainsKey(key);

    /// <summary>
    /// Adds or replaces a property value.
    /// </summary>
    /// <param name="key">The property key.</param>
    /// <param name="value">The property value.</param>
    public void Add(string key, NativeNode? value)
    {
        this[key] = value;
    }

    /// <summary>
    /// Returns an insertion-order enumerator for object properties.
    /// </summary>
    /// <returns>An enumerator over key/value pairs.</returns>
    public IEnumerator<KeyValuePair<string, NativeNode?>> GetEnumerator() => properties.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>
/// Represents an ordered array node used for inline arrays, tabular arrays, and list-form arrays.
/// </summary>
internal sealed class NativeArrayNode : NativeNode, IEnumerable<NativeNode?>
{
    private readonly List<NativeNode?> items = new();

    /// <summary>
    /// Gets the number of items in the array.
    /// </summary>
    public int Count => items.Count;

    /// <summary>
    /// Gets the item at the specified zero-based index.
    /// </summary>
    /// <param name="index">The zero-based item index.</param>
    /// <returns>The item at the specified index.</returns>
    public NativeNode? this[int index] => items[index];

    /// <summary>
    /// Appends an item to the array.
    /// </summary>
    /// <param name="value">The item to append.</param>
    public void Add(NativeNode? value)
    {
        items.Add(value);
    }

    /// <summary>
    /// Returns an enumerator over array items in order.
    /// </summary>
    /// <returns>An enumerator over item nodes.</returns>
    public IEnumerator<NativeNode?> GetEnumerator() => items.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
