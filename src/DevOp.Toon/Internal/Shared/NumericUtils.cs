using System;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace DevOp.Toon.Internal.Shared
{
    /// <summary>
    /// Numeric helpers for canonical decimal emission and target-framework-independent finite-number checks.
    /// </summary>
    internal static class NumericUtils
    {
        /// <summary>
        /// Converts a double to a decimal in canonical form for accurate representation.
        /// </summary>
        /// <param name="value">The input double value</param>
        /// <returns>A decimal representation of the input value.</returns>
        /// <remarks>https://github.com/toon-format/spec/blob/main/SPEC.md#2-data-model</remarks>
        /// <example>1e-7 => 0.0000001</example>
        public static decimal EmitCanonicalDecimalForm(double value)
        {
            if (!TryEmitCanonicalDecimalForm(value, out var decimalValue))
            {
                throw new OverflowException("The value cannot be represented in canonical decimal form.");
            }

            return decimalValue;
        }

        /// <summary>
        /// Attempts to convert a double to a decimal in canonical form without throwing on overflow.
        /// </summary>
        /// <param name="value">The input double value.</param>
        /// <param name="decimalValue">The canonical decimal value when conversion succeeds.</param>
        /// <returns><see langword="true"/> when the value can be represented as a decimal.</returns>
        public static bool TryEmitCanonicalDecimalForm(double value, out decimal decimalValue)
        {
            var scientificString = value.ToString("G17");
            var match = Regex.Match(scientificString, @"e[-+]\d+", RegexOptions.IgnoreCase);

            if (!match.Success)
            {
                try
                {
                    decimalValue = (decimal)value;
                    return true;
                }
                catch (OverflowException)
                {
                    decimalValue = default;
                    return false;
                }
            }

            // The match is the exponent part, e.g., "E+04"
            var exponentPart = match.Value;

            // Remove the 'E' or 'e' and the sign to get just the digits
            var exponentDigits = exponentPart.Substring(2);

            // Parse the actual exponent value (4 in this example)
            var exponent = int.Parse(exponentDigits);

            // You also need to check the sign to determine if it's positive or negative
            if (exponentPart.Contains('-'))
            {
                exponent = -exponent;
            }

            var mantissa =
                scientificString.Substring(0, scientificString.IndexOf(match.Value, StringComparison.Ordinal));

            decimal parsedMantissa;
            try
            {
                parsedMantissa = decimal.Parse(mantissa);
            }
            catch (OverflowException)
            {
                decimalValue = default;
                return false;
            }

            if (exponent == 0) exponent++;

            try
            {
                parsedMantissa *= (decimal)Math.Pow(10, exponent);
            }
            catch (OverflowException)
            {
                decimalValue = default;
                return false;
            }

            decimalValue = parsedMantissa;
            return true;
        }

        /// <summary>
        /// Returns whether a double is neither NaN nor infinity.
        /// </summary>
        /// <param name="value">The value to inspect.</param>
        /// <returns><see langword="true"/> when the value is finite.</returns>
        public static bool IsFinite(double value)
        {
#if NETSTANDARD2_0
            return !(double.IsNaN(value) || double.IsInfinity(value));
#else
            return double.IsFinite(value);
#endif
        }

        /// <summary>
        /// Returns whether a float is neither NaN nor infinity.
        /// </summary>
        /// <param name="value">The value to inspect.</param>
        /// <returns><see langword="true"/> when the value is finite.</returns>
        public static bool IsFinite(float value)
        {
#if NETSTANDARD2_0
            return !(float.IsNaN(value) || float.IsInfinity(value));
#else
            return float.IsFinite(value);
#endif
        }

        /// <summary>
        /// Returns whether text has the shape of a decimal number (<c>-12</c>, <c>0e-2</c>, <c>007</c>, <c>1e400</c>), whatever
        /// its magnitude or leading zeros.
        /// </summary>
        public static bool IsNumberShaped(string text) => IsNumberShaped(text.AsSpan());

        /// <summary>
        /// Returns whether text has the shape of a decimal number; see <see cref="IsNumberShaped(string)"/>.
        /// </summary>
        public static bool IsNumberShaped(ReadOnlySpan<char> text)
        {
            var i = text.Length > 0 && text[0] == '-' ? 1 : 0;
            if (SkipDigits(text, ref i) == 0)
            {
                return false;
            }

            if (i < text.Length && text[i] == '.')
            {
                i++;
                if (SkipDigits(text, ref i) == 0)
                {
                    return false;
                }
            }

            if (i < text.Length && (text[i] == 'e' || text[i] == 'E'))
            {
                i++;
                if (i < text.Length && (text[i] == '+' || text[i] == '-'))
                {
                    i++;
                }

                if (SkipDigits(text, ref i) == 0)
                {
                    return false;
                }
            }

            return i == text.Length;
        }

        private static int SkipDigits(ReadOnlySpan<char> text, ref int index)
        {
            var start = index;
            while (index < text.Length && text[index] >= '0' && text[index] <= '9')
            {
                index++;
            }

            return index - start;
        }

        /// <summary>
        /// Returns whether a decimal number literal such as <c>1e2</c>, <c>-2.50e1</c> or <c>1e-29</c> denotes an integer,
        /// judged from its digits: parsing first can round a fraction such as <c>1e-29</c> or
        /// <c>1.00000000000000000000000000001</c> to an integer.
        /// </summary>
        public static bool IsIntegralNumberText(string text) => IsIntegralNumberText(text.AsSpan());

        /// <summary>
        /// Returns whether a decimal number literal denotes an integer; see <see cref="IsIntegralNumberText(string)"/>.
        /// </summary>
        public static bool IsIntegralNumberText(ReadOnlySpan<char> text)
        {
            var i = text.Length > 0 && (text[0] == '-' || text[0] == '+') ? 1 : 0;
            var integerDigits = 0;
            var digitCount = 0;
            var lastNonZero = -1;
            var seenPoint = false;
            for (; i < text.Length; i++)
            {
                var c = text[i];
                if (c == '.')
                {
                    seenPoint = true;
                    continue;
                }

                if (c < '0' || c > '9')
                {
                    break;
                }

                if (c != '0')
                {
                    lastNonZero = digitCount;
                }

                digitCount++;
                if (!seenPoint)
                {
                    integerDigits++;
                }
            }

            long exponent = 0;
            if (i < text.Length && (text[i] == 'e' || text[i] == 'E'))
            {
                i++;
                var negative = i < text.Length && text[i] == '-';
                if (i < text.Length && (text[i] == '-' || text[i] == '+'))
                {
                    i++;
                }

                for (; i < text.Length && text[i] >= '0' && text[i] <= '9'; i++)
                {
                    exponent = Math.Min(exponent * 10 + (text[i] - '0'), 1_000_000_000L);
                }

                if (negative)
                {
                    exponent = -exponent;
                }
            }

            // Every digit zero is exactly zero, whatever the exponent.
            return lastNonZero < 0 || lastNonZero < integerDigits + exponent;
        }

        /// <summary>
        /// Returns whether number-shaped text such as <c>1e400</c> or <c>-12.5</c> has a magnitude of at least one, judged
        /// from its digits so that it holds beyond the range of every numeric type.
        /// </summary>
        public static bool IsAtLeastOne(ReadOnlySpan<char> text)
        {
            var i = text.Length > 0 && text[0] == '-' ? 1 : 0;
            long leading = long.MinValue;
            long position = 0;
            for (; i < text.Length && text[i] >= '0' && text[i] <= '9'; i++)
            {
                if (leading == long.MinValue && text[i] != '0') leading = 0;
                else if (leading != long.MinValue) leading++;
            }

            if (i < text.Length && text[i] == '.')
            {
                for (i++; i < text.Length && text[i] >= '0' && text[i] <= '9'; i++)
                {
                    position--;
                    if (leading == long.MinValue && text[i] != '0') leading = position;
                }
            }

            if (leading == long.MinValue) return false;
            long exponent = 0;
            if (i < text.Length && (text[i] == 'e' || text[i] == 'E'))
            {
                i++;
                var negative = i < text.Length && text[i] == '-';
                if (i < text.Length && (text[i] == '-' || text[i] == '+')) i++;
                for (; i < text.Length && text[i] >= '0' && text[i] <= '9'; i++)
                    exponent = Math.Min(exponent * 10 + (text[i] - '0'), 1_000_000_000L);
                if (negative) exponent = -exponent;
            }

            return leading + exponent >= 0;
        }

        /// <summary>
        /// The message for a number with a fractional part bound to an integer or enum type.
        /// </summary>
        public static string NotAnInteger(string numberText, Type type) => $"Number {numberText} is not an integer ({type.FullName})";

        /// <summary>
        /// The message for a number outside the range of the type it is bound to.
        /// </summary>
        public static string OutsideRange(string numberText, Type type) => $"Number {numberText} is outside the range of {type.FullName}";

        /// <summary>
        /// Binds number text to a signed integer type with range <paramref name="min"/> to <paramref name="max"/>: any literal
        /// whose value is an integer in range binds (<c>1e3</c>, <c>2.0</c>, <c>-0.0</c>); a fraction never rounds.
        /// </summary>
        public static NumberBinding TryBindSigned(ReadOnlySpan<char> text, long min, long max, out long value)
        {
            value = 0;
            var result = ReadInteger(text, out var negative, out var magnitude);
            if (result != NumberBinding.Bound) return result;
            if (negative)
            {
                if (magnitude > unchecked((ulong)-(min + 1)) + 1) return NumberBinding.OutOfRange;
                value = unchecked(-(long)magnitude);
            }
            else
            {
                if (magnitude > (ulong)max) return NumberBinding.OutOfRange;
                value = (long)magnitude;
            }

            return NumberBinding.Bound;
        }

        /// <summary>
        /// Binds number text to an unsigned integer type with maximum <paramref name="max"/>; see
        /// <see cref="TryBindSigned"/>. <c>-0</c> and <c>-0.0</c> bind as zero.
        /// </summary>
        public static NumberBinding TryBindUnsigned(ReadOnlySpan<char> text, ulong max, out ulong value)
        {
            value = 0;
            var result = ReadInteger(text, out var negative, out var magnitude);
            if (result != NumberBinding.Bound) return result;
            if (negative || magnitude > max) return NumberBinding.OutOfRange;
            value = magnitude;
            return NumberBinding.Bound;
        }

        /// <summary>
        /// Binds number text to <see cref="double"/>, parsing the text once; a result outside the finite range never binds and
        /// negative zero binds as zero.
        /// </summary>
        public static NumberBinding TryBindDouble(ReadOnlySpan<char> text, out double value)
        {
#if NETSTANDARD2_0
            var parsed = double.TryParse(text.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out value);
#else
            var parsed = double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out value);
#endif
            if (!IsNumberShaped(text)) return NumberBinding.Invalid;
            // .NET Framework fails the parse on overflow where .NET returns an infinity.
            if (!parsed || !IsFinite(value)) return NumberBinding.OutOfRange;
            if (value == 0) value = 0d;
            return NumberBinding.Bound;
        }

        /// <summary>
        /// Binds number text to <see cref="float"/>, rounding the text once rather than through a double; see
        /// <see cref="TryBindDouble"/>.
        /// </summary>
        public static NumberBinding TryBindSingle(ReadOnlySpan<char> text, out float value)
        {
#if NETSTANDARD2_0
            var parsed = float.TryParse(text.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out value);
#else
            var parsed = float.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out value);
#endif
            if (!IsNumberShaped(text)) return NumberBinding.Invalid;
            if (!parsed || !IsFinite(value)) return NumberBinding.OutOfRange;
            if (value == 0) value = 0f;
            return NumberBinding.Bound;
        }

        /// <summary>
        /// Binds number text to <see cref="decimal"/> with the scale the text gives (<c>1.10</c>, <c>0.0</c>).
        /// </summary>
        public static NumberBinding TryBindDecimal(ReadOnlySpan<char> text, out decimal value)
        {
            if (!IsNumberShaped(text))
            {
                value = default;
                return NumberBinding.Invalid;
            }

#if NETSTANDARD2_0
            var parsed = decimal.TryParse(text.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out value);
#else
            var parsed = decimal.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out value);
#endif
            return parsed ? NumberBinding.Bound : NumberBinding.OutOfRange;
        }

        /// <summary>
        /// Reads number text as an exact integer magnitude and sign. Plain digits take a loop without parsing or allocation;
        /// other forms are judged from their digits before an exact decimal parse.
        /// </summary>
        private static NumberBinding ReadInteger(ReadOnlySpan<char> text, out bool negative, out ulong magnitude)
        {
            negative = text.Length > 0 && text[0] == '-';
            magnitude = 0;
            var i = negative ? 1 : 0;
            var start = i;
            var overflow = false;
            for (; i < text.Length; i++)
            {
                var digit = (uint)(text[i] - '0');
                if (digit > 9) break;
                if (magnitude > (ulong.MaxValue - digit) / 10) overflow = true;
                else magnitude = magnitude * 10 + digit;
            }

            if (i == text.Length && i > start)
            {
                if (overflow) return NumberBinding.OutOfRange;
                if (magnitude == 0) negative = false;
                return NumberBinding.Bound;
            }

            magnitude = 0;
            negative = false;
            if (!IsNumberShaped(text)) return NumberBinding.Invalid;
            if (!IsIntegralNumberText(text)) return NumberBinding.Fraction;
            if (IsZeroNumberText(text)) return NumberBinding.Bound;
#if NETSTANDARD2_0
            var parsed = decimal.TryParse(text.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var exact);
#else
            var parsed = decimal.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var exact);
#endif
            if (!parsed) return NumberBinding.OutOfRange;
            var absolute = Math.Abs(exact);
            if (absolute > ulong.MaxValue) return NumberBinding.OutOfRange;
            negative = exact < 0;
            magnitude = (ulong)absolute;
            return NumberBinding.Bound;
        }

        private static bool IsZeroNumberText(ReadOnlySpan<char> text)
        {
            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];
                if (c == 'e' || c == 'E') return true;
                if (c >= '1' && c <= '9') return false;
            }

            return true;
        }
    }

    /// <summary>
    /// The outcome of binding number text to a numeric type.
    /// </summary>
    internal enum NumberBinding : byte
    {
        /// <summary>The value fits the type.</summary>
        Bound,

        /// <summary>The text is not a number.</summary>
        Invalid,

        /// <summary>The number has a fractional part and the type is an integer type.</summary>
        Fraction,

        /// <summary>The number is outside the range, or the finite range, of the type.</summary>
        OutOfRange
    }
}
