using DevOp.Toon;

namespace DevOp.Toon.Tests;

public class HybridObjectArrayTests
{
    [Fact]
    public void Decode_ColumnarObjectArrayLiteral_ReturnsExpectedToonNodeGraph()
    {
        var toon = """
                   [2]{RecordId,ItemCode,Active}:
                     1,A,true
                       Details:
                         Category: Hardware
                       Warehouses[1]{Number,QuantityInStock}:
                         main,3
                     2,B,false
                       Details:
                         Category: Software
                       Warehouses[1]{Number,QuantityInStock}:
                         backup,7
                   """;

        var decoded = ToonDecoder.Decode(toon);

        Assert.NotNull(decoded);
        var rows = decoded.AsArray();
        Assert.Equal(2, rows.Count);
        Assert.Equal(1.0, rows[0]?["RecordId"]?.GetValue<double>());
        Assert.Equal("A", rows[0]?["ItemCode"]?.GetValue<string>());
        Assert.True(rows[0]?["Active"]?.GetValue<bool>());
        Assert.Equal("Hardware", rows[0]?["Details"]?["Category"]?.GetValue<string>());
        Assert.Equal("main", rows[0]?["Warehouses"]?[0]?["Number"]?.GetValue<string>());
        Assert.Equal(7.0, rows[1]?["Warehouses"]?[0]?["QuantityInStock"]?.GetValue<double>());
    }

    [Fact]
    public void Encode_WithColumnarObjectArrayLayout_UsesPrimitiveHeaderAndSpillsComplexFields()
    {
        var items = CreateRows();

        var encoded = ToonEncoder.Encode(items, new ToonEncodeOptions
        {
            ObjectArrayLayout = ToonObjectArrayLayout.Columnar
        });

        Assert.Contains("[2]{RecordId,ItemCode,Active}:", encoded);
        Assert.Contains("Details:", encoded);
        Assert.Contains("Warehouses[1]{Number,QuantityInStock}:", encoded);
    }

    [Fact]
    public void Decode_TypedColumnarObjectArray_RoundTripsSuccessfully()
    {
        var items = CreateRows();

        var encoded = ToonEncoder.Encode(items, new ToonEncodeOptions
        {
            ObjectArrayLayout = ToonObjectArrayLayout.Columnar
        });

        var decoded = ToonDecoder.Decode<List<ProductRow>>(encoded);

        Assert.NotNull(decoded);
        Assert.Equal(2, decoded!.Count);
        Assert.Equal("A", decoded[0].ItemCode);
        Assert.True(decoded[0].Active);
        Assert.Equal("Hardware", decoded[0].Details?.Category);
        Assert.Equal("main", decoded[0].Warehouses?[0].Number);
        Assert.Equal(7, decoded[1].Warehouses?[0].QuantityInStock);
    }

    [Fact]
    public void Decode_TypedColumnarObjectArrayLiteral_ReturnsExpectedTypedRows()
    {
        var toon = """
                   [2]{RecordId,ItemCode,Active}:
                     1,A,true
                       Details:
                         Category: Hardware
                       Warehouses[1]{Number,QuantityInStock}:
                         main,3
                     2,B,false
                       Details:
                         Category: Software
                       Warehouses[1]{Number,QuantityInStock}:
                         backup,7
                   """;

        var decoded = ToonDecoder.Decode<List<ProductRow>>(toon);

        Assert.NotNull(decoded);
        Assert.Equal(2, decoded!.Count);
        Assert.Equal(1, decoded[0].RecordId);
        Assert.Equal("A", decoded[0].ItemCode);
        Assert.True(decoded[0].Active);
        Assert.Equal("Hardware", decoded[0].Details?.Category);
        Assert.Equal("main", decoded[0].Warehouses?[0].Number);
        Assert.Equal(7, decoded[1].Warehouses?[0].QuantityInStock);
    }

    private static ProductRow[] CreateRows()
    {
        return
        [
            new ProductRow
            {
                RecordId = 1,
                ItemCode = "A",
                Active = true,
                Details = new ProductDetails { Category = "Hardware" },
                Warehouses =
                [
                    new WarehouseRow { Number = "main", QuantityInStock = 3 }
                ]
            },
            new ProductRow
            {
                RecordId = 2,
                ItemCode = "B",
                Active = false,
                Details = new ProductDetails { Category = "Software" },
                Warehouses =
                [
                    new WarehouseRow { Number = "backup", QuantityInStock = 7 }
                ]
            }
        ];
    }

    private sealed class ProductRow
    {
        public int RecordId { get; set; }
        public string? ItemCode { get; set; }
        public bool Active { get; set; }
        public ProductDetails? Details { get; set; }
        public List<WarehouseRow>? Warehouses { get; set; }
    }

    private sealed class ProductDetails
    {
        public string? Category { get; set; }
    }

    private sealed class WarehouseRow
    {
        public string? Number { get; set; }
        public int QuantityInStock { get; set; }
    }
}
