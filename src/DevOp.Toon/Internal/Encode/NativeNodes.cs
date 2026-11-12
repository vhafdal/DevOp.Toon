#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

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
    /// Initializes a scalar node for a parsed number, keeping its source text when the parse may have rounded it.
    /// </summary>
    /// <param name="value">The parsed number.</param>
    /// <param name="numberText">The number's source text, or <see langword="null"/>.</param>
    public NativePrimitiveNode(object value, string? numberText)
    {
        Value = value;
        NumberText = numberText;
    }

    /// <summary>
    /// Gets the raw scalar value carried by the node.
    /// </summary>
    public object? Value { get; }

    /// <summary>
    /// Gets the source text of a number, when it was kept: typed materialization reads a decimal target from it exactly and
    /// judges from it whether the number is an integer, while every other use takes <see cref="Value"/>.
    /// </summary>
    public string? NumberText { get; }
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
    // Quoted dotted keys are rare and matter only to path expansion, so they live in a side table rather than in a field
    // that every object node, including those built by encoding, would carry.
    private static readonly ConditionalWeakTable<NativeObjectNode, HashSet<string>> LiteralKeys = new();

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
    /// Removes every property, keeping the storage for reuse.
    /// </summary>
    public void Clear()
    {
        properties.Clear();
        propertyIndexes.Clear();
    }

    /// <summary>
    /// Records whether <paramref name="key"/> was quoted in source, so path expansion keeps a quoted dotted key literal.
    /// The last occurrence of a repeated key decides. Only decoding with path expansion records this.
    /// </summary>
    /// <param name="key">The decoded dotted key.</param>
    /// <param name="quoted">Whether this occurrence was quoted.</param>
    /// <param name="inherited">Literal keys this object inherits from its table header, copied before the first change.</param>
    public void SetLiteralKey(string key, bool quoted, HashSet<string>? inherited = null)
    {
        if (!LiteralKeys.TryGetValue(this, out var keys))
        {
            if (!quoted && inherited?.Contains(key) != true)
                return;

            keys = LiteralKeys.GetValue(this, _ => inherited != null ? new HashSet<string>(inherited, StringComparer.Ordinal) : new HashSet<string>(StringComparer.Ordinal));
        }

        if (quoted)
            keys.Add(key);
        else
            keys.Remove(key);
    }

    /// <summary>
    /// Returns the literal keys recorded on this object, or <see langword="null"/> when it has none of its own.
    /// </summary>
    public HashSet<string>? GetLiteralKeys() => LiteralKeys.TryGetValue(this, out var keys) ? keys : null;

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
    // The quoted dotted fields of a table header, shared by all its rows instead of recorded on each row.
    private static readonly ConditionalWeakTable<NativeArrayNode, HashSet<string>> RowLiteralKeys = new();

    private readonly List<NativeNode?> items = new();

    /// <summary>
    /// Records the literal (quoted dotted) keys that every row object of this table inherits from its header.
    /// </summary>
    public void SetRowLiteralKeys(HashSet<string> keys) => RowLiteralKeys.Add(this, keys);

    /// <summary>
    /// Returns the literal keys the row objects of this table inherit, or <see langword="null"/> when there are none.
    /// </summary>
    public HashSet<string>? GetRowLiteralKeys() => RowLiteralKeys.TryGetValue(this, out var keys) ? keys : null;

    /// <summary>
    /// Replaces the item at <paramref name="index"/>.
    /// </summary>
    public void SetItem(int index, NativeNode? value) => items[index] = value;

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
