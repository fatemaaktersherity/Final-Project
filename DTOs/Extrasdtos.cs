using System.ComponentModel.DataAnnotations;

namespace PharmacyV2.DTOs
{
    public class ProductStockListItemDto
    {
        public int Id { get; set; }
        public int ProductId { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public string? ProductCode { get; set; }
        public string? ProductImagePath { get; set; }
        public string? GenericName { get; set; }
        public string Strength { get; set; } = string.Empty;
        public string BrandType { get; set; } = "Allopathic";
        public int? CompanyId { get; set; }
        public string? CompanyName { get; set; }
        public int? DosageFormId { get; set; }
        public string? DosageFormName { get; set; }
        public int? SupplierId { get; set; }
        public string? SupplierName { get; set; }
        public bool RequiresPrescription { get; set; } = false;
        public int UnitId { get; set; }
        public string UnitName { get; set; } = string.Empty;
        public int WarehouseId { get; set; }
        public string BatchNumber { get; set; } = string.Empty;
        public decimal AvailableQuantity { get; set; }
        public decimal Quantity { get; set; }
        public DateOnly ExpiryDate { get; set; }
        public DateOnly ReceivedDate { get; set; }
        public decimal UnitCost { get; set; }
        public decimal SalePrice { get; set; }
        public string WarehouseName { get; set; } = string.Empty;
        public List<ProductPriceReadDto> Packagings { get; set; } = new();

        // Total units of this Product ever sold (summed across all its
        // stock batches) — powers the "Popularity" sort on the Sale form's
        // Alternate Brands list. 0 for a brand-new product with no sales yet.
        public int PopularityScore { get; set; }
    }

    public class ProductStockWriteDto
    {
        [Range(1, int.MaxValue)]
        public int ProductId { get; set; }

        [Range(1, int.MaxValue)]
        public int WarehouseId { get; set; }

        [Required, MaxLength(100)]
        public string BatchNumber { get; set; } = string.Empty;

        [Range(0, double.MaxValue)]
        public decimal Quantity { get; set; }

        [Range(0, double.MaxValue)]
        public decimal? AvailableQuantity { get; set; }

        public DateOnly ExpiryDate { get; set; }
        public DateOnly? ReceivedDate { get; set; }

        [Range(0, double.MaxValue)]
        public decimal UnitCost { get; set; }

        public int? SupplierId { get; set; }
        public int? ProductRakId { get; set; }
    }

    // ===================== ExpiredProductStock =====================
    public class ExpiredProductStockReadDto
    {
        public int Id { get; set; }
        public int ProductId { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public int ProductStockId { get; set; }
        public string BatchNumber { get; set; } = string.Empty;
        public DateOnly ExpiryDate { get; set; }
        public int WarehouseId { get; set; }
        public string WarehouseName { get; set; } = string.Empty;
        public decimal Quantity { get; set; }
        public decimal UnitCost { get; set; }
        public decimal TotalCost { get; set; }
        public string DisposalMethod { get; set; } = string.Empty;
        public DateTime DisposalDate { get; set; }
        public int ApprovedByUserId { get; set; }
        public string ApprovedByName { get; set; } = string.Empty;
        public string? Note { get; set; }
    }

    public class ExpiredProductStockCreateDto
    {
        public int ProductStockId { get; set; }

        [Range(0.01, double.MaxValue)]
        public decimal Quantity { get; set; }

        [MaxLength(100)]
        public string DisposalMethod { get; set; } = "Returned";


        [MaxLength(500)]
        public string? Note { get; set; }
    }

    // ===================== DailyPurchaseRequirementTable =====================
    public class PurchaseRequirementReadDto
    {
        public Guid SerialNo { get; set; }
        public int ProductId { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public int UnitId { get; set; }
        public string UnitName { get; set; } = string.Empty;
        public decimal LastPurchaseRate { get; set; }
        public decimal StockQty { get; set; }
        public decimal RequiredQty { get; set; }
        public decimal OrdersQty { get; set; }
    }

    public class PurchaseRequirementWriteDto
    {
        public int ProductId { get; set; }
        public int UnitId { get; set; }

        [Range(0, double.MaxValue)]
        public decimal LastPurchaseRate { get; set; }

        [Range(0, double.MaxValue)]
        public decimal StockQty { get; set; }

        [Range(0, double.MaxValue)]
        public decimal RequiredQty { get; set; }

        [Range(0, double.MaxValue)]
        public decimal OrdersQty { get; set; }
    }

    // ===================== RolePermission =====================
    public class RolePermissionReadDto
    {
        public int Id { get; set; }
        public string Module { get; set; } = string.Empty;
        public bool CanView { get; set; }
        public bool CanCreate { get; set; }
        public bool CanEdit { get; set; }
        public bool CanDelete { get; set; }
    }

    public class RolePermissionSetDto
    {
        [Required, MaxLength(100)]
        public string Module { get; set; } = string.Empty;
        public bool CanView { get; set; }
        public bool CanCreate { get; set; }
        public bool CanEdit { get; set; }
        public bool CanDelete { get; set; }
    }

    // ===================== SmsLog =====================
    public class SmsLogReadDto
    {
        public int Id { get; set; }
        public string PhoneNumber { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public DateTime SentAt { get; set; }
        public bool IsSuccess { get; set; }
        public string? Response { get; set; }
    }

    public class SmsLogCreateDto
    {
        [Required, MaxLength(20)]
        public string PhoneNumber { get; set; } = string.Empty;

        [Required, MaxLength(500)]
        public string Message { get; set; } = string.Empty;

        public bool IsSuccess { get; set; }

        [MaxLength(500)]
        public string? Response { get; set; }
    }
}
