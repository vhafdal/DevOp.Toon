#nullable enable
using System;
using DevOp.Toon.Core;
using DevOp.Toon;
using DevOp.Toon.Internal.Encode;

namespace DevOp.Toon;

/// <summary>
/// Options that control how CLR values and TOON nodes are encoded as TOON text.
/// </summary>
public class ToonEncodeOptions
{
    private ToonProtocolVersion protocolVersion = ToonProtocolVersion.V4_4;
    private int indent = 1;
    private ToonDelimiter delimiter = Constants.DEFAULT_DELIMITER_ENUM;
    private ToonKeyFolding keyFolding = ToonKeyFolding.Off;
    private int? flattenDepth = int.MaxValue;
    private ToonByteArrayFormat byteArrayFormat = ToonByteArrayFormat.Base64String;
    private ToonObjectArrayLayout? explicitObjectArrayLayout;
    private bool? explicitIgnoreNullOrEmpty;
    private bool? explicitExcludeEmptyArrays;
    private bool? explicitConstantColumns;
    private ResolvedEncodeOptions? cachedResolvedOptions;

    /// <summary>
    /// Gets or sets the TOON protocol version to use for encoding.
    /// </summary>
    /// <remarks>The default value is <see cref="ToonProtocolVersion.V4_4"/>.</remarks>
    public ToonProtocolVersion ProtocolVersion
    {
        get => protocolVersion;
        set
        {
            if (protocolVersion == value)
                return;

            protocolVersion = value;
            cachedResolvedOptions = null;
        }
    }

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
    /// For official protocol versions, the default is <see cref="ToonObjectArrayLayout.Auto"/>.
    /// For custom protocol versions, the default is <see cref="ToonObjectArrayLayout.Columnar"/>.
    /// </remarks>
    public ToonObjectArrayLayout ObjectArrayLayout
    {
        get => explicitObjectArrayLayout ?? (IsCustom(protocolVersion) ? ToonObjectArrayLayout.Columnar : ToonObjectArrayLayout.Auto);
        set
        {
            if (explicitObjectArrayLayout == value)
                return;

            explicitObjectArrayLayout = value;
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
    /// For custom protocol versions, the default is <see langword="true"/>. For official protocol versions,
    /// the default is <see langword="false"/>.
    /// </remarks>
    public bool IgnoreNullOrEmpty
    {
        get => explicitIgnoreNullOrEmpty ?? IsCustom(protocolVersion);
        set
        {
            if (explicitIgnoreNullOrEmpty == value)
                return;

            explicitIgnoreNullOrEmpty = value;
            cachedResolvedOptions = null;
        }
    }

    /// <summary>
    /// Gets or sets whether empty array properties are omitted from the encoded output.
    /// </summary>
    /// <remarks>
    /// For custom protocol versions, the default is <see langword="true"/>. For official protocol versions,
    /// the default is <see langword="false"/>.
    /// </remarks>
    public bool ExcludeEmptyArrays
    {
        get => explicitExcludeEmptyArrays ?? IsCustom(protocolVersion);
        set
        {
            if (explicitExcludeEmptyArrays == value)
                return;

            explicitExcludeEmptyArrays = value;
            cachedResolvedOptions = null;
        }
    }

    /// <summary>
    /// Gets or sets whether a table column whose value is identical in every row is written once in the
    /// table header (<c>Name=value</c>) instead of in every row.
    /// </summary>
    /// <remarks>
    /// The default is <see langword="false"/>. Only <see cref="ToonProtocolVersion.CustomV4_4"/> writes constant
    /// columns; every other protocol version ignores this option and its output is unchanged. A column is written
    /// as a constant only when the table has at least three rows and the column's encoded value is the same in each
    /// of them; at least one column of every table stays a row cell. Decoders that do not support CustomV4_4
    /// constant columns (DevOp.Toon 0.4.0 and earlier) cannot read such output, so enable this option only when
    /// every reader is up to date.
    /// </remarks>
    public bool ConstantColumns
    {
        get => explicitConstantColumns ?? false;
        set
        {
            if (explicitConstantColumns == value)
                return;

            explicitConstantColumns = value;
            cachedResolvedOptions = null;
        }
    }

    /// <summary>
    /// Creates a shallow clone of the current options instance, preserving explicit override state.
    /// </summary>
    /// <returns>A new <see cref="ToonEncodeOptions"/> instance with identical settings and explicit overrides.</returns>
    public ToonEncodeOptions Clone()
    {
        return new ToonEncodeOptions
        {
            protocolVersion = protocolVersion,
            indent = indent,
            delimiter = delimiter,
            keyFolding = keyFolding,
            flattenDepth = flattenDepth,
            byteArrayFormat = byteArrayFormat,
            explicitObjectArrayLayout = explicitObjectArrayLayout,
            explicitIgnoreNullOrEmpty = explicitIgnoreNullOrEmpty,
            explicitExcludeEmptyArrays = explicitExcludeEmptyArrays,
            explicitConstantColumns = explicitConstantColumns
        };
    }

    /// <summary>Validates the selected version and option combination.</summary>
    public void Validate()
    {
        if (!Enum.IsDefined(typeof(ToonProtocolVersion), protocolVersion))
            throw new ArgumentOutOfRangeException(nameof(ProtocolVersion), protocolVersion, "Unknown or undefined protocol version.");

        if (protocolVersion == ToonProtocolVersion.Auto)
            throw new ArgumentOutOfRangeException(nameof(ProtocolVersion), protocolVersion, "ProtocolVersion.Auto is not valid for encoding.");

        if (indent <= 0)
            throw new ArgumentOutOfRangeException(nameof(Indent), indent, "Indent must be greater than 0.");

        if (!Enum.IsDefined(typeof(ToonDelimiter), delimiter))
            throw new ArgumentOutOfRangeException(nameof(Delimiter), delimiter, "Unknown or undefined delimiter.");

        if (!Enum.IsDefined(typeof(ToonKeyFolding), keyFolding))
            throw new ArgumentOutOfRangeException(nameof(KeyFolding), keyFolding, "Unknown or undefined key folding mode.");

        if (explicitObjectArrayLayout.HasValue && !Enum.IsDefined(typeof(ToonObjectArrayLayout), explicitObjectArrayLayout.Value))
            throw new ArgumentOutOfRangeException(nameof(ObjectArrayLayout), explicitObjectArrayLayout.Value, "Unknown or undefined object array layout.");

        if (!Enum.IsDefined(typeof(ToonByteArrayFormat), byteArrayFormat))
            throw new ArgumentOutOfRangeException(nameof(ByteArrayFormat), byteArrayFormat, "Unknown or undefined byte array format.");

        if (keyFolding == ToonKeyFolding.Safe && protocolVersion != ToonProtocolVersion.CustomLegacy && protocolVersion != ToonProtocolVersion.V3_0)
            throw new ArgumentException($"Key folding is not supported for protocol version {protocolVersion}.", nameof(KeyFolding));

        if (ObjectArrayLayout == ToonObjectArrayLayout.Columnar && !IsCustom(protocolVersion))
            throw new ArgumentException($"Columnar layout is only supported for custom protocol versions, not {protocolVersion}.", nameof(ObjectArrayLayout));
    }

    internal ResolvedEncodeOptions GetResolvedOptions()
    {
        if (cachedResolvedOptions != null)
            return cachedResolvedOptions;

        Validate();

        return cachedResolvedOptions = new ResolvedEncodeOptions
        {
            ProtocolVersion = protocolVersion,
            Indent = indent,
            Delimiter = Constants.ToDelimiterChar(delimiter),
            KeyFolding = keyFolding,
            FlattenDepth = flattenDepth ?? int.MaxValue,
            ObjectArrayLayout = ObjectArrayLayout,
            ByteArrayFormat = byteArrayFormat,
            IgnoreNullOrEmpty = IgnoreNullOrEmpty,
            ExcludeEmptyArrays = ExcludeEmptyArrays,
            ConstantColumns = ConstantColumns && protocolVersion == ToonProtocolVersion.CustomV4_4
        };
    }

    private static bool IsCustom(ToonProtocolVersion version) =>
        version is ToonProtocolVersion.CustomLegacy or ToonProtocolVersion.CustomV4_4;
}
