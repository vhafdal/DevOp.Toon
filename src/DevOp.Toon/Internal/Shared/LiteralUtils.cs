#nullable enable
using System.Globalization;

namespace DevOp.Toon.Internal.Shared
{
    /// <summary>
    /// Literal judgment utilities, aligned with TypeScript version shared/literal-utils.ts.
    /// - IsBooleanOrNullLiteral: Determines if it is true/false/null
    /// - IsNumericLiteral: Determines if it is a numeric literal, rejecting invalid leading zero forms
    /// </summary>
    internal static class LiteralUtils
    {
        /// <summary>
        /// Checks whether the token is one of the reserved primitive literals: <c>true</c>, <c>false</c>, or <c>null</c>.
        /// </summary>
        /// <param name="token">The token to inspect.</param>
        /// <returns><see langword="true"/> when the token is a reserved literal.</returns>
        internal static bool IsBooleanOrNullLiteral(string token)
        {
            return string.Equals(token, Constants.TRUE_LITERAL, StringComparison.Ordinal)
                || string.Equals(token, Constants.FALSE_LITERAL, StringComparison.Ordinal)
                || string.Equals(token, Constants.NULL_LITERAL, StringComparison.Ordinal);
        }

        /// <summary>
        /// Checks whether the token span is one of the reserved primitive literals: <c>true</c>, <c>false</c>, or <c>null</c>.
        /// </summary>
        /// <param name="token">The token span to inspect.</param>
        /// <returns><see langword="true"/> when the token is a reserved literal.</returns>
        internal static bool IsBooleanOrNullLiteral(ReadOnlySpan<char> token)
        {
            return token.SequenceEqual(Constants.TRUE_LITERAL)
                || token.SequenceEqual(Constants.FALSE_LITERAL)
                || token.SequenceEqual(Constants.NULL_LITERAL);
        }

        /// <summary>
        /// Checks if the token is a valid numeric literal.
        /// </summary>
        /// <param name="token">The token to inspect.</param>
        /// <returns><see langword="true"/> when the token parses as a finite number and does not use an invalid leading-zero form.</returns>
        internal static bool IsNumericLiteral(string token)
        {
            if (string.IsNullOrEmpty(token))
                return false;

            // Must not have leading zeros (except "0" itself or decimals like "0.5")
            if (token.Length > 1 && token[0] == '0' && token[1] != '.')
                return false;

            if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var num))
                return false;

            return !double.IsNaN(num) && !double.IsInfinity(num);
        }

        /// <summary>
        /// Checks if the token span is a valid numeric literal.
        /// </summary>
        /// <param name="token">The token span to inspect.</param>
        /// <returns><see langword="true"/> when the token parses as a finite number and does not use an invalid leading-zero form.</returns>
        internal static bool IsNumericLiteral(ReadOnlySpan<char> token)
        {
            if (token.IsEmpty)
                return false;

            if (token.Length > 1 && token[0] == '0' && token[1] != '.')
                return false;

#if NETSTANDARD2_0
            if (!double.TryParse(token.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var num))
#else
            if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var num))
#endif
                return false;

            return !double.IsNaN(num) && !double.IsInfinity(num);
        }
    }
}
