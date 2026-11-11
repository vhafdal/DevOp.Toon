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
        /// Returns whether a decimal number literal such as <c>1e2</c>, <c>-2.50e1</c> or <c>1e-29</c> denotes an integer,
        /// judged from its digits: parsing first can round a fraction such as <c>1e-29</c> or
        /// <c>1.00000000000000000000000000001</c> to an integer.
        /// </summary>
        public static bool IsIntegralNumberText(string text)
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

            return lastNonZero < integerDigits + exponent;
        }

        /// <summary>
        /// Converts a number with a fractional part to an integer type the way 4.x decoding does: it rounds to even into
        /// <see cref="byte"/>, <see cref="sbyte"/>, <see cref="short"/> and <see cref="ushort"/>, and is outside the range of
        /// every other integer type.
        /// </summary>
        public static object ConvertFractionToInteger(Type type, decimal value, string numberText)
        {
            if (type == typeof(byte) || type == typeof(sbyte) || type == typeof(short) || type == typeof(ushort))
            {
                try
                {
                    return Convert.ChangeType(value, type, System.Globalization.CultureInfo.InvariantCulture);
                }
                catch (OverflowException)
                {
                }
            }

            throw ToonFormatException.Syntax($"Number {numberText} is outside the range of {type.FullName}");
        }
    }
}
