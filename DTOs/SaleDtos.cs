using System.ComponentModel.DataAnnotations;

namespace PharmacyV2.DTOs
{
    public class SaleItemReadDto
    {
        public int SaleItemId { get; set; }
        public int ProductStockId { get; set; }
        public string? ProductName { get; set; }
        public string? ProductImagePath { get; set; }
        public int UnitId { get; set; }
        public string? UnitName { get; set; }
        public int Quantity { get; set; }
        public decimal BaseQuantity { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal TotalPrice { get; set; }
    }

    // Quantity/UnitPrice/UnitId for one batch being sold. TotalPrice is
    // DB-computed — never send it, the API ignores it. UnitId lets each line
    // item be sold in its own unit (e.g. one medicine as "Strip", another as
    // "Bottle") within the same sale.
    public class SaleItemWriteDto
    {
        [Required]
        public int ProductStockId { get; set; }

        [Required]
        public int UnitId { get; set; }

        [Range(1, int.MaxValue)]
        public int Quantity { get; set; }

        // Ignored by the server: UnitPrice is always resolved from
        // Product.SalePrice (see SalesController.GetCurrentSalePriceAsync),
        // so it stays in sync automatically whenever that column is edited
        // on the Product. Sending a value here has no effect — kept on the
        // DTO only so SaleItemReadDto/SaleItemSyncDto share the same shape.
        [Range(0, double.MaxValue)]
        public decimal UnitPrice { get; set; }
    }

    public class SaleReadDto
    {
        public int SaleId { get; set; }
        public string InvoiceNo { get; set; } = string.Empty;
        public int? CustomerId { get; set; }
        public string? CustomerName { get; set; }
        public DateTime SaleDate { get; set; }
        public decimal TotalAmount { get; set; }
        public string PaymentMethod { get; set; } = string.Empty;
        public string? CashierId { get; set; }
        public bool? IsPaid { get; set; }
        public bool IsVoided { get; set; }
        public string? VoidReason { get; set; }
        public DateTime? VoidedAt { get; set; }
        public decimal TotalPaid { get; set; }
        public decimal DueAmount { get; set; }

        // Total of all (non-deleted) SaleReturns posted against this sale —
        // powers the Sales list "Returned" status (medex-style: a sale with
        // returns shouldn't still read as plain "Paid").
        public decimal ReturnedAmount { get; set; }
        public bool HasReturns { get; set; }

        // Receipt image now lives on disk (wwwroot/images/sale-receipts) — see
        // POST/DELETE api/sales/{id}/receipt-image. Only the path + content
        // type travel in JSON now, no more base64 blob bloating every response.
        public string? ReceiptImagePath { get; set; }
        public string? ReceiptImageContentType { get; set; }

        public List<SaleItemReadDto> Items { get; set; } = new();
    }

    // Body for POST /api/sales — header + line items in one call. Each item's
    // ProductStockId batch must have enough AvailableQuantity; stock is
    // decremented server-side. Upload the receipt image afterwards via
    // POST /api/sales/{id}/receipt-image (multipart file) — it's no longer
    // accepted here as raw bytes.
    public class SaleCreateDto
    {
        public int? CustomerId { get; set; }

        public DateTime SaleDate { get; set; } = DateTime.UtcNow;

        // Ignored: TotalAmount is always computed server-side from
        // Items[].Quantity * Items[].UnitPrice so it can't drift from the
        // line items that actually moved stock.
        [Obsolete("Server-computed from Items; sending this has no effect.")]
        public decimal TotalAmount { get; set; }

        public string PaymentMethod { get; set; } = "Cash";

        public string? CashierId { get; set; }

        public bool? IsPaid { get; set; } = null;

        [MinLength(1)]
        public List<SaleItemWriteDto> Items { get; set; } = new();
    }

    // Body for PUT /api/sales/{id} — header fields only; line items are
    // synced separately (see comments in SalesController). Receipt image is
    // managed only via POST/DELETE api/sales/{id}/receipt-image.
    public class SaleUpdateDto
    {
        public int? CustomerId { get; set; }

        // Was missing before, so SaleDate could never be changed via PUT —
        // Update() had no line setting it on the tracked entity either.
        // Nullable: omit it (or send null) to leave the existing SaleDate
        // untouched, same convention as the other optional fields here.
        public DateTime? SaleDate { get; set; }

        public string PaymentMethod { get; set; } = "Cash";

        public string? CashierId { get; set; }

        public bool? IsPaid { get; set; } = null;

        // Full sync of the line items: existing SaleItemId -> update
        // (ProductStockId/Quantity/UnitPrice), no/0 SaleItemId -> insert,
        // any existing item left out of this list -> deleted. Stock is
        // reconciled either way (released on remove/change, re-reserved
        // on add/change) and TotalAmount is recomputed from the result.
        // Leave this null to update header fields only and leave items untouched.
        public List<SaleItemSyncDto>? Items { get; set; }
    }

    // Payment collection is a separate accounting event. It never edits a
    // posted sale or its stock lines.
    public class SalePaymentDto
    {
        [Required, RegularExpression("^(Cash|Card|Mobile Banking)$")]
        public string PaymentMethod { get; set; } = "Cash";
        [Range(0.01, double.MaxValue)] public decimal? Amount { get; set; }
        [MaxLength(500)] public string? Note { get; set; }
    }

    public class SaleVoidDto
    {
        [Required, MaxLength(500)]
        public string Reason { get; set; } = string.Empty;
    }

    public class SaleItemSyncDto : SaleItemWriteDto
    {
        // null or 0 => insert a new item; otherwise update the matching one.
        public int? SaleItemId { get; set; }
    }
}
