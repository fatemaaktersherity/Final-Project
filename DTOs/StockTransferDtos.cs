using System.ComponentModel.DataAnnotations;

namespace PharmacyV2.DTOs
{
    public class StockTransferItemReadDto
    {
        public int Id { get; set; }
        public int MedicineId { get; set; }
        public int SourceProductStockId { get; set; }
        public string? MedicineName { get; set; }
        public decimal Quantity { get; set; }
        public DateTime? ExpireDate { get; set; }
    }

    public class StockTransferItemWriteDto
    {
        [Required]
        public int MedicineId { get; set; }
        [Required] public int SourceProductStockId { get; set; }

        [Range(0.01, double.MaxValue)]
        public decimal Quantity { get; set; }

        public DateTime? ExpireDate { get; set; }
    }

    public class StockTransferReadDto
    {
        public int Id { get; set; }
        public string InvoiceId { get; set; } = string.Empty;
        public DateTime TransferDate { get; set; }
        public int FromWarehouseId { get; set; }
        public string? FromWarehouseName { get; set; }
        public int ToWarehouseId { get; set; }
        public string? ToWarehouseName { get; set; }
        public decimal TotalQty { get; set; }
        public int CreatedByUserId { get; set; }
        public bool IsReceived { get; set; }

        public string? ReceiptImagePath { get; set; }

        // No longer sends raw bytes in the list/detail JSON (that's what was
        // blowing up the response size — every byte becomes ~1.33 bytes of
        // base64 text, repeated on every GetAll call). Instead we just say
        // whether a receipt exists and where to fetch it from.
        public bool HasReceiptImage { get; set; }
        public string? ReceiptImageUrl { get; set; }
        public string? ReceiptImageContentType { get; set; }

        public List<StockTransferItemReadDto> Items { get; set; } = new();
    }

    // Body for POST /api/stocktransfers — header + items in one call.
    // FromWarehouseId and ToWarehouseId must differ and must both exist.
    // TotalQty is computed server-side from the items.
    public class StockTransferCreateDto
    {
        [Required, MaxLength(50)]
        public string InvoiceId { get; set; } = string.Empty;

        public DateTime TransferDate { get; set; } = DateTime.Now;

        [Required]
        public int FromWarehouseId { get; set; }

        [Required]
        public int ToWarehouseId { get; set; }

        [Required]
        public int CreatedByUserId { get; set; }

        // Optional receipt image — omit/leave null and no image is stored,
        // same as every other field here that isn't [Required].
        [MaxLength(500)]
        public string? ReceiptImagePath { get; set; }

        public byte[]? ReceiptImage { get; set; }

        [MaxLength(100)]
        public string? ReceiptImageContentType { get; set; }

        [MinLength(1)]
        public List<StockTransferItemWriteDto> Items { get; set; } = new();
    }

    // Body for PUT /api/stocktransfers/{id} — header notes/invoice id only
    // (items are append/remove only, via the parent controller's item endpoints
    // if you add them, following the PurchaseOrders pattern).
    public class StockTransferUpdateDto
    {
        [Required, MaxLength(50)]
        public string InvoiceId { get; set; } = string.Empty;

        public bool IsReceived { get; set; }

        // Optional — omit/leave null to leave the stored receipt untouched.
        [MaxLength(500)]
        public string? ReceiptImagePath { get; set; }

        public byte[]? ReceiptImage { get; set; }

        [MaxLength(100)]
        public string? ReceiptImageContentType { get; set; }
    }
}
