using System;
using System.Collections.Generic;
using System.Text;
using DevOp.Toon;

namespace DevOp.Toon.Tests;

public class CompactHybridRegressionTests
{
    [Fact]
    public void Encode_CompactHybridProductLikeRows_RemainsSemanticallyEquivalentToFallbackShape()
    {
        var typedRows = CreateTypedRows();
        var fallbackRows = CreateFallbackRows();
        var options = CreateCompactHybridOptions();

        var typedToon = ToonEncoder.Encode(typedRows, options);
        var fallbackToon = ToonEncoder.Encode(fallbackRows, options);
        var decodeOptions = new ToonDecodeOptions
        {
            Indent = 1,
            ObjectArrayLayout = ToonObjectArrayLayout.Columnar
        };

        Assert.Contains("[2]{RecordId,ItemCode,Description,Inactive,RecordModified}:", typedToon);
        Assert.Contains("Tags[2]: Featured,Web", typedToon);
        Assert.Contains("PosProperties:", typedToon);
        Assert.Contains("Warehouses[2]{WarehouseCode,QuantityInStock,DefaultPOQuantity}:", typedToon);
        Assert.Contains("Variations[2]{Code,Description,Quantity}:", typedToon);

        var typedNode = ToonDecoder.Decode(typedToon, decodeOptions);
        var fallbackNode = ToonDecoder.Decode(fallbackToon, decodeOptions);

        Assert.True(ToonNode.DeepEquals(typedNode, fallbackNode));

        var decoded = ToonDecoder.Decode<List<ProductLikeRow>>(typedToon, decodeOptions);
        Assert.NotNull(decoded);
        Assert.Equal(2, decoded!.Count);
        Assert.Equal("A-100", decoded[0].ItemCode);
        Assert.Equal("MAIN", decoded[0].Warehouses![0].WarehouseCode);
        Assert.Equal("Blue", decoded[0].Warehouses![0].Variations![1].Description);
        Assert.Equal("B-200", decoded[1].ItemCode);
        Assert.False(decoded[1].PosProperties!.IsIncludedItem);
    }

    [Fact]
    public void EncodeToBytes_CompactHybridTypedRows_MatchesStringEncoderOutput()
    {
        var typedRows = CreateTypedRows();
        var options = CreateCompactHybridOptions();

        var encodedText = ToonEncoder.Encode(typedRows, options);
        var encodedBytes = ToonEncoder.EncodeToBytes(typedRows, options);
        var encodedBytesText = Encoding.UTF8.GetString(encodedBytes);

        Assert.Equal(encodedText, encodedBytesText);
    }

    [Fact]
    public void EncodeToBytes_CompactHybridFallbackRows_MatchesStringEncoderOutput()
    {
        var fallbackRows = CreateFallbackRows();
        var options = CreateCompactHybridOptions();

        var encodedText = ToonEncoder.Encode(fallbackRows, options);
        var encodedBytes = ToonEncoder.EncodeToBytes(fallbackRows, options);
        var encodedBytesText = Encoding.UTF8.GetString(encodedBytes);

        Assert.Equal(encodedText, encodedBytesText);
    }

    private static ToonEncodeOptions CreateCompactHybridOptions()
    {
        return new ToonEncodeOptions
        {
            Indent = 1,
            Delimiter = ToonDelimiter.COMMA,
            KeyFolding = ToonKeyFolding.Safe,
            ObjectArrayLayout = ToonObjectArrayLayout.Columnar
        };
    }

    private static List<ProductLikeRow> CreateTypedRows()
    {
        return new List<ProductLikeRow>
        {
            new ProductLikeRow
            {
                RecordId = 1,
                ItemCode = "A-100",
                Description = "Widget",
                Inactive = false,
                RecordModified = new DateTimeOffset(2026, 03, 20, 12, 34, 56, TimeSpan.Zero),
                Tags = new List<string> { "Featured", "Web" },
                PosProperties = new PosPropertiesRow
                {
                    IsIncludedItem = true,
                    HasIncludedItems = false
                },
                Warehouses = new List<WarehouseRow>
                {
                    new WarehouseRow
                    {
                        WarehouseCode = "MAIN",
                        QuantityInStock = 12.5,
                        DefaultPOQuantity = 4,
                        Variations = new List<VariationRow>
                        {
                            new VariationRow { Code = "RED", Description = "Red", Quantity = 7 },
                            new VariationRow { Code = "BLUE", Description = "Blue", Quantity = 5.5 }
                        }
                    },
                    new WarehouseRow
                    {
                        WarehouseCode = "BACKUP",
                        QuantityInStock = 3,
                        DefaultPOQuantity = 1,
                        Variations = new List<VariationRow>
                        {
                            new VariationRow { Code = "STD", Description = "Standard", Quantity = 3 }
                        }
                    }
                },
            },
            new ProductLikeRow
            {
                RecordId = 2,
                ItemCode = "B-200",
                Description = "Gadget",
                Inactive = true,
                RecordModified = new DateTimeOffset(2026, 03, 21, 8, 15, 00, TimeSpan.Zero),
                Tags = new List<string> { "Clearance" },
                PosProperties = new PosPropertiesRow
                {
                    IsIncludedItem = false,
                    HasIncludedItems = true
                },
                Warehouses = new List<WarehouseRow>
                {
                    new WarehouseRow
                    {
                        WarehouseCode = "MAIN",
                        QuantityInStock = 1,
                        DefaultPOQuantity = 2,
                        Variations = new List<VariationRow>
                        {
                            new VariationRow { Code = "STD", Description = "Standard", Quantity = 1 }
                        }
                    }
                },
            }
        };
    }

    private static List<Dictionary<string, object?>> CreateFallbackRows()
    {
        var firstWarehouse = new Dictionary<string, object?>
        {
            ["WarehouseCode"] = "MAIN",
            ["QuantityInStock"] = 12.5,
            ["DefaultPOQuantity"] = 4,
            ["Variations"] = new List<object?>
            {
                new Dictionary<string, object?>
                {
                    ["Code"] = "RED",
                    ["Description"] = "Red",
                    ["Quantity"] = 7
                },
                new Dictionary<string, object?>
                {
                    ["Code"] = "BLUE",
                    ["Description"] = "Blue",
                    ["Quantity"] = 5.5
                }
            }
        };

        var secondWarehouse = new Dictionary<string, object?>
        {
            ["WarehouseCode"] = "BACKUP",
            ["QuantityInStock"] = 3,
            ["DefaultPOQuantity"] = 1,
            ["Variations"] = new List<object?>
            {
                new Dictionary<string, object?>
                {
                    ["Code"] = "STD",
                    ["Description"] = "Standard",
                    ["Quantity"] = 3
                }
            }
        };

        var firstRow = new Dictionary<string, object?>
        {
            ["RecordId"] = 1,
            ["ItemCode"] = "A-100",
            ["Description"] = "Widget",
            ["Inactive"] = false,
            ["RecordModified"] = new DateTimeOffset(2026, 03, 20, 12, 34, 56, TimeSpan.Zero),
            ["Tags"] = new List<object?> { "Featured", "Web" },
            ["PosProperties"] = new Dictionary<string, object?>
            {
                ["IsIncludedItem"] = true,
                ["HasIncludedItems"] = false
            },
            ["Warehouses"] = new List<object?> { firstWarehouse, secondWarehouse }
        };

        var thirdWarehouse = new Dictionary<string, object?>
        {
            ["WarehouseCode"] = "MAIN",
            ["QuantityInStock"] = 1,
            ["DefaultPOQuantity"] = 2,
            ["Variations"] = new List<object?>
            {
                new Dictionary<string, object?>
                {
                    ["Code"] = "STD",
                    ["Description"] = "Standard",
                    ["Quantity"] = 1
                }
            }
        };

        var secondRow = new Dictionary<string, object?>
        {
            ["RecordId"] = 2,
            ["ItemCode"] = "B-200",
            ["Description"] = "Gadget",
            ["Inactive"] = true,
            ["RecordModified"] = new DateTimeOffset(2026, 03, 21, 8, 15, 00, TimeSpan.Zero),
            ["Tags"] = new List<object?> { "Clearance" },
            ["PosProperties"] = new Dictionary<string, object?>
            {
                ["IsIncludedItem"] = false,
                ["HasIncludedItems"] = true
            },
            ["Warehouses"] = new List<object?> { thirdWarehouse }
        };

        return new List<Dictionary<string, object?>> { firstRow, secondRow };
    }

    private sealed class ProductLikeRow
    {
        public int RecordId { get; set; }
        public string? ItemCode { get; set; }
        public string? Description { get; set; }
        public bool Inactive { get; set; }
        public DateTimeOffset RecordModified { get; set; }
        public List<string>? Tags { get; set; }
        public PosPropertiesRow? PosProperties { get; set; }
        public List<WarehouseRow>? Warehouses { get; set; }
    }

    private sealed class PosPropertiesRow
    {
        public bool IsIncludedItem { get; set; }
        public bool HasIncludedItems { get; set; }
    }

    private sealed class WarehouseRow
    {
        public string? WarehouseCode { get; set; }
        public double QuantityInStock { get; set; }
        public int DefaultPOQuantity { get; set; }
        public List<VariationRow>? Variations { get; set; }
    }

    private sealed class VariationRow
    {
        public string? Code { get; set; }
        public string? Description { get; set; }
        public double Quantity { get; set; }
    }
}
