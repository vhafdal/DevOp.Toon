using System.Text.Json.Serialization;

namespace DevOp.Toon.Benchmarks.Models;

// Root myDeserializedClass = JsonConvert.DeserializeObject<Root>(myJsonResponse);
    public class Alternative
    {
        public string ItemCode { get; set; }
    }

    public class Barcode
    {
        [JsonPropertyName("Barcode")]
        public string Value { get; set; }
        public double? Quantity { get; set; }
        public bool? IsExtraBarcode { get; set; }
        public DateTime? Modified { get; set; }
    }

    public class Attachment
    {
        public int? ID { get; set; }
        public string Name { get; set; }
        public int? Size { get; set; }
        public DateTime? Linked { get; set; }
        public string MD5Hash { get; set; }
        public bool? ShowOnWeb { get; set; }
    }

    public class Category
    {
        public string ID { get; set; }
        public List<SubCategory> SubCategories { get; set; }
        public string Description { get; set; }
        public bool? IsActive { get; set; }
    }

    public class CurrencyPrice
    {
        public string CurrencyCode { get; set; }
        public double? Price1 { get; set; }
        public double? Price2 { get; set; }
        public double? Price3 { get; set; }
    }

    public class Memo
    {
        public string PageName { get; set; }
        public string PlainText { get; set; }
        public DateTime? Modified { get; set; }
        public int? RecordID { get; set; }
    }

    public class Change
    {
        public DateTime? Modified { get; set; }
        public string By { get; set; }
        public List<ChangeField> Fields { get; set; }
    }

    public class ChangeField
    {
        public string Name { get; set; }
        public string Value { get; set; }
    }

    public class PosProperties
    {
        public bool? IsIncludedItem { get; set; }
        public bool? HasIncludedItems { get; set; }
    }

    public class Product
    {
        public int? RecordID { get; set; }
        public string ItemCode { get; set; }
        public string Description { get; set; }
        public string Description2 { get; set; }
        public bool? Inactive { get; set; }
        public DateTime? RecordCreated { get; set; }
        public DateTime? RecordModified { get; set; }
        public DateTime? ObjectDate { get; set; }
        public int? ItemClass { get; set; }
        public string UnitCode { get; set; }
        public double? UnitQuantity { get; set; }
        public double? NetWeight { get; set; }
        public double? UnitVolume { get; set; }
        public double? TotalQuantityInWarehouse { get; set; }
        public double? PurchasePrice { get; set; }
        public string CurrencyCode { get; set; }
        public double? Exchange { get; set; }
        public double? UnitPrice1 { get; set; }
        public double? Purchasefactor { get; set; }
        public double? CostPrice { get; set; }
        public double? ProfitRatio1 { get; set; }
        public double? UnitPrice1WithTax { get; set; }
        public double? UnitPrice2 { get; set; }
        public double? UnitPrice3WithTax { get; set; }
        public bool? ShowItemInWebShop { get; set; }
        public bool? AllowDiscount { get; set; }
        public double? Discount { get; set; }
        public double? UnitPrice2WithTax { get; set; }
        public double? UnitPrice3 { get; set; }
        public double? PropositionPrice { get; set; }
        public string ExtraDesc1 { get; set; }
        public string ExtraDesc2 { get; set; }
        public bool? IsVariation { get; set; }
        public double? TaxPercent { get; set; }
        public string SalesTaxCode { get; set; }
        public string SalesLedgerCode { get; set; }
        public string PurchaseTaxCode { get; set; }
        public string PurchaseLedgerCode { get; set; }
        public bool? AllowNegativeInventiry { get; set; }
        public double? MinimumStock { get; set; }
        public double? MaximumStock { get; set; }
        public double? DefaultPurchaseQuantity { get; set; }
        public bool? SkipInPurchaseOrderSuggestions { get; set; }
        public int? DeliveryTime { get; set; }
        public double? DiscountQuantity { get; set; }
        public double? MaxDiscountAllowed { get; set; }
        public double? DefaultSaleQuantity { get; set; }
        public int? CostMethod { get; set; }
        public PosProperties PosProperties { get; set; }
        public bool? HasAttachments { get; set; }
        public bool? HasBarcodes { get; set; }
        public bool? HasCurrencyPrices { get; set; }
        public bool? HasUnits { get; set; }
        public bool? HasAlternative { get; set; }
        public List<Barcode> Barcodes { get; set; }
        public List<Attachment> Attachments { get; set; }
        public List<Category> Categories { get; set; }
        public List<Warehouse> Warehouses { get; set; }
        public List<CurrencyPrice> CurrencyPrices { get; set; }
        public List<Unit> Units { get; set; }
        public List<Alternative> Alternative { get; set; }
        public List<Change> Changes { get; set; }
        public List<Memo> Memos { get; set; }
        public List<Vendor> Vendors { get; set; }
    }

    public class SubCategory
    {
        public string ID { get; set; }
        public string Description { get; set; }
    }

    public class Unit
    {
        public string UnitCode { get; set; }
        public double? UnitQuantity { get; set; }
        public double? DefaultSaleQuantity { get; set; }
        public double? UnitPriceRatio { get; set; }
        public double? NetWeight { get; set; }
        public double? UnitPrice { get; set; }
        public double? UnitPriceWithTax { get; set; }
        public double? QuantityOnHand { get; set; }
    }

    public class Vendor
    {
        
        public string ItemCode { get; set; }
        [JsonPropertyName("Vendor")]
        public string Number { get; set; }
        public string Description { get; set; }
        public bool? PrimarySupplier { get; set; }
        public double? LastPurchasedPrice { get; set; }
        public double? LastPurchasedQuantity { get; set; }
        public double? PurchasedDiscount { get; set; }
        public double? Price { get; set; }
    }

    public class Warehouse
    {
        [JsonPropertyName("Warehouse")]
        public string Number { get; set; }
        public double? QuantityInStock { get; set; }
        public double? MinimumStock { get; set; }
        public double? MaximumStock { get; set; }
        public string LocationInWarehouse { get; set; }
        public double? QuantityOnBackOrders { get; set; }
        public double? QuantityPoOrders { get; set; }
        public DateTime? Modified { get; set; }
        public List<Variation> Variations { get; set; }
        public double? DefaultPOQuantity { get; set; }
        public bool? SkipPOSuggestion { get; set; }
        public int? Id { get; set; }
    }

    public class Variation
    {
        public string Code { get; set; }
        public string Description { get; set; }
        public double? Quantity { get; set; }
        public double? QuantityOnBackOrders { get; set; }
    }

