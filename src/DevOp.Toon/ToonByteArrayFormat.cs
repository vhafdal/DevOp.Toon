namespace DevOp.Toon;

/// <summary>
/// Controls how CLR <see cref="byte"/> arrays are represented in TOON.
/// </summary>
public enum ToonByteArrayFormat
{
    /// <summary>
    /// Encodes <see cref="byte"/> arrays as TOON primitive arrays of integers.
    /// </summary>
    NumericArray = 0,

    /// <summary>
    /// Encodes <see cref="byte"/> arrays as Base64 strings.
    /// </summary>
    Base64String = 1
}
