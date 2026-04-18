#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using DevOp.Toon.Internal.Shared;

namespace DevOp.Toon;

/// <summary>
/// Represents a node in the TOON object model.
/// </summary>
public abstract class ToonNode
{
    /// <summary>
    /// Gets or sets the object property with the specified key.
    /// </summary>
    /// <param name="key">The property name.</param>
    /// <returns>The matching child node, or <see langword="null"/> when the property does not exist.</returns>
    public virtual ToonNode? this[string key]
    {
        get => AsObject()[key];
        set => throw new InvalidOperationException($"Node is not an object: {GetType().Name}.");
    }

    /// <summary>
    /// Gets the array item at the specified zero-based index.
    /// </summary>
    /// <param name="index">The zero-based item index.</param>
    /// <returns>The child node at the specified index.</returns>
    public virtual ToonNode? this[int index] => AsArray()[index];

    /// <summary>
    /// Returns this node as a <see cref="ToonObject"/>.
    /// </summary>
    /// <returns>This node cast to <see cref="ToonObject"/>.</returns>
    /// <exception cref="InvalidOperationException">This node is not an object.</exception>
    public virtual ToonObject AsObject()
    {
        throw new InvalidOperationException($"Node is not an object: {GetType().Name}.");
    }

    /// <summary>
    /// Returns this node as a <see cref="ToonArray"/>.
    /// </summary>
    /// <returns>This node cast to <see cref="ToonArray"/>.</returns>
    /// <exception cref="InvalidOperationException">This node is not an array.</exception>
    public virtual ToonArray AsArray()
    {
        throw new InvalidOperationException($"Node is not an array: {GetType().Name}.");
    }

    /// <summary>
    /// Returns this node as a <see cref="ToonValue"/>.
    /// </summary>
    /// <returns>This node cast to <see cref="ToonValue"/>.</returns>
    /// <exception cref="InvalidOperationException">This node is not a scalar value.</exception>
    public virtual ToonValue AsValue()
    {
        throw new InvalidOperationException($"Node is not a value: {GetType().Name}.");
    }

    /// <summary>
    /// Gets the current scalar value converted to the specified type.
    /// </summary>
    /// <typeparam name="T">The requested CLR type.</typeparam>
    /// <returns>The converted scalar value.</returns>
    public virtual T GetValue<T>()
    {
        return AsValue().GetValue<T>();
    }

    /// <summary>
    /// Parses a JSON string into the corresponding TOON node graph.
    /// </summary>
    /// <param name="json">The JSON payload to parse.</param>
    /// <returns>The parsed node graph, or <see langword="null"/> when the payload is the JSON literal <c>null</c>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="json"/> is <see langword="null"/>.</exception>
    /// <exception cref="FormatException">The JSON payload is invalid.</exception>
    public static ToonNode? Parse(string json)
    {
        if (json == null)
        {
            throw new ArgumentNullException(nameof(json));
        }

        return new JsonParser(json).Parse();
    }

    /// <summary>
    /// Compares two node graphs for deep structural equality.
    /// </summary>
    /// <param name="left">The first node to compare.</param>
    /// <param name="right">The second node to compare.</param>
    /// <returns><see langword="true"/> when both graphs contain the same structure and values; otherwise, <see langword="false"/>.</returns>
    public static bool DeepEquals(ToonNode? left, ToonNode? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null)
        {
            return false;
        }

        if (left is ToonValue leftValue && right is ToonValue rightValue)
        {
            return ValuesEqual(leftValue.Value, rightValue.Value);
        }

        if (left is ToonArray leftArray && right is ToonArray rightArray)
        {
            if (leftArray.Count != rightArray.Count)
            {
                return false;
            }

            for (int i = 0; i < leftArray.Count; i++)
            {
                if (!DeepEquals(leftArray[i], rightArray[i]))
                {
                    return false;
                }
            }

            return true;
        }

        if (left is ToonObject leftObject && right is ToonObject rightObject)
        {
            if (leftObject.Count != rightObject.Count)
            {
                return false;
            }

            using var leftEnumerator = leftObject.GetEnumerator();
            using var rightEnumerator = rightObject.GetEnumerator();
            while (leftEnumerator.MoveNext() && rightEnumerator.MoveNext())
            {
                if (!string.Equals(leftEnumerator.Current.Key, rightEnumerator.Current.Key, StringComparison.Ordinal))
                {
                    return false;
                }

                if (!DeepEquals(leftEnumerator.Current.Value, rightEnumerator.Current.Value))
                {
                    return false;
                }
            }

            return true;
        }

        return false;
    }

    private static bool ValuesEqual(object? left, object? right)
    {
        if (left is null || right is null)
        {
            return left is null && right is null;
        }

        if (TryGetNumericValue(left, out var leftNumber) && TryGetNumericValue(right, out var rightNumber))
        {
            return leftNumber == rightNumber;
        }

        return Equals(left, right);
    }

    private static bool TryGetNumericValue(object value, out decimal numericValue)
    {
        switch (value)
        {
            case byte byteValue:
                numericValue = byteValue;
                return true;
            case sbyte sbyteValue:
                numericValue = sbyteValue;
                return true;
            case short shortValue:
                numericValue = shortValue;
                return true;
            case ushort ushortValue:
                numericValue = ushortValue;
                return true;
            case int intValue:
                numericValue = intValue;
                return true;
            case uint uintValue:
                numericValue = uintValue;
                return true;
            case long longValue:
                numericValue = longValue;
                return true;
            case ulong ulongValue:
                numericValue = ulongValue;
                return true;
            case decimal decimalValue:
                numericValue = decimalValue;
                return true;
            case float floatValue:
                numericValue = Convert.ToDecimal(floatValue, CultureInfo.InvariantCulture);
                return true;
            case double doubleValue:
                numericValue = Convert.ToDecimal(doubleValue, CultureInfo.InvariantCulture);
                return true;
            default:
                numericValue = default;
                return false;
        }
    }

    private sealed class JsonParser
    {
        private readonly string json;
        private int index;

        public JsonParser(string json)
        {
            this.json = json;
        }

        public ToonNode? Parse()
        {
            SkipWhitespace();
            var node = ParseValue();
            SkipWhitespace();
            if (index != json.Length)
            {
                throw new FormatException("Unexpected characters after JSON value.");
            }

            return node;
        }

        private ToonNode? ParseValue()
        {
            SkipWhitespace();
            if (index >= json.Length)
            {
                throw new FormatException("Unexpected end of JSON input.");
            }

            return json[index] switch
            {
                '{' => ParseObject(),
                '[' => ParseArray(),
                '"' => new ToonValue(ParseString()),
                't' => ParseLiteral("true", true),
                'f' => ParseLiteral("false", false),
                'n' => ParseLiteral("null", null),
                _ => ParseNumberOrInvalid()
            };
        }

        private ToonObject ParseObject()
        {
            index++;
            var result = new ToonObject();
            SkipWhitespace();
            if (ConsumeIf('}'))
            {
                return result;
            }

            while (true)
            {
                SkipWhitespace();
                var key = ParseString();
                SkipWhitespace();
                Expect(':');
                result[key] = ParseValue();
                SkipWhitespace();
                if (ConsumeIf('}'))
                {
                    return result;
                }

                Expect(',');
            }
        }

        private ToonArray ParseArray()
        {
            index++;
            var result = new ToonArray();
            SkipWhitespace();
            if (ConsumeIf(']'))
            {
                return result;
            }

            while (true)
            {
                result.Add(ParseValue());
                SkipWhitespace();
                if (ConsumeIf(']'))
                {
                    return result;
                }

                Expect(',');
            }
        }

        private ToonNode? ParseLiteral(string literal, object? value)
        {
            if (!json.AsSpan(index).StartsWith(literal.AsSpan(), StringComparison.Ordinal))
            {
                throw new FormatException($"Invalid JSON literal at position {index}.");
            }

            index += literal.Length;
            return value is null ? null : new ToonValue(value);
        }

        private ToonNode ParseNumberOrInvalid()
        {
            var start = index;
            if (json[index] == '-')
            {
                index++;
            }

            while (index < json.Length && char.IsDigit(json[index]))
            {
                index++;
            }

            if (index < json.Length && json[index] == '.')
            {
                index++;
                while (index < json.Length && char.IsDigit(json[index]))
                {
                    index++;
                }
            }

            if (index < json.Length && (json[index] == 'e' || json[index] == 'E'))
            {
                index++;
                if (index < json.Length && (json[index] == '+' || json[index] == '-'))
                {
                    index++;
                }

                while (index < json.Length && char.IsDigit(json[index]))
                {
                    index++;
                }
            }

            var token = json.Substring(start, index - start);
            if (token.Length == 0)
            {
                throw new FormatException($"Invalid JSON token at position {start}.");
            }

            var parsedNumber = double.Parse(token, CultureInfo.InvariantCulture);
            parsedNumber = FloatUtils.NormalizeSignedZero(parsedNumber);
            if (parsedNumber < 1e-6 || parsedNumber > 1e6)
            {
                if (NumericUtils.TryEmitCanonicalDecimalForm(parsedNumber, out var decimalValue))
                {
                    return new ToonValue(decimalValue);
                }
            }

            return new ToonValue(parsedNumber);
        }

        private string ParseString()
        {
            Expect('"');
            var start = index - 1;
            var closingQuoteIndex = StringUtils.FindClosingQuote(json.AsSpan(start), 0);
            if (closingQuoteIndex == -1)
            {
                throw new FormatException("Unterminated JSON string.");
            }

            var contentStart = start + 1;
            var contentLength = closingQuoteIndex - 1;
            var value = UnescapeJsonString(json.AsSpan(contentStart, contentLength));
            index = start + closingQuoteIndex + 1;
            return value;
        }

        private static string UnescapeJsonString(ReadOnlySpan<char> value)
        {
            int slashIndex = value.IndexOf('\\');
            if (slashIndex < 0)
            {
                return value.ToString();
            }

            var builder = new StringBuilder(value.Length);
            builder.Append(value.Slice(0, slashIndex).ToString());

            for (int i = slashIndex; i < value.Length; i++)
            {
                char current = value[i];
                if (current != '\\')
                {
                    builder.Append(current);
                    continue;
                }

                if (++i >= value.Length)
                {
                    throw new FormatException("Unterminated JSON escape sequence.");
                }

                switch (value[i])
                {
                    case '"':
                        builder.Append('"');
                        break;
                    case '\\':
                        builder.Append('\\');
                        break;
                    case '/':
                        builder.Append('/');
                        break;
                    case 'b':
                        builder.Append('\b');
                        break;
                    case 'f':
                        builder.Append('\f');
                        break;
                    case 'n':
                        builder.Append('\n');
                        break;
                    case 'r':
                        builder.Append('\r');
                        break;
                    case 't':
                        builder.Append('\t');
                        break;
                    case 'u':
                        builder.Append(ParseUnicodeEscape(value, ref i));
                        break;
                    default:
                        throw new FormatException($"Invalid JSON escape sequence: \\{value[i]}");
                }
            }

            return builder.ToString();
        }

        private static string ParseUnicodeEscape(ReadOnlySpan<char> value, ref int index)
        {
            if (index + 4 >= value.Length)
            {
                throw new FormatException("Incomplete JSON unicode escape.");
            }

            int codePoint = ParseHex(value.Slice(index + 1, 4));
            index += 4;

            if (!char.IsHighSurrogate((char)codePoint))
            {
                return ((char)codePoint).ToString();
            }

            if (index + 6 >= value.Length || value[index + 1] != '\\' || value[index + 2] != 'u')
            {
                throw new FormatException("Expected low surrogate JSON unicode escape.");
            }

            int lowSurrogate = ParseHex(value.Slice(index + 3, 4));
            if (!char.IsLowSurrogate((char)lowSurrogate))
            {
                throw new FormatException("Invalid low surrogate JSON unicode escape.");
            }

            index += 6;
            return new string(new[] { (char)codePoint, (char)lowSurrogate });
        }

        private static int ParseHex(ReadOnlySpan<char> value)
        {
            int result = 0;
            for (int i = 0; i < value.Length; i++)
            {
                result <<= 4;
                char ch = value[i];
                if (ch >= '0' && ch <= '9')
                {
                    result += ch - '0';
                }
                else if (ch >= 'a' && ch <= 'f')
                {
                    result += ch - 'a' + 10;
                }
                else if (ch >= 'A' && ch <= 'F')
                {
                    result += ch - 'A' + 10;
                }
                else
                {
                    throw new FormatException($"Invalid JSON unicode escape: {value.ToString()}");
                }
            }

            return result;
        }

        private void SkipWhitespace()
        {
            while (index < json.Length && char.IsWhiteSpace(json[index]))
            {
                index++;
            }
        }

        private bool ConsumeIf(char ch)
        {
            if (index < json.Length && json[index] == ch)
            {
                index++;
                return true;
            }

            return false;
        }

        private void Expect(char ch)
        {
            if (index >= json.Length || json[index] != ch)
            {
                throw new FormatException($"Expected '{ch}' at position {index}.");
            }

            index++;
        }
    }
}

/// <summary>
/// Represents an ordered collection of named TOON properties.
/// </summary>
public sealed class ToonObject : ToonNode, IEnumerable<KeyValuePair<string, ToonNode?>>
{
    private readonly List<KeyValuePair<string, ToonNode?>> properties = new();

    /// <summary>
    /// Gets the number of properties in the object.
    /// </summary>
    public int Count => properties.Count;

    /// <summary>
    /// Returns this instance as a <see cref="ToonObject"/>.
    /// </summary>
    /// <returns>This instance.</returns>
    public override ToonObject AsObject() => this;

    /// <summary>
    /// Gets or sets the property with the specified key.
    /// </summary>
    /// <param name="key">The property name.</param>
    /// <returns>The property value, or <see langword="null"/> when the key does not exist.</returns>
    public override ToonNode? this[string key]
    {
        get
        {
            for (int i = 0; i < properties.Count; i++)
            {
                if (string.Equals(properties[i].Key, key, StringComparison.Ordinal))
                {
                    return properties[i].Value;
                }
            }

            return null;
        }
        set
        {
            for (int i = 0; i < properties.Count; i++)
            {
                if (string.Equals(properties[i].Key, key, StringComparison.Ordinal))
                {
                    properties[i] = new KeyValuePair<string, ToonNode?>(key, value);
                    return;
                }
            }

            properties.Add(new KeyValuePair<string, ToonNode?>(key, value));
        }
    }

    /// <summary>
    /// Adds or replaces a property on the object.
    /// </summary>
    /// <param name="key">The property name.</param>
    /// <param name="value">The property value.</param>
    public void Add(string key, ToonNode? value)
    {
        this[key] = value;
    }

    /// <summary>
    /// Returns an enumerator that iterates through the object properties in insertion order.
    /// </summary>
    /// <returns>An enumerator for the object properties.</returns>
    public IEnumerator<KeyValuePair<string, ToonNode?>> GetEnumerator() => properties.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>
/// Represents an ordered collection of TOON nodes.
/// </summary>
public sealed class ToonArray : ToonNode, IEnumerable<ToonNode?>
{
    private readonly List<ToonNode?> items = new();

    /// <summary>
    /// Gets the number of items in the array.
    /// </summary>
    public int Count => items.Count;

    /// <summary>
    /// Returns this instance as a <see cref="ToonArray"/>.
    /// </summary>
    /// <returns>This instance.</returns>
    public override ToonArray AsArray() => this;

    /// <summary>
    /// Gets the array item at the specified zero-based index.
    /// </summary>
    /// <param name="index">The zero-based item index.</param>
    /// <returns>The node at the specified index.</returns>
    public override ToonNode? this[int index] => items[index];

    /// <summary>
    /// Appends an item to the end of the array.
    /// </summary>
    /// <param name="value">The value to add.</param>
    public void Add(ToonNode? value)
    {
        items.Add(value);
    }

    /// <summary>
    /// Returns an enumerator that iterates through the array items in order.
    /// </summary>
    /// <returns>An enumerator for the array items.</returns>
    public IEnumerator<ToonNode?> GetEnumerator() => items.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>
/// Represents a scalar TOON value.
/// </summary>
public sealed class ToonValue : ToonNode
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ToonValue"/> class.
    /// </summary>
    /// <param name="value">The wrapped scalar value.</param>
    public ToonValue(object? value)
    {
        Value = value;
    }

    /// <summary>
    /// Gets the wrapped scalar value.
    /// </summary>
    public object? Value { get; }

    /// <summary>
    /// Returns this instance as a <see cref="ToonValue"/>.
    /// </summary>
    /// <returns>This instance.</returns>
    public override ToonValue AsValue() => this;

    /// <summary>
    /// Gets the wrapped value converted to the specified type.
    /// </summary>
    /// <typeparam name="T">The requested CLR type.</typeparam>
    /// <returns>The converted value.</returns>
    public override T GetValue<T>()
    {
        if (Value is null)
        {
            return default!;
        }

        if (Value is T typedValue)
        {
            return typedValue;
        }

        var targetType = typeof(T);
        if (targetType == typeof(string))
        {
            return (T)(object)Convert.ToString(Value, CultureInfo.InvariantCulture)!;
        }

        if (targetType == typeof(double))
        {
            return (T)(object)Convert.ToDouble(Value, CultureInfo.InvariantCulture);
        }

        if (targetType == typeof(float))
        {
            return (T)(object)Convert.ToSingle(Value, CultureInfo.InvariantCulture);
        }

        if (targetType == typeof(decimal))
        {
            return (T)(object)Convert.ToDecimal(Value, CultureInfo.InvariantCulture);
        }

        if (targetType == typeof(int))
        {
            return (T)(object)Convert.ToInt32(Value, CultureInfo.InvariantCulture);
        }

        if (targetType == typeof(long))
        {
            return (T)(object)Convert.ToInt64(Value, CultureInfo.InvariantCulture);
        }

        if (targetType == typeof(bool))
        {
            return (T)(object)Convert.ToBoolean(Value, CultureInfo.InvariantCulture);
        }

        if (targetType == typeof(byte))
        {
            return (T)(object)Convert.ToByte(Value, CultureInfo.InvariantCulture);
        }

        if (targetType == typeof(short))
        {
            return (T)(object)Convert.ToInt16(Value, CultureInfo.InvariantCulture);
        }

        if (targetType == typeof(uint))
        {
            return (T)(object)Convert.ToUInt32(Value, CultureInfo.InvariantCulture);
        }

        return (T)Convert.ChangeType(Value, targetType, CultureInfo.InvariantCulture);
    }
}
