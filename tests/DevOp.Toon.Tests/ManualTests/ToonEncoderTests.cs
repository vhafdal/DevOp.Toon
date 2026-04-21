using System.Text.Json;
using DevOp.Toon;

namespace DevOp.Toon.Tests;

/// <summary>
/// Tests for encoding data to TOON format.
/// </summary>
[Collection("DefaultOptions")]
public class ToonEncoderTests
{
    [Fact]
    public void NewEncodeOptions_UseCompactColumnarDefaults()
    {
        var options = new ToonEncodeOptions();

        Assert.Equal(1, options.Indent);
        Assert.Equal(ToonDelimiter.COMMA, options.Delimiter);
        Assert.Equal(ToonKeyFolding.Off, options.KeyFolding);
        Assert.Equal(ToonObjectArrayLayout.Columnar, options.ObjectArrayLayout);
        Assert.True(options.IgnoreNullOrEmpty);
        Assert.True(options.ExcludeEmptyArrays);
    }

    [Fact]
    public void Encode_DefaultCompactOutput_DecodesWithDefaultDecoder()
    {
        var data = new[] { new { id = 1, name = "Alice" } };

        var encoded = ToonEncoder.Encode(data);
        var decoded = ToonDecoder.Decode(encoded);

        Assert.StartsWith("[1]{id,name}:", encoded);
        Assert.Equal("Alice", decoded?[0]?["name"]?.GetValue<string>());
    }

    [Fact]
    public void Encode_SimpleObject_ReturnsValidToon()
    {
        // Arrange
        var data = new { name = "Alice", age = 30 };

        // Act
        var result = ToonEncoder.Encode(data);

        // Assert
        Assert.NotNull(result);
        Assert.Contains("name:", result);
        Assert.Contains("age:", result);
    }

    [Fact]
    public void Encode_PrimitiveTypes_ReturnsValidToon()
    {
        // String
        var stringResult = ToonEncoder.Encode("hello");
        Assert.Equal("hello", stringResult);

        // Number
        var numberResult = ToonEncoder.Encode(42);
        Assert.Equal("42", numberResult);

        // Boolean
        var boolResult = ToonEncoder.Encode(true);
        Assert.Equal("true", boolResult);

        // Null
        var nullResult = ToonEncoder.Encode(null);
        Assert.Equal("null", nullResult);
    }

    [Fact]
    public void Encode_Array_ReturnsValidToon()
    {
        // Arrange
        var data = new { numbers = new[] { 1, 2, 3, 4, 5 } };

        // Act
        var result = ToonEncoder.Encode(data);

        // Assert
        Assert.NotNull(result);
        Assert.Contains("numbers[", result);
    }

    [Fact]
    public void Encode_TabularArray_ReturnsValidToon()
    {
        // Arrange
        var employees = new[]
        {
            new { id = 1, name = "Alice", salary = 50000 },
            new { id = 2, name = "Bob", salary = 60000 },
            new { id = 3, name = "Charlie", salary = 55000 }
        };
        var data = new { employees };

        // Act
        var result = ToonEncoder.Encode(data);

        // Assert
        Assert.NotNull(result);
        Assert.Contains("employees[", result);
        Assert.Contains("id", result);
        Assert.Contains("name", result);
        Assert.Contains("salary", result);
    }

    [Fact]
    public void Encode_WithCustomIndent_UsesCorrectIndentation()
    {
        // Arrange
        var data = new { outer = new { inner = "value" } };
        var options = new ToonEncodeOptions { Indent = 4 };

        // Act
        var result = ToonEncoder.Encode(data, options);

        // Assert
        Assert.NotNull(result);
        Assert.Contains("outer:", result);
    }

    [Fact]
    public void Encode_WithCustomDelimiter_UsesCorrectDelimiter()
    {
        // Arrange
        var data = new { numbers = new[] { 1, 2, 3 } };
        var options = new ToonEncodeOptions { Delimiter = ToonDelimiter.TAB };

        // Act
        var result = ToonEncoder.Encode(data, options);

        // Assert
        Assert.NotNull(result);
        Assert.Contains("numbers[", result);
    }

    [Fact]
    public void Encode_WithNonPositiveIndent_ThrowsArgumentOutOfRangeException()
    {
        var options = new ToonEncodeOptions { Indent = 0 };

        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => ToonEncoder.Encode(new { name = "Alice" }, options));

        Assert.Equal("indent", ex.ParamName);
    }

    [Fact]
    public void Encode_WithoutExplicitOptions_UsesConfiguredDefaultOptions()
    {
        var originalDefaults = ToonEncoder.DefaultOptions;

        try
        {
            ToonEncoder.DefaultOptions = new ToonEncodeOptions
            {
                KeyFolding = ToonKeyFolding.Safe
            };

            var data = new
            {
                root = new
                {
                    child = new
                    {
                        value = 42
                    }
                }
            };

            var result = ToonEncoder.Encode(data);

            Assert.Contains("root.child.value: 42", result);
        }
        finally
        {
            ToonEncoder.DefaultOptions = originalDefaults;
        }
    }

    [Fact]
    public void Encode_NestedStructures_ReturnsValidToon()
    {
        // Arrange
        var data = new
        {
            user = new
            {
                name = "Alice",
                address = new
                {
                    city = "New York",
                    zip = "10001"
                }
            }
        };

        // Act
        var result = ToonEncoder.Encode(data);

        // Assert
        Assert.NotNull(result);
        Assert.Contains("user:", result);
        Assert.Contains("address:", result);
    }

    [Fact]
    public void Encode_ArrayOfPrimitives_Issue7()
    {
        var data = new[]
        {
            new
            {
                ZCHATJID = "18324448539@s.whatsapp.net",
                ZMATCHEDTEXT = "http://www.\\u0138roger.com/anniversary",
                ZINDEX = "0",
                Z_OPT = 2,
                Z_PK = 2,
                ZSENDERJID = "18324448539@s.whatsapp.net",
                ZOWNSTHUMBNAIL = "0",
                ZTYPE = "0",
                Z_ENT = "10",
                ZMESSAGE = "696",
                ZSECTIONID = "2017-11",
                ZDATE = "531829799",
                ZCONTENT1 = "http://www.xn--roger-t5a.com/anniversary"
            }
        };

        var expected = """
                       [1]{ZCHATJID,ZMATCHEDTEXT,ZINDEX,Z_OPT,Z_PK,ZSENDERJID,ZOWNSTHUMBNAIL,ZTYPE,Z_ENT,ZMESSAGE,ZSECTIONID,ZDATE,ZCONTENT1}:
                         18324448539@s.whatsapp.net,"http://www.\\u0138roger.com/anniversary","0",2,2,18324448539@s.whatsapp.net,"0","0","10","696",2017-11,"531829799","http://www.xn--roger-t5a.com/anniversary"
                       """;

        var result = ToonEncoder.Encode(data, new ToonEncodeOptions
        {
            Indent = 2,
            Delimiter = ToonDelimiter.COMMA,
            KeyFolding = ToonKeyFolding.Safe,
            ObjectArrayLayout = ToonObjectArrayLayout.Columnar,
            IgnoreNullOrEmpty = true,
            ExcludeEmptyArrays = true
        });

        Assert.Equal(expected, result);
    }

    [Fact]
    public void Json2Toon_SimpleObject_ReturnsExpectedToon()
    {
        var json = """{"name":"Alice","age":30,"active":true}""";

        var result = ToonEncoder.Json2Toon(json);

        Assert.Contains("name: Alice", result);
        Assert.Contains("age: 30", result);
        Assert.Contains("active: true", result);
    }

    [Fact]
    public void Json2Toon_WithEscapedContent_PreservesText()
    {
        var json = """{"message":"Line 1\nLine 2","quote":"\"hi\""}""";

        var result = ToonEncoder.Json2Toon(json);
        var decoded = ToonDecoder.Toon2Json(result);

        using var expectedDocument = JsonDocument.Parse(json);
        using var actualDocument = JsonDocument.Parse(decoded);

        Assert.Equal(expectedDocument.RootElement.GetProperty("message").GetString(), actualDocument.RootElement.GetProperty("message").GetString());
        Assert.Equal(expectedDocument.RootElement.GetProperty("quote").GetString(), actualDocument.RootElement.GetProperty("quote").GetString());
    }

    [Fact]
    public void Encode_JsonElementList_EncodesObjectFields()
    {
        var json = """[{"id":"abc","name":"session","version":{"major":5,"minor":2}},{"id":"def","name":"other","version":{"major":5,"minor":3}}]""";
        var objects = JsonSerializer.Deserialize<List<object>>(json);
        Assert.NotNull(objects);

        var result = ToonEncoder.Encode(objects);

        Assert.Contains("[2]{id,name}:", result);
        Assert.Contains("abc,session", result);
        Assert.Contains("def,other", result);
        Assert.Contains("version:", result);
        Assert.DoesNotContain("""
                              [2]:
                                -
                                -
                              """, result);
    }
}
