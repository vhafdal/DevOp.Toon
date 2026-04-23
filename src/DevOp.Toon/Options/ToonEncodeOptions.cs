#nullable enable
using DevOp.Toon.Core;
using DevOp.Toon;
using DevOp.Toon.Internal.Encode;

namespace DevOp.Toon;

/// <summary>
/// Options that control how CLR values and TOON nodes are encoded as TOON text.
/// </summary>
public class ToonEncodeOptions
{
    private int indent = 1;
    private ToonDelimiter delimiter = Constants.DEFAULT_DELIMITER_ENUM;
    private ToonKeyFolding keyFolding = ToonKeyFolding.Off;
    private int? flattenDepth = int.MaxValue;
    private ToonObjectArrayLayout objectArrayLayout = ToonObjectArrayLayout.Columnar;
    private ToonByteArrayFormat byteArrayFormat = ToonByteArrayFormat.Base64String;
    private bool ignoreNullOrEmpty = true;
    private bool excludeEmptyArrays = true;
    private ResolvedEncodeOptions? cachedResolvedOptions;

    /// <summary>
    /// Gets or sets the number of spaces to write for each indentation level.
    /// </summary>
    /// <remarks>The default value is 1. Encoding throws when this value is less than or equal to zero.</remarks>
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
    /// Gets or sets the delimiter used for tabular array rows and inline primitive arrays.
    /// </summary>
    /// <remarks>The default value is <see cref="ToonDelimiter.COMMA"/>.</remarks>
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
    /// Gets or sets whether eligible single-key wrapper objects are collapsed into dotted keys.
    /// </summary>
    /// <remarks>
    /// The default value is <see cref="ToonKeyFolding.Off"/>. When set to <see cref="ToonKeyFolding.Safe"/>,
    /// chains such as <c>data.metadata.items</c> can be written as a folded path instead of nested indentation.
    /// </remarks>
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
    /// Gets or sets the maximum number of path segments to fold when <see cref="KeyFolding"/> is enabled.
    /// </summary>
    /// <remarks>
    /// The default value is <see cref="int.MaxValue"/>. A <see langword="null"/> value is treated as unlimited;
    /// values 0 or 1 have no practical folding effect.
    /// </remarks>
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
    /// Gets or sets how arrays of objects are encoded.
    /// </summary>
    /// <remarks>
    /// The default value is <see cref="ToonObjectArrayLayout.Columnar"/>, which writes shared scalar properties
    /// as columns and emits complex per-row fields below the row where needed.
    /// </remarks>
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
    /// Gets or sets how CLR <see cref="byte"/> arrays are encoded.
    /// </summary>
    /// <remarks>
    /// The default value is <see cref="ToonByteArrayFormat.Base64String"/>, which treats <see cref="byte"/> arrays as opaque binary payloads.
    /// Use <see cref="ToonByteArrayFormat.NumericArray"/> to preserve JSON-like array shape.
    /// </remarks>
    public ToonByteArrayFormat ByteArrayFormat
    {
        get => byteArrayFormat;
        set
        {
            if (byteArrayFormat == value)
                return;

            byteArrayFormat = value;
            cachedResolvedOptions = null;
        }
    }

    /// <summary>
    /// Gets or sets whether all-null and all-empty-string columns are omitted from columnar object arrays.
    /// </summary>
    /// <remarks>
    /// The default value is <see langword="true"/>. Mixed columns are preserved when at least one row contains
    /// a non-null, non-empty value.
    /// </remarks>
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
    /// Gets or sets whether empty array properties are omitted from the encoded output.
    /// </summary>
    /// <remarks>
    /// The default value is <see langword="true"/>. This applies to regular object properties and columnar
    /// spill rows, reducing payload size when empty collections do not need to be preserved.
    /// </remarks>
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
            ByteArrayFormat = byteArrayFormat,
            IgnoreNullOrEmpty = ignoreNullOrEmpty,
            ExcludeEmptyArrays = excludeEmptyArrays
        };
    }
}
