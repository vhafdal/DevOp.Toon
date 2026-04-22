using System;

namespace DevOp.Toon.Internal.Shared
{
    /// <summary>
    /// Floating-point helpers used to compare numeric values and normalize values before canonical literal emission.
    /// </summary>
    internal static class FloatUtils
    {
        /// <summary>
        /// Compares two doubles using both absolute and relative tolerance.
        /// </summary>
        /// <param name="a">The first value to compare.</param>
        /// <param name="b">The second value to compare.</param>
        /// <param name="absEps">The absolute tolerance used near zero.</param>
        /// <param name="relEps">The relative tolerance used for larger magnitudes.</param>
        /// <returns><see langword="true"/> when the values are equal within tolerance.</returns>
        public static bool NearlyEqual(double a, double b, double absEps = 1e-12, double relEps = 1e-9)
        {
            if (double.IsNaN(a) && double.IsNaN(b)) return true;
            if (double.IsInfinity(a) || double.IsInfinity(b)) return a.Equals(b);
            if (a == b) return true;

            var diff = Math.Abs(a - b);
            var scale = Math.Max(Math.Abs(a), Math.Abs(b));
            if (scale == 0) return diff <= absEps;
            return diff <= Math.Max(absEps, relEps * scale);
        }

        /// <summary>
        /// Explicitly change -0.0 to +0.0 to avoid exposing the sign difference in subsequent operations such as 1.0/x.
        /// </summary>
        /// <param name="v">The value to normalize.</param>
        /// <returns>Positive zero when <paramref name="v"/> is negative zero; otherwise, <paramref name="v"/>.</returns>
        public static double NormalizeSignedZero(double v) =>
            BitConverter.DoubleToInt64Bits(v) == BitConverter.DoubleToInt64Bits(-0.0) ? 0.0 : v;

        /// <summary>
        /// Explicitly change -0.0f to +0.0f for float values.
        /// </summary>
        /// <param name="v">The value to normalize.</param>
        /// <returns>Positive zero when <paramref name="v"/> is negative zero; otherwise, <paramref name="v"/>.</returns>
        public static float NormalizeSignedZero(float v)
        {
#if NETSTANDARD2_0
            unsafe
            {
                int* vBits = (int*)&v;
                float negZero = -0.0f;
                int* zeroBits = (int*)&negZero;
                return *vBits == *zeroBits ? 0.0f : v;
            }
#else
            return BitConverter.SingleToInt32Bits(v) == BitConverter.SingleToInt32Bits(-0.0f) ? 0.0f : v;
#endif
        }
    }
}
