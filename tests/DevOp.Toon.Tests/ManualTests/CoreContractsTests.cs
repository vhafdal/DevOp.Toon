using DevOp.Toon.Core;
using DevOp.Toon;

namespace DevOp.Toon.Tests.ManualTests;

public sealed class CoreContractsTests
{
    [Fact]
    public void EncodeAndDecode_Uses_ToonPropertyNameAttribute_FromCorePackage()
    {
        var value = new CoreAnnotatedWarehouse
        {
            WarehouseCode = "MAIN"
        };

        var toon = ToonEncoder.Encode(value);
        var decoded = ToonDecoder.Decode<CoreAnnotatedWarehouse>(toon);

        Assert.Equal("DevOp.Toon.Core", typeof(ToonPropertyNameAttribute).Namespace);
        Assert.Contains("Warehouse: MAIN", toon);
        Assert.DoesNotContain("WarehouseCode:", toon);
        Assert.NotNull(decoded);
        Assert.Equal("MAIN", decoded!.WarehouseCode);
    }

    private sealed class CoreAnnotatedWarehouse
    {
        [ToonPropertyName("Warehouse")]
        public string? WarehouseCode { get; set; }
    }
}
