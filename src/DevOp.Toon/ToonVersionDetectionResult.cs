using DevOp.Toon.Core;

namespace DevOp.Toon;

/// <summary>Describes protocol selection without claiming to identify the producer's version.</summary>
public readonly struct ToonVersionDetectionResult
{
    internal ToonVersionDetectionResult(ToonProtocolVersion version, bool unique, bool requiresHint)
    {
        DetectedVersion = unique && !requiresHint ? version : null;
        SelectedVersion = requiresHint ? null : version;
        RequiresVersionHint = requiresHint;
    }
    /// <summary>Gets the version when syntax uniquely identifies a supported mode.</summary>
    public ToonProtocolVersion? DetectedVersion { get; }
    /// <summary>Gets the compatible decoder selection, or null when a hint is required.</summary>
    public ToonProtocolVersion? SelectedVersion { get; }
    /// <summary>Gets whether interpretations conflict and require an explicit version.</summary>
    public bool RequiresVersionHint { get; }
}
