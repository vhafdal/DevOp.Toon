#nullable enable
using DevOp.Toon.Core;

namespace DevOp.Toon;

/// <summary>
/// Options for decoding TOON format strings.
/// </summary>
public class ToonDecodeOptions
{
    /// <summary>
    /// Number of spaces per indentation level.
    /// Default is 2.
    /// </summary>
    public int Indent { get; set; } = 2;

    /// <summary>
    /// When true, enforce strict validation of array lengths and tabular row counts.
    /// Default is true.
    /// </summary>
    public bool Strict { get; set; } = true;

    /// <summary>
    /// Controls path expansion for dotted keys.
    /// <see cref="ToonPathExpansion.Off" /> (default): Dotted keys are treated as literal keys.
    /// <see cref="ToonPathExpansion.Safe" />: Expand eligible dotted keys into nested objects.
    /// </summary>
    public ToonPathExpansion ExpandPaths { get; set; } = ToonPathExpansion.Off;

    /// <summary>
    /// Tracks the detected object-array layout.
    /// This is informational for auto-detection; the decoder already handles hybrid arrays automatically.
    /// Default is <see cref="ToonObjectArrayLayout.Auto"/>.
    /// </summary>
    public ToonObjectArrayLayout ObjectArrayLayout { get; set; } = ToonObjectArrayLayout.Auto;
}
