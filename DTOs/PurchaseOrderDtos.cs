using PharmacyV2.Enums;
using System.ComponentModel.DataAnnotations;

namespace PharmacyV2.DTOs
{
    public class PurchaseOrderItemReadDto
    {
        public int Id { get; set; }
        public int ProductId { get; set; }
        public string? ProductName { get; set; }
        public int UnitId { get; set; }
        public string? UnitName { get; set; }
        public decimal LastPurchasePrice { get; set; }
        public decimal StockQty { get; set; }
        public decimal RequiredQty { get; set; }
        public decimal OrderQty { get; set; }
        public decimal? ReceivingQty { get; set; }
        public bool IsCancelled { get; set; }
    }

    public class PurchaseOrderItemWriteDto
    {
        [Required]
        public int ProductId { get; set; }

        [Required]
        public int UnitId { get; set; }

        [Range(0, double.MaxValue)]
        public decimal LastPurchasePrice { get; set; }

        [Range(0, double.MaxValue)]
        public decimal StockQty { get; set; }

        [Range(0, double.MaxValue)]
        public decimal RequiredQty { get; set; }

        [Range(0, double.MaxValue)]
        public decimal OrderQty { get; set; }

        [Range(0, double.MaxValue)]
        public decimal? ReceivingQty { get; set; }

        public bool IsCancelled { get; set; } = false;
    }

    public class PurchaseOrderReadDto
    {
        public int Id { get; set; }
        public string OrderNo { get; set; } = string.Empty;
        public DateTime OrderDate { get; set; }
        public DateTime? RequirementDate { get; set; }
        public int SupplierId { get; set; }
        public string? SupplierName { get; set; }
        public string? SupplierCategory { get; set; }
        public PurchaseOrderSource Source { get; set; }
        public PurchaseOrderStatus Status { get; set; }
        public bool IsUrgent { get; set; }
        public DateTime CreatedAt { get; set; }

        public string? ReceiptImagePath { get; set; }
        public byte[]? ReceiptImage { get; set; }
        public string? ReceiptImageContentType { get; set; }

        public List<PurchaseOrderItemReadDto> Items { get; set; } = new();
    }

    // Body for POST /api/purchaseorders — header + items in one call.
    public class PurchaseOrderCreateDto
    {
        [MaxLength(50)]
        public string? OrderNo { get; set; }

        public DateTime OrderDate { get; set; } = DateTime.Now;

        public DateTime? RequirementDate { get; set; }

        [Required]
        public int SupplierId { get; set; }

        [MaxLength(100)]
        public string? SupplierCategory { get; set; }

        public PurchaseOrderSource Source { get; set; } = PurchaseOrderSource.Manual;

        public PurchaseOrderStatus Status { get; set; } = PurchaseOrderStatus.Draft;

        public bool IsUrgent { get; set; } = false;

        // Optional receipt image — omit/leave null and no image is stored,
        // same as every other field here that isn't [Required].
        [MaxLength(500)]
        public string? ReceiptImagePath { get; set; }

        public byte[]? ReceiptImage { get; set; }

        [MaxLength(100)]
        public string? ReceiptImageContentType { get; set; }

        public List<PurchaseOrderItemWriteDto> Items { get; set; } = new();
    }

    // Body for PUT /api/purchaseorders/{id} — header fields only; use the
    // /items endpoints to add or remove line items.
    public class PurchaseOrderUpdateDto
    {
        [Required]
        public int SupplierId { get; set; }

        [MaxLength(100)]
        public string? SupplierCategory { get; set; }

        public PurchaseOrderSource Source { get; set; }

        public PurchaseOrderStatus Status { get; set; }

        public bool IsUrgent { get; set; }

        public DateTime? RequirementDate { get; set; }

        // Optional — omit/leave null to leave the stored receipt untouched.
        [MaxLength(500)]
        public string? ReceiptImagePath { get; set; }

        public byte[]? ReceiptImage { get; set; }

        [MaxLength(100)]
        public string? ReceiptImageContentType { get; set; }
    }
}
