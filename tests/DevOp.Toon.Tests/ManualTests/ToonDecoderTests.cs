using System.Text.Json;
using DevOp.Toon;

namespace DevOp.Toon.Tests;

/// <summary>
/// Tests for decoding TOON format strings.
/// </summary>
[Collection("DefaultOptions")]
public class ToonDecoderTests
{
    [Fact]
    public void Decode_SimpleObject_ReturnsValidJson()
    {
        // Arrange
        var toonString = "name: Alice\nage: 30";

        // Act
        var result = ToonDecoder.Decode(toonString);

        // Assert
        Assert.NotNull(result);
        var obj = result.AsObject();
        Assert.Equal("Alice", obj["name"]?.GetValue<string>());
        Assert.Equal(30.0, obj["age"]?.GetValue<double>());
    }

    [Fact]
    public void Decode_PrimitiveTypes_ReturnsCorrectValues()
    {
        // String
        var stringResult = ToonDecoder.Decode("hello");
        Assert.Equal("hello", stringResult?.GetValue<string>());

        // Number - JSON defaults to double
        var numberResult = ToonDecoder.Decode("42");
        Assert.Equal(42.0, numberResult?.GetValue<double>());

        // Boolean
        var boolResult = ToonDecoder.Decode("true");
        Assert.True(boolResult?.GetValue<bool>());

        // Null
        var nullResult = ToonDecoder.Decode("null");
        Assert.Null(nullResult);
    }

    [Fact]
    public void Decode_PrimitiveArray_ReturnsValidArray()
    {
        // Arrange
        var toonString = "numbers[5]: 1, 2, 3, 4, 5";

        // Act
        var result = ToonDecoder.Decode(toonString);

        // Assert
        Assert.NotNull(result);
        var obj = result.AsObject();
        var numbers = obj["numbers"]?.AsArray();
        Assert.NotNull(numbers);
        Assert.Equal(5, numbers.Count);
        Assert.Equal(1.0, numbers[0]?.GetValue<double>());
        Assert.Equal(5.0, numbers[4]?.GetValue<double>());
    }

    [Fact]
    public void Decode_TabularArray_ReturnsValidStructure()
    {
        // Arrange - using list array format instead
        var toonString = @"employees[3]:
  - id: 1
    name: Alice
    salary: 50000
  - id: 2
    name: Bob
    salary: 60000
  - id: 3
    name: Charlie
    salary: 55000";

        // Act
        var result = ToonDecoder.Decode(toonString);

        // Assert
        Assert.NotNull(result);
        var obj = result.AsObject();
        var employees = obj["employees"]?.AsArray();
        Assert.NotNull(employees);
        Assert.Equal(3, employees.Count);
        Assert.Equal(1.0, employees[0]?["id"]?.GetValue<double>());
        Assert.Equal("Alice", employees[0]?["name"]?.GetValue<string>());
    }

    [Fact]
    public void Decode_NestedObject_ReturnsValidStructure()
    {
        // Arrange
        var toonString = @"user:
  name: Alice
  address:
    city: New York
    zip: 10001";

        // Act
        var result = ToonDecoder.Decode(toonString);

        // Assert
        Assert.NotNull(result);
        var user = result["user"]?.AsObject();
        Assert.NotNull(user);
        Assert.Equal("Alice", user["name"]?.GetValue<string>());
        var address = user["address"]?.AsObject();
        Assert.NotNull(address);
        Assert.Equal("New York", address["city"]?.GetValue<string>());
    }

    [Fact]
    public void Decode_WithStrictOption_ValidatesArrayLength()
    {
        // Arrange - array declares 5 items but only provides 3
        var toonString = "numbers[5]: 1, 2, 3";
        var options = new ToonDecodeOptions { Strict = true };

        // Act & Assert
        Assert.Throws<ToonFormatException>(() => ToonDecoder.Decode(toonString, options));
    }

    [Fact]
    public void Decode_WithNonStrictOption_AllowsLengthMismatch()
    {
        // Arrange - array declares 5 items but only provides 3
        var toonString = "numbers[5]: 1, 2, 3";
        var options = new ToonDecodeOptions { Strict = false };

        // Act
        var result = ToonDecoder.Decode(toonString, options);

        // Assert
        Assert.NotNull(result);
        var obj = result.AsObject();
        var numbers = obj["numbers"]?.AsArray();
        Assert.NotNull(numbers);
        Assert.Equal(3, numbers.Count);
    }

    [Fact]
    public void Decode_WithNonPositiveIndent_ThrowsArgumentOutOfRangeException()
    {
        var options = new ToonDecodeOptions { Indent = 0 };

        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => ToonDecoder.Decode("name: Alice", options));

        Assert.Equal("indent", ex.ParamName);
    }

    [Fact]
    public void Decode_WithoutExplicitOptions_UsesConfiguredDefaultOptions()
    {
        var originalDefaults = ToonDecoder.DefaultOptions;

        try
        {
            ToonDecoder.DefaultOptions = new ToonDecodeOptions
            {
                ExpandPaths = ToonPathExpansion.Safe
            };

            var result = ToonDecoder.Decode("user.name: Alice");

            Assert.NotNull(result);
            Assert.Equal("Alice", result?["user"]?["name"]?.GetValue<string>());
        }
        finally
        {
            ToonDecoder.DefaultOptions = originalDefaults;
        }
    }

    [Fact]
    public void Toon2Json_SimpleObject_ReturnsExpectedJson()
    {
        var toon = "name: Alice\nage: 30\nactive: true";

        var result = ToonDecoder.Toon2Json(toon);

        using var document = JsonDocument.Parse(result);
        Assert.Equal("Alice", document.RootElement.GetProperty("name").GetString());
        Assert.Equal(30, document.RootElement.GetProperty("age").GetInt32());
        Assert.True(document.RootElement.GetProperty("active").GetBoolean());
    }

    [Fact]
    public void Toon2Json_WithPathExpansionOptions_RespectsExpandedShape()
    {
        var toon = "warehouse.code: MAIN";

        var result = ToonDecoder.Toon2Json(
            toon,
            new ToonDecodeOptions
            {
                ExpandPaths = ToonPathExpansion.Safe
            });

        using var document = JsonDocument.Parse(result);
        Assert.Equal("MAIN", document.RootElement.GetProperty("warehouse").GetProperty("code").GetString());
    }

    [Fact]
    public void DetectOptions_WithFourSpaceIndentation_ReturnsDetectedIndent()
    {
        var toon = """
                   company:
                       name: Acme
                       address:
                           city: Reykjavik
                   """;

        var options = ToonDecoder.DetectOptions(toon);

        Assert.Equal(4, options.Indent);
        Assert.True(options.Strict);
        Assert.Equal(ToonPathExpansion.Off, options.ExpandPaths);
    }

    [Fact]
    public void DetectOptions_WithFallback_PreservesNonDetectableSettings()
    {
        var toon = """
                   company:
                     name: Acme
                   """;

        var options = ToonDecoder.DetectOptions(
            toon,
            new ToonDecodeOptions
            {
                Strict = false,
                ExpandPaths = ToonPathExpansion.Safe
            });

        Assert.Equal(2, options.Indent);
        Assert.False(options.Strict);
        Assert.Equal(ToonPathExpansion.Safe, options.ExpandPaths);
    }

    [Fact]
    public void DetectOptions_WithColumnarObjectArray_ReturnsColumnarLayout()
    {
        var toon = """
                   [2]{RecordId,ItemCode,Active}:
                     1,A,true
                       Details:
                         Category: Hardware
                     2,B,false
                       Details:
                         Category: Software
                   """;

        var options = ToonDecoder.DetectOptions(toon);

        Assert.Equal(ToonObjectArrayLayout.Columnar, options.ObjectArrayLayout);
    }

    [Fact]
    public void DetectOptions_WithoutColumnarObjectArray_PreservesAutoLayout()
    {
        var toon = """
                   company:
                     name: Acme
                     items[2]:
                       - id: 1
                       - id: 2
                   """;

        var options = ToonDecoder.DetectOptions(toon);

        Assert.Equal(ToonObjectArrayLayout.Auto, options.ObjectArrayLayout);
    }

    [Fact]
    public void Decode_InvalidFormat_ThrowsToonFormatException()
    {
        // Arrange - array length mismatch with strict mode
        var invalidToon = "items[10]: 1, 2, 3";
        var options = new ToonDecodeOptions { Strict = true };

        // Act & Assert
        Assert.Throws<ToonFormatException>(() => ToonDecoder.Decode(invalidToon, options));
    }

    [Fact]
    public void Decode_LargeScientificNotation_DoesNotOverflow()
    {
        var result = ToonDecoder.Decode("""
                                        value: 1e100
                                        """);

        Assert.NotNull(result);
        Assert.Equal(1e100, result["value"]?.GetValue<double>());
    }

    [Fact]
    public void Decode_EmptyString_ReturnsEmptyObject()
    {
        // Arrange
        var emptyString = "";

        // Act
        var result = ToonDecoder.Decode(emptyString);

        // Assert - empty string returns empty array
        Assert.NotNull(result);
    }

    [Fact]
    public void Decode_TypedNestedList_ReturnsExpectedObjectGraph()
    {
        var products = new List<ProductRecord>
        {
            new()
            {
                ItemCode = "A-100",
                Description = "Widget",
                RecordModified = new DateTimeOffset(2026, 03, 20, 12, 34, 56, TimeSpan.Zero),
                PosProperties = new PosPropertiesRecord
                {
                    IsIncludedItem = true,
                    HasIncludedItems = false
                },
                Warehouses = new List<WarehouseRecord>
                {
                    new()
                    {
                        WarehouseCode = "MAIN",
                        QuantityInStock = 12.5,
                        DefaultPOQuantity = 4
                    },
                    new()
                    {
                        WarehouseCode = "BACKUP",
                        QuantityInStock = 3,
                        DefaultPOQuantity = 1
                    }
                }
            }
        };

        var toon = ToonEncoder.Encode(products);

        var decoded = ToonDecoder.Decode<List<ProductRecord>>(toon);

        Assert.NotNull(decoded);
        var product = Assert.Single(decoded!);
        Assert.Equal("A-100", product.ItemCode);
        Assert.Equal("Widget", product.Description);
        Assert.Equal(new DateTimeOffset(2026, 03, 20, 12, 34, 56, TimeSpan.Zero), product.RecordModified);
        Assert.NotNull(product.PosProperties);
        Assert.True(product.PosProperties!.IsIncludedItem);
        Assert.False(product.PosProperties.HasIncludedItems);
        Assert.NotNull(product.Warehouses);
        Assert.Equal(2, product.Warehouses!.Count);
        Assert.Equal("MAIN", product.Warehouses[0].WarehouseCode);
        Assert.Equal(12.5, product.Warehouses[0].QuantityInStock);
        Assert.Equal("BACKUP", product.Warehouses[1].WarehouseCode);
    }

    [Fact]
    public void EncodeAndDecode_TypedPropertyName_UsesToonPropertyName()
    {
        var warehouse = new WarehouseAliasRecord
        {
            WarehouseCode = "MAIN",
            QuantityInStock = 12.5
        };

        var toon = ToonEncoder.Encode(warehouse);
        var decoded = ToonDecoder.Decode<WarehouseAliasRecord>(toon);

        Assert.Contains("Warehouse: MAIN", toon);
        Assert.DoesNotContain("WarehouseCode:", toon);
        Assert.NotNull(decoded);
        Assert.Equal("MAIN", decoded!.WarehouseCode);
        Assert.Equal(12.5, decoded.QuantityInStock);
    }

    [Fact]
    public void Decode_WithPathExpansionSafe_UsesToonPropertyName()
    {
        var toon = "warehouse.code: MAIN";
        var options = new ToonDecodeOptions
        {
            ExpandPaths = ToonPathExpansion.Safe
        };

        var decoded = ToonDecoder.Decode<WarehouseContainerRecord>(toon, options);

        Assert.NotNull(decoded);
        Assert.NotNull(decoded!.WarehouseInfo);
        Assert.Equal("MAIN", decoded.WarehouseInfo!.WarehouseCode);
    }

    [Fact]
    public void Decode_TypedRootArray_WithPathExpansionSafe_ReturnsExpectedObjectGraph()
    {
        var products = new List<ProductRecord>
        {
            new()
            {
                ItemCode = "A-100",
                Description = "Widget",
                RecordModified = new DateTimeOffset(2026, 03, 20, 12, 34, 56, TimeSpan.Zero),
                PosProperties = new PosPropertiesRecord
                {
                    IsIncludedItem = true,
                    HasIncludedItems = false
                },
                Warehouses = new List<WarehouseRecord>
                {
                    new()
                    {
                        WarehouseCode = "MAIN",
                        QuantityInStock = 12.5,
                        DefaultPOQuantity = 4
                    }
                }
            }
        };

        var toon = ToonEncoder.Encode(products);
        var decodeOptions = ToonDecoder.DetectOptions(toon, new ToonDecodeOptions
        {
            ExpandPaths = ToonPathExpansion.Safe
        });
        var decoded = ToonDecoder.Decode<List<ProductRecord>>(toon, decodeOptions);

        Assert.NotNull(decoded);
        var product = Assert.Single(decoded!);
        Assert.Equal("A-100", product.ItemCode);
        Assert.Equal("Widget", product.Description);
        Assert.NotNull(product.PosProperties);
        Assert.True(product.PosProperties!.IsIncludedItem);
        Assert.NotNull(product.Warehouses);
        var warehouse = Assert.Single(product.Warehouses!);
        Assert.Equal("MAIN", warehouse.WarehouseCode);
        Assert.Equal(12.5, warehouse.QuantityInStock);
    }

    [Fact]
    public void Decode_RootArray_WithPathExpansionSafe_ReturnsArrayWithoutExpansion()
    {
        var toon = """
                   [2]{Id,Name}:
                     1,Alice
                     2,Bob
                   """;

        var result = ToonDecoder.Decode(toon, new ToonDecodeOptions
        {
            ExpandPaths = ToonPathExpansion.Safe
        });

        Assert.NotNull(result);
        var array = result!.AsArray();
        Assert.Equal(2, array.Count);
        Assert.Equal(1.0, array[0]?["Id"]?.GetValue<double>());
        Assert.Equal("Bob", array[1]?["Name"]?.GetValue<string>());
    }

    [Fact]
    public void Decode_TypedEnumAndSmallIntegers_UsesNativeMaterializer()
    {
        var toon = @"Level: High
ByteValue: 7
ShortValue: 12
UnsignedValue: 99";

        var decoded = ToonDecoder.Decode<EnumCarrierRecord>(toon);

        Assert.NotNull(decoded);
        Assert.Equal(TestLevel.High, decoded!.Level);
        Assert.Equal((byte)7, decoded.ByteValue);
        Assert.Equal((short)12, decoded.ShortValue);
        Assert.Equal((uint)99, decoded.UnsignedValue);
    }

    [Fact]
    public void Decode_TypedUnsupportedTarget_ThrowsNotSupportedException()
    {
        var ex = Assert.Throws<NotSupportedException>(() => ToonDecoder.Decode<Tuple<int, int>>("Item1: 1\nItem2: 2"));

        Assert.Contains("System.Tuple", ex.Message);
    }

    private sealed class ProductRecord
    {
        public string? ItemCode { get; set; }
        public string? Description { get; set; }
        public DateTimeOffset? RecordModified { get; set; }
        public PosPropertiesRecord? PosProperties { get; set; }
        public List<WarehouseRecord>? Warehouses { get; set; }
    }

    private sealed class PosPropertiesRecord
    {
        public bool IsIncludedItem { get; set; }
        public bool HasIncludedItems { get; set; }
    }

    private sealed class WarehouseRecord
    {
        [ToonPropertyName("Warehouse")]
        public string? WarehouseCode { get; set; }

        public double QuantityInStock { get; set; }
        public int DefaultPOQuantity { get; set; }
    }

    private sealed class WarehouseAliasRecord
    {
        [ToonPropertyName("Warehouse")]
        public string? WarehouseCode { get; set; }

        public double QuantityInStock { get; set; }
    }

    private sealed class WarehouseContainerRecord
    {
        [ToonPropertyName("warehouse")]
        public WarehouseCodeRecord? WarehouseInfo { get; set; }
    }

    private sealed class WarehouseCodeRecord
    {
        [ToonPropertyName("code")]
        public string? WarehouseCode { get; set; }
    }

    private sealed class EnumCarrierRecord
    {
        public TestLevel Level { get; set; }
        public byte ByteValue { get; set; }
        public short ShortValue { get; set; }
        public uint UnsignedValue { get; set; }
    }

    private enum TestLevel
    {
        Low,
        High
    }
}
