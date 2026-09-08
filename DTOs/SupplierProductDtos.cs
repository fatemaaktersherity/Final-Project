using System.ComponentModel.DataAnnotations;

namespace PharmacyV2.DTOs
{
    // One (Unit, prices) row nested under a SupplierProduct.
    public class SupplierProductPriceReadDto
    {
        public int Id { get; set; }
        public int UnitId { get; set; }
        public string UnitName { get; set; } = string.Empty;
        public decimal BaseQuantity { get; set; }
        public decimal PurchasePrice { get; set; }
        public decimal? SalePrice { get; set; }
        public decimal? UnitPrice { get; set; }
        public decimal? DistributorPrice { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public class SupplierProductPriceWriteDto
    {
        // null/0 => insert; matching Id => update (same "full sync" convention
        // used elsewhere in this API, e.g. ProductPriceSyncDto).
        public int? Id { get; set; }

        [Required]
        public int UnitId { get; set; }

        [Range(0.0001, double.MaxValue)]
        public decimal BaseQuantity { get; set; } = 1;

        [Range(0, double.MaxValue)]
        public decimal PurchasePrice { get; set; }

        [Range(0, double.MaxValue)]
        public decimal? SalePrice { get; set; }

        [Range(0, double.MaxValue)]
        public decimal? UnitPrice { get; set; }

        [Range(0, double.MaxValue)]
        public decimal? DistributorPrice { get; set; }
    }

    // GET /api/supplierproducts?supplierId= or ?productId=
    public class SupplierProductReadDto
    {
        public int Id { get; set; }
        public int SupplierId { get; set; }
        public string SupplierName { get; set; } = string.Empty;
        public int ProductId { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public string? ProductStrength { get; set; }
        public string? SupplierProductCode { get; set; }
        public bool IsPreferred { get; set; }
        public bool IsActive { get; set; }
        public DateTime? LastPurchaseDate { get; set; }
        public decimal? LastPurchaseUnitCost { get; set; }
        public string? LastPurchaseUnitName { get; set; }
        public string? Note { get; set; }
        public List<SupplierProductPriceReadDto> Prices { get; set; } = new();
    }

    public class SupplierProductWriteDto
    {
        [Required]
        public int SupplierId { get; set; }

        [Required]
        public int ProductId { get; set; }

        [MaxLength(50)]
        public string? SupplierProductCode { get; set; }

        public bool IsPreferred { get; set; } = false;
        public bool IsActive { get; set; } = true;

        [MaxLength(500)]
        public string? Note { get; set; }

        // Omit to leave prices untouched on update; provide to fully sync
        // (same convention as Product.Prices).
        public List<SupplierProductPriceWriteDto>? Prices { get; set; }
    }

    // Effective price lookup for a Purchase Invoice line item — the piece
    // that lets the frontend auto-fill UnitCost/SalePrice the moment
    // Product + Unit (+ Supplier) are picked, instead of falling back to the
    // Product's flat master price.
    public class EffectiveUnitPriceDto
    {
        public int ProductId { get; set; }
        public int UnitId { get; set; }
        public string UnitName { get; set; } = string.Empty;
        public decimal BaseQuantity { get; set; }
        public decimal PurchasePrice { get; set; }
        public decimal? SalePrice { get; set; }
        public decimal? UnitPrice { get; set; }
        public decimal? DistributorPrice { get; set; }

        // "Supplier" if a SupplierProductPrice row matched, "Product" if it
        // fell back to the product-level ProductPrice for this unit —
        // lets the UI show the user where the number came from.
        public string Source { get; set; } = "Product";
    }
}
