using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace PharmacyV2.DTOs
{
    public class DosageFormOptionDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    public class ProductFilterOptionsDto
    {
        public List<DosageFormOptionDto> DosageForms { get; set; } = new();
        public List<string> GenericNames { get; set; } = new();
    }

    public class ProductListItemDto
    {
        public int Id { get; set; }
        public string? ProductCode { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public string Strength { get; set; } = string.Empty;
        public string? GenericName { get; set; }
        public string BrandType { get; set; } = "Allopathic";
        public int? CompanyId { get; set; }
        public string? CompanyName { get; set; }
        public int? DosageFormId { get; set; }
        public string? DosageFormName { get; set; }
        public int? ProductVariantId { get; set; }
        public int? ProductGroupId { get; set; }
        public string? ProductGroupName { get; set; }
        public int UnitId { get; set; }
        public string UnitName { get; set; } = string.Empty;
        public decimal UnitPrice { get; set; }
        public decimal PurchasePrice { get; set; }
        public decimal? SalePrice { get; set; }
        public int StockQuantity { get; set; }

        // Just the URL path (not the raw bytes) so the list stays light —
        // combine with your API's base URL to load it, e.g.
        // https://localhost:7079 + ImagePath. Null if no image was uploaded.
        // [JsonIgnore(Never)] forces this to appear as "imagePath": null in
        // the response instead of being dropped by the app-wide
        // DefaultIgnoreCondition = WhenWritingNull setting in Program.cs.
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public string? ImagePath { get; set; }

        // Flattened from the 1:1 ProductDetails row (null if the product
        // has no details row yet).
        public string? Manufacturer { get; set; }
        public string? Schedule { get; set; }
        public string? DarNo { get; set; }
        public string? StorageConditions { get; set; }
        public decimal? TemperatureMin { get; set; }
        public decimal? TemperatureMax { get; set; }
        public string? SideEffects { get; set; }
        public string? PregnancyCategory { get; set; }
        public bool? RequiresPrescription { get; set; }
        public bool? IsControlledDrug { get; set; }
        // Per-unit prices (e.g. Pcs / Box), same shape as GetById returns.
        public List<ProductPriceReadDto> Prices { get; set; } = new();
    }

    // Full shape for GET /api/products/{id}
    public class ProductReadDto
    {
        public int Id { get; set; }
        public string? ProductCode { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public string Strength { get; set; } = string.Empty;
        public string? GenericName { get; set; }
        public string? Barcode { get; set; }
        public string BrandType { get; set; } = "Allopathic";

        public int? CompanyId { get; set; }
        public string? CompanyName { get; set; }
        public int? DosageFormId { get; set; }
        public string? DosageFormName { get; set; }
        public int? ProductVariantId { get; set; }
        public int? ProductGroupId { get; set; }
        public string? ProductGroupName { get; set; }

        public int UnitId { get; set; }
        public string UnitName { get; set; } = string.Empty;

        public decimal UnitPrice { get; set; }
        public decimal PurchasePrice { get; set; }
        public decimal? DistributorPrice { get; set; }
        public decimal? SalePrice { get; set; }

        // Same override as ProductListItemDto.ImagePath — always show these
        // three as null rather than being dropped from the JSON when empty.
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public string? ImagePath { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public byte[]? ProductImage { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public string? ProductImageContentType { get; set; }
        public DateTime RegisteredDate { get; set; }
        public bool? IsActive { get; set; }

        public int StockQuantity { get; set; }
        public int MinStockQty { get; set; }
        public int MaxStockQty { get; set; }
        public int PurchaseQty { get; set; }

        public ProductDetailsReadDto? Details { get; set; }
        public List<ProductPriceReadDto> Prices { get; set; } = new();
    }

    public class ProductDetailsReadDto
    {
        public string? Description { get; set; }
        public string? Manufacturer { get; set; }
        public string? Schedule { get; set; }
        public string? DarNo { get; set; }
        public string StorageConditions { get; set; } = string.Empty;
        public decimal? TemperatureMin { get; set; }
        public decimal? TemperatureMax { get; set; }
        public string? Composition { get; set; }
        public string? SideEffects { get; set; }
        public string? PregnancyCategory { get; set; }
        public bool RequiresPrescription { get; set; }
        public bool IsControlledDrug { get; set; }
    }

    public class ProductPriceReadDto
    {
        public int Id { get; set; }
        public int UnitId { get; set; }
        public string UnitName { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public decimal PerUnitPrice { get; set; }
        public decimal BaseQuantity { get; set; }

        // Independent per-unit purchase/sale/distributor prices. Null means
        // "not set for this unit yet" — callers should fall back to
        // PerUnitPrice (purchase) / the product's own SalePrice as before,
        // rather than silently defaulting to 0.
        public decimal? PurchasePrice { get; set; }
        public decimal? SalePrice { get; set; }
        public decimal? DistributorPrice { get; set; }
    }

    // Row for the alphabetical "List of Brand Names" page (P3).
    public class ProductBrandListItemDto
    {
        public int Id { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public string Strength { get; set; } = string.Empty;
        public string? GenericName { get; set; }
        public string BrandType { get; set; } = "Allopathic";
        public string? ImagePath { get; set; }
        public string UnitName { get; set; } = string.Empty;
        public string? CompanyName { get; set; }
        public string? DosageFormName { get; set; }
    }

    public class ProductPriceHistoryReadDto
    {
        public int Id { get; set; }
        public string PriceType { get; set; } = string.Empty;
        public string? UnitName { get; set; }
        public decimal? PreviousPrice { get; set; }
        public decimal NewPrice { get; set; }
        public decimal? PreviousBaseQuantity { get; set; }
        public decimal? NewBaseQuantity { get; set; }
        public DateTime ChangedAt { get; set; }
    }

    // ---------- Write DTOs ----------

    public class ProductCreateDto
    {
        // Ignored by the server: ProductCode is always generated from a
        // Guid by ICodeGeneratorService (see ProductsController.Create), so
        // it's unique and consistently formatted without relying on the
        // caller. Kept on the DTO only so old clients that still send it
        // don't fail model binding.
        [Obsolete("Server-generated; sending a value has no effect.")]
        [MaxLength(50)]
        public string? ProductCode { get; set; }

        [Required, MaxLength(200)]
        public string ProductName { get; set; } = string.Empty;

        public string Strength { get; set; } = string.Empty;

        [MaxLength(200)]
        public string? GenericName { get; set; }

        [MaxLength(50)]
        public string? Barcode { get; set; }

        // "Allopathic" or "Herbal". Defaults to "Allopathic" when omitted.
        [MaxLength(20)]
        public string? BrandType { get; set; }

        public int? CompanyId { get; set; }
        public int? DosageFormId { get; set; }
        public int? ProductVariantId { get; set; }

        [Required]
        public int UnitId { get; set; }

        [Range(0, double.MaxValue)]
        public decimal UnitPrice { get; set; }

        [Range(0, double.MaxValue)]
        public decimal PurchasePrice { get; set; }

        [Range(0, double.MaxValue)]
        public decimal? DistributorPrice { get; set; }

        [Range(0, double.MaxValue)]
        public decimal? SalePrice { get; set; }

        [MaxLength(500)]
        public string? ImagePath { get; set; }

        public byte[]? ProductImage { get; set; }

        [MaxLength(100)]
        public string? ProductImageContentType { get; set; }

        public DateTime? RegisteredDate { get; set; }

        public bool? IsActive { get; set; } = null;

        [Range(0, double.MaxValue)]
        public int MinStockQty { get; set; } = 10;

        [Range(0, double.MaxValue)]
        public int MaxStockQty { get; set; } = 10000;

        public ProductDetailsWriteDto? Details { get; set; }
        public List<ProductPriceWriteDto>? Prices { get; set; }
    }

    public class ProductUpdateDto
    {
        // Ignored by the server: ProductCode is immutable once generated at
        // creation time (see ProductsController.Update). Kept on the DTO
        // only so old clients that still send it don't fail model binding.
        [Obsolete("Server-generated and immutable; sending a value has no effect.")]
        [MaxLength(50)]
        public string? ProductCode { get; set; }

        [Required, MaxLength(200)]
        public string ProductName { get; set; } = string.Empty;

        public string Strength { get; set; } = string.Empty;

        [MaxLength(200)]
        public string? GenericName { get; set; }

        [MaxLength(50)]
        public string? Barcode { get; set; }

        // "Allopathic" or "Herbal". Defaults to "Allopathic" when omitted.
        [MaxLength(20)]
        public string? BrandType { get; set; }

        public int? CompanyId { get; set; }
        public int? DosageFormId { get; set; }
        public int? ProductVariantId { get; set; }

        [Required]
        public int UnitId { get; set; }

        [Range(0, double.MaxValue)]
        public decimal UnitPrice { get; set; }

        [Range(0, double.MaxValue)]
        public decimal PurchasePrice { get; set; }

        [Range(0, double.MaxValue)]
        public decimal? DistributorPrice { get; set; }

        [Range(0, double.MaxValue)]
        public decimal? SalePrice { get; set; }

        [MaxLength(500)]
        public string? ImagePath { get; set; }

        public byte[]? ProductImage { get; set; }

        [MaxLength(100)]
        public string? ProductImageContentType { get; set; }

        // Nullable on purpose — if the caller doesn't send RegisteredDate on
        // an update, we now leave the product's original date untouched
        // instead of overwriting it with DateTime.MinValue.
        public DateTime? RegisteredDate { get; set; }

        public bool? IsActive { get; set; } = null;

        [Range(0, double.MaxValue)]
        public int MinStockQty { get; set; }

        [Range(0, double.MaxValue)]
        public int MaxStockQty { get; set; }

        // Omit to leave the details row untouched; provide to upsert it.
        public ProductDetailsWriteDto? Details { get; set; }

        // Omit to leave prices untouched; provide a list to fully sync
        // (rows with a matching Id are updated, rows with no/unmatched Id
        // are inserted, and any existing price not present here is removed) —
        // same convention as CustomerUpdateDto.Addresses.
        public List<ProductPriceSyncDto>? Prices { get; set; }
    }

    public class ProductDetailsWriteDto
    {
        public string? Description { get; set; }
        public string? Manufacturer { get; set; }

        [MaxLength(50)]
        public string? Schedule { get; set; }

        [MaxLength(100)]
        public string? DarNo { get; set; }

        public string StorageConditions { get; set; } = string.Empty;
        public decimal? TemperatureMin { get; set; }
        public decimal? TemperatureMax { get; set; }

        [MaxLength(1000)]
        public string? Composition { get; set; }

        [MaxLength(1000)]
        public string? SideEffects { get; set; }

        [MaxLength(50)]
        public string? PregnancyCategory { get; set; }

        public bool RequiresPrescription { get; set; }
        public bool IsControlledDrug { get; set; }
    }

    public class ProductPriceWriteDto
    {
        [Required]
        public int UnitId { get; set; }

        [MaxLength(150)]
        public string? DisplayName { get; set; }

        [Range(0, double.MaxValue)]
        public decimal PerUnitPrice { get; set; }

        [Range(0.0001, double.MaxValue)]
        public decimal BaseQuantity { get; set; } = 1;

        [Range(0, double.MaxValue)]
        public decimal? PurchasePrice { get; set; }

        [Range(0, double.MaxValue)]
        public decimal? SalePrice { get; set; }

        [Range(0, double.MaxValue)]
        public decimal? DistributorPrice { get; set; }
    }

    public class ProductPriceSyncDto : ProductPriceWriteDto
    {
        // null or 0 => insert a new price row; otherwise update the matching one.
        public int? Id { get; set; }
    }
    // Simple, consistent error payload for 400/404/409 responses.
    public class ApiError
    {
        public string Message { get; set; }

        public ApiError(string message)
        {
            Message = message;
        }
    }
}
