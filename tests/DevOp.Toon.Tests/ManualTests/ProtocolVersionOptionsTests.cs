using System;
using DevOp.Toon.Core;
using DevOp.Toon.Internal.Decode;
using DevOp.Toon.Internal.Encode;
using Xunit;

namespace DevOp.Toon.Tests.ManualTests;

public sealed class ProtocolVersionOptionsTests
{
    [Fact]
    public void ToonProtocolVersion_EnumValues_MatchSpecification()
    {
        Assert.Equal(0, (int)ToonProtocolVersion.Auto);
        Assert.Equal(300, (int)ToonProtocolVersion.V3_0);
        Assert.Equal(400, (int)ToonProtocolVersion.V4_0);
        Assert.Equal(401, (int)ToonProtocolVersion.V4_1);
        Assert.Equal(402, (int)ToonProtocolVersion.V4_2);
        Assert.Equal(403, (int)ToonProtocolVersion.V4_3);
        Assert.Equal(404, (int)ToonProtocolVersion.V4_4);
        Assert.Equal(10000, (int)ToonProtocolVersion.CustomLegacy);
        Assert.Equal(10404, (int)ToonProtocolVersion.CustomV4_4);
    }

    [Fact]
    public void ToonEncodeOptions_DefaultValues_AreOfficialV4_4()
    {
        var options = new ToonEncodeOptions();

        Assert.Equal(ToonProtocolVersion.V4_4, options.ProtocolVersion);
        Assert.Equal(1, options.Indent);
        Assert.Equal(ToonDelimiter.COMMA, options.Delimiter);
        Assert.Equal(ToonKeyFolding.Off, options.KeyFolding);
        Assert.Equal(ToonByteArrayFormat.Base64String, options.ByteArrayFormat);
        Assert.Equal(ToonObjectArrayLayout.Auto, options.ObjectArrayLayout);
        Assert.False(options.IgnoreNullOrEmpty);
        Assert.False(options.ExcludeEmptyArrays);
    }

    [Theory]
    [InlineData(ToonProtocolVersion.V3_0, ToonObjectArrayLayout.Auto, false, false)]
    [InlineData(ToonProtocolVersion.V4_0, ToonObjectArrayLayout.Auto, false, false)]
    [InlineData(ToonProtocolVersion.V4_1, ToonObjectArrayLayout.Auto, false, false)]
    [InlineData(ToonProtocolVersion.V4_2, ToonObjectArrayLayout.Auto, false, false)]
    [InlineData(ToonProtocolVersion.V4_3, ToonObjectArrayLayout.Auto, false, false)]
    [InlineData(ToonProtocolVersion.V4_4, ToonObjectArrayLayout.Auto, false, false)]
    [InlineData(ToonProtocolVersion.CustomLegacy, ToonObjectArrayLayout.Columnar, true, true)]
    [InlineData(ToonProtocolVersion.CustomV4_4, ToonObjectArrayLayout.Columnar, true, true)]
    public void ToonEncodeOptions_DefaultsVaryByVersion_WhenNotExplicitlySet(
        ToonProtocolVersion version,
        ToonObjectArrayLayout expectedLayout,
        bool expectedIgnoreNullOrEmpty,
        bool expectedExcludeEmptyArrays)
    {
        var options = new ToonEncodeOptions { ProtocolVersion = version };

        Assert.Equal(expectedLayout, options.ObjectArrayLayout);
        Assert.Equal(expectedIgnoreNullOrEmpty, options.IgnoreNullOrEmpty);
        Assert.Equal(expectedExcludeEmptyArrays, options.ExcludeEmptyArrays);
    }

    [Fact]
    public void ToonEncodeOptions_ChangingVersionDynamicallyUpdatesUnsetProperties()
    {
        var options = new ToonEncodeOptions();
        Assert.Equal(ToonObjectArrayLayout.Auto, options.ObjectArrayLayout);
        Assert.False(options.IgnoreNullOrEmpty);
        Assert.False(options.ExcludeEmptyArrays);

        options.ProtocolVersion = ToonProtocolVersion.CustomV4_4;
        Assert.Equal(ToonObjectArrayLayout.Columnar, options.ObjectArrayLayout);
        Assert.True(options.IgnoreNullOrEmpty);
        Assert.True(options.ExcludeEmptyArrays);

        options.ProtocolVersion = ToonProtocolVersion.V4_0;
        Assert.Equal(ToonObjectArrayLayout.Auto, options.ObjectArrayLayout);
        Assert.False(options.IgnoreNullOrEmpty);
        Assert.False(options.ExcludeEmptyArrays);
    }

    [Fact]
    public void ToonEncodeOptions_ExplicitOverrides_PreservedAcrossVersionChanges()
    {
        var options = new ToonEncodeOptions
        {
            ObjectArrayLayout = ToonObjectArrayLayout.Auto,
            IgnoreNullOrEmpty = false,
            ExcludeEmptyArrays = false
        };

        options.ProtocolVersion = ToonProtocolVersion.CustomV4_4;

        Assert.Equal(ToonObjectArrayLayout.Auto, options.ObjectArrayLayout);
        Assert.False(options.IgnoreNullOrEmpty);
        Assert.False(options.ExcludeEmptyArrays);
    }

    [Fact]
    public void ToonEncodeOptions_Clone_PreservesExplicitOverrideTracking()
    {
        var unmodified = new ToonEncodeOptions();
        var cloneUnmodified = unmodified.Clone();

        cloneUnmodified.ProtocolVersion = ToonProtocolVersion.CustomV4_4;
        Assert.Equal(ToonObjectArrayLayout.Columnar, cloneUnmodified.ObjectArrayLayout);
        Assert.True(cloneUnmodified.IgnoreNullOrEmpty);
        Assert.True(cloneUnmodified.ExcludeEmptyArrays);

        var overridden = new ToonEncodeOptions
        {
            ProtocolVersion = ToonProtocolVersion.CustomLegacy,
            ObjectArrayLayout = ToonObjectArrayLayout.Auto,
            IgnoreNullOrEmpty = false,
            ExcludeEmptyArrays = false
        };
        var cloneOverridden = overridden.Clone();

        Assert.Equal(ToonObjectArrayLayout.Auto, cloneOverridden.ObjectArrayLayout);
        Assert.False(cloneOverridden.IgnoreNullOrEmpty);
        Assert.False(cloneOverridden.ExcludeEmptyArrays);

        cloneOverridden.ProtocolVersion = ToonProtocolVersion.CustomV4_4;
        Assert.Equal(ToonObjectArrayLayout.Auto, cloneOverridden.ObjectArrayLayout);
        Assert.False(cloneOverridden.IgnoreNullOrEmpty);
        Assert.False(cloneOverridden.ExcludeEmptyArrays);
    }

    [Fact]
    public void ToonEncodeOptions_Validate_RejectsUndefinedEnum()
    {
        var options = new ToonEncodeOptions { ProtocolVersion = (ToonProtocolVersion)999 };
        Assert.Throws<ArgumentOutOfRangeException>(() => ToonEncoder.Encode(42, options));
    }

    [Fact]
    public void ToonEncodeOptions_Validate_RejectsAutoForEncode()
    {
        var options = new ToonEncodeOptions { ProtocolVersion = ToonProtocolVersion.Auto };
        Assert.Throws<ArgumentOutOfRangeException>(() => ToonEncoder.Encode(42, options));
    }

    [Theory]
    [InlineData(ToonProtocolVersion.V4_0)]
    [InlineData(ToonProtocolVersion.V4_1)]
    [InlineData(ToonProtocolVersion.V4_2)]
    [InlineData(ToonProtocolVersion.V4_3)]
    [InlineData(ToonProtocolVersion.V4_4)]
    [InlineData(ToonProtocolVersion.CustomV4_4)]
    public void ToonEncodeOptions_Validate_RejectsKeyFoldingSafe_ExceptLegacyVersions(ToonProtocolVersion version)
    {
        var options = new ToonEncodeOptions
        {
            ProtocolVersion = version,
            KeyFolding = ToonKeyFolding.Safe
        };

        Assert.Throws<ArgumentException>(() => ToonEncoder.Encode(new { a = new { b = 1 } }, options));
    }

    [Fact]
    public void ToonEncodeOptions_Validate_AllowsKeyFoldingSafe_OnCustomLegacy()
    {
        var options = new ToonEncodeOptions
        {
            ProtocolVersion = ToonProtocolVersion.CustomLegacy,
            KeyFolding = ToonKeyFolding.Safe
        };

        var encoded = ToonEncoder.Encode(new { a = new { b = 1 } }, options);
        Assert.Contains("a.b: 1", encoded);
    }

    [Theory]
    [InlineData(ToonProtocolVersion.V3_0)]
    [InlineData(ToonProtocolVersion.V4_0)]
    [InlineData(ToonProtocolVersion.V4_1)]
    [InlineData(ToonProtocolVersion.V4_2)]
    [InlineData(ToonProtocolVersion.V4_3)]
    [InlineData(ToonProtocolVersion.V4_4)]
    public void ToonEncodeOptions_Validate_RejectsExplicitColumnar_OnOfficialVersions(ToonProtocolVersion officialVersion)
    {
        var options = new ToonEncodeOptions
        {
            ProtocolVersion = officialVersion,
            ObjectArrayLayout = ToonObjectArrayLayout.Columnar
        };

        Assert.Throws<ArgumentException>(() => ToonEncoder.Encode(new[] { new { id = 1 } }, options));
    }

    [Theory]
    [InlineData(ToonProtocolVersion.CustomLegacy)]
    [InlineData(ToonProtocolVersion.CustomV4_4)]
    public void ToonEncodeOptions_Validate_AllowsColumnar_OnCustomVersions(ToonProtocolVersion customVersion)
    {
        var options = new ToonEncodeOptions
        {
            ProtocolVersion = customVersion,
            ObjectArrayLayout = ToonObjectArrayLayout.Columnar
        };

        var encoded = ToonEncoder.Encode(new[] { new { id = 1 } }, options);
        Assert.Contains("[1]{id}:", encoded);
    }

    [Fact]
    public void ToonDecodeOptions_DefaultValues_And_Clone()
    {
        var options = new ToonDecodeOptions();
        Assert.Equal(ToonProtocolVersion.Auto, options.ProtocolVersion);
        Assert.Equal(2, options.Indent);
        Assert.True(options.Strict);
        Assert.Equal(ToonPathExpansion.Off, options.ExpandPaths);
        Assert.Equal(ToonObjectArrayLayout.Auto, options.ObjectArrayLayout);

        options.ProtocolVersion = ToonProtocolVersion.V4_4;
        options.Indent = 4;
        options.Strict = false;
        options.ExpandPaths = ToonPathExpansion.Safe;
        options.ObjectArrayLayout = ToonObjectArrayLayout.Columnar;

        var clone = options.Clone();
        Assert.Equal(ToonProtocolVersion.V4_4, clone.ProtocolVersion);
        Assert.Equal(4, clone.Indent);
        Assert.False(clone.Strict);
        Assert.Equal(ToonPathExpansion.Safe, clone.ExpandPaths);
        Assert.Equal(ToonObjectArrayLayout.Columnar, clone.ObjectArrayLayout);
    }

    [Fact]
    public void ToonServiceOptions_ProtocolVersion_ForwardsToEncode()
    {
        var serviceOptions = new ToonServiceOptions
        {
            ProtocolVersion = ToonProtocolVersion.CustomV4_4
        };

        Assert.Equal(ToonProtocolVersion.CustomV4_4, serviceOptions.ProtocolVersion);
        Assert.Equal(ToonProtocolVersion.CustomV4_4, serviceOptions.Encode.ProtocolVersion);

        var clonedEncode = serviceOptions.Encode.Clone();
        var clonedDecode = serviceOptions.Decode.Clone();

        Assert.Equal(ToonProtocolVersion.CustomV4_4, clonedEncode.ProtocolVersion);
        Assert.Equal(ToonObjectArrayLayout.Columnar, clonedEncode.ObjectArrayLayout);
        Assert.True(clonedEncode.IgnoreNullOrEmpty);
        Assert.True(clonedEncode.ExcludeEmptyArrays);

        Assert.Equal(ToonProtocolVersion.Auto, clonedDecode.ProtocolVersion);
    }

    [Fact]
    public void ToonEncodeOptions_Validate_RejectsNonPositiveIndent()
    {
        var options = new ToonEncodeOptions
        {
            ProtocolVersion = ToonProtocolVersion.CustomLegacy,
            Indent = 0
        };
        Assert.Throws<ArgumentOutOfRangeException>(() => ToonEncoder.Encode(42, options));

        options.Indent = -1;
        Assert.Throws<ArgumentOutOfRangeException>(() => ToonEncoder.Encode(42, options));
    }

    [Fact]
    public void ToonEncodeOptions_Validate_RejectsUndefinedEnums()
    {
        var options = new ToonEncodeOptions
        {
            ProtocolVersion = ToonProtocolVersion.CustomLegacy,
            Delimiter = (ToonDelimiter)999
        };
        Assert.Throws<ArgumentOutOfRangeException>(() => ToonEncoder.Encode(42, options));

        options = new ToonEncodeOptions
        {
            ProtocolVersion = ToonProtocolVersion.CustomLegacy,
            KeyFolding = (ToonKeyFolding)999
        };
        Assert.Throws<ArgumentOutOfRangeException>(() => ToonEncoder.Encode(42, options));

        options = new ToonEncodeOptions
        {
            ProtocolVersion = ToonProtocolVersion.CustomLegacy,
            ObjectArrayLayout = (ToonObjectArrayLayout)999
        };
        Assert.Throws<ArgumentOutOfRangeException>(() => ToonEncoder.Encode(42, options));

        options = new ToonEncodeOptions
        {
            ProtocolVersion = ToonProtocolVersion.CustomLegacy,
            ByteArrayFormat = (ToonByteArrayFormat)999
        };
        Assert.Throws<ArgumentOutOfRangeException>(() => ToonEncoder.Encode(42, options));
    }

    [Fact]
    public void ToonEncodeOptions_OfficialVersions_AllowExplicitAutoLayout()
    {
        var options = new ToonEncodeOptions
        {
            ProtocolVersion = ToonProtocolVersion.V4_4,
            ObjectArrayLayout = ToonObjectArrayLayout.Auto
        };

        var encoded = ToonEncoder.Encode(new[] { new { id = 1, name = "test" } }, options);
        Assert.NotNull(encoded);
    }
}
