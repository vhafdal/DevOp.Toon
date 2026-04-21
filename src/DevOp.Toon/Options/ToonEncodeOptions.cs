#nullable enable
using DevOp.Toon.Core;
using DevOp.Toon;
using DevOp.Toon.Internal.Encode;

namespace DevOp.Toon;

/// <summary>
/// Options for encoding data to TOON format.
/// </summary>
public class ToonEncodeOptions
{
    private int indent = 1;
    private ToonDelimiter delimiter = Constants.DEFAULT_DELIMITER_ENUM;
    private ToonKeyFolding keyFolding = ToonKeyFolding.Off;
    private int? flattenDepth = int.MaxValue;
    private ToonObjectArrayLayout objectArrayLayout = ToonObjectArrayLayout.Columnar;
    private bool ignoreNullOrEmpty = true;
    private bool excludeEmptyArrays = true;
    private ResolvedEncodeOptions? cachedResolvedOptions;

    /// <summary>
    /// Number of spaces per indentation level.
    /// </summary>
    /// <remarks>Default is 1</remarks>
    public int Indent
    {
        get => indent;
        set
        {
            if (indent == value)
                return;

            indent = value;
            cachedResolvedOptions = null;
        }
    }

    /// <summary>
    /// Delimiter to use for tabular array rows and inline primitive arrays.
    /// Default is comma (,).
    /// </summary>
    /// <remarks>Default is <see cref="ToonDelimiter.COMMA"/></remarks>
    public ToonDelimiter Delimiter
    {
        get => delimiter;
        set
        {
            if (delimiter == value)
                return;

            delimiter = value;
            cachedResolvedOptions = null;
        }
    }

    /// <summary>
    /// Enable key folding to collapse single-key wrapper chains.
    /// When set to <see cref="ToonKeyFolding.Safe"/>, nested objects with single keys are
    /// collapsed into dotted paths
    /// (e.g., data.metadata.items instead of nested indentation).
    /// </summary>
    /// <remarks>Default is <see cref="ToonKeyFolding.Off"/></remarks>
    public ToonKeyFolding KeyFolding
    {
        get => keyFolding;
        set
        {
            if (keyFolding == value)
                return;

            keyFolding = value;
            cachedResolvedOptions = null;
        }
    }

    /// <summary>
    /// Maximum number of segments to fold when <see cref="ToonEncodeOptions.KeyFolding"/> is enabled.
    /// Controls how deep the folding can go in single-key chains.
    /// Values 0 or 1 have no practical effect (treated as effectively disabled).
    /// </summary>
    /// <remarks>Default is <see cref="int.MaxValue"/></remarks>
    public int? FlattenDepth
    {
        get => flattenDepth;
        set
        {
            if (flattenDepth == value)
                return;

            flattenDepth = value;
            cachedResolvedOptions = null;
        }
    }

    /// <summary>
    /// Controls how arrays of objects are encoded.
    /// </summary>
    /// <remarks>Default is <see cref="ToonObjectArrayLayout.Columnar"/></remarks>
    public ToonObjectArrayLayout ObjectArrayLayout
    {
        get => objectArrayLayout;
        set
        {
            if (objectArrayLayout == value)
                return;

            objectArrayLayout = value;
            cachedResolvedOptions = null;
        }
    }

    /// <summary>
    /// When <see langword="true"/>, columns where every row has a <see langword="null"/> or empty-string value
    /// are omitted from the tabular header and all value rows, reducing output size.
    /// </summary>
    /// <remarks>Default is <see langword="true"/></remarks>
    public bool IgnoreNullOrEmpty
    {
        get => ignoreNullOrEmpty;
        set
        {
            if (ignoreNullOrEmpty == value)
                return;

            ignoreNullOrEmpty = value;
            cachedResolvedOptions = null;
        }
    }

    /// <summary>
    /// When <see langword="true"/>, properties whose value is an empty array are omitted from
    /// the output entirely — they are excluded from tabular spill rows and regular object output.
    /// </summary>
    /// <remarks>Default is <see langword="true"/></remarks>
    public bool ExcludeEmptyArrays
    {
        get => excludeEmptyArrays;
        set
        {
            if (excludeEmptyArrays == value)
                return;

            excludeEmptyArrays = value;
            cachedResolvedOptions = null;
        }
    }

    internal ResolvedEncodeOptions GetResolvedOptions()
    {
        return cachedResolvedOptions ??= new ResolvedEncodeOptions
        {
            Indent = indent,
            Delimiter = Constants.ToDelimiterChar(delimiter),
            KeyFolding = keyFolding,
            FlattenDepth = flattenDepth ?? int.MaxValue,
            ObjectArrayLayout = objectArrayLayout,
            IgnoreNullOrEmpty = ignoreNullOrEmpty,
            ExcludeEmptyArrays = excludeEmptyArrays
        };
    }
}
