using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace PharmacyV2.DTOs
{
    public class PurchaseReturnItemReadDto
    {
        public int Id { get; set; }
        public int ProductId { get; set; }
        public string? ProductName { get; set; }
        public int UnitId { get; set; }
        public string? UnitName { get; set; }
        public decimal Quantity { get; set; }
        public decimal BaseQuantity { get; set; }
        public decimal UnitCost { get; set; }
        public decimal SubTotal { get; set; }
    }

    public class PurchaseReturnItemWriteDto
    {
        // The original purchase line is the source of truth for product,
        // unit, batch and recorded cost.
        [Range(1, int.MaxValue)] public int PurchaseInvoiceItemId { get; set; }

        [Range(0.01, double.MaxValue)]
        public decimal Quantity { get; set; }

        // Kept only so obsolete item-level controller actions fail safely;
        // these values are never accepted from JSON and are not part of the API.
        [JsonIgnore] public int ProductId { get; set; }
        [JsonIgnore] public int UnitId { get; set; }
        [JsonIgnore] public decimal BaseQuantity { get; set; }
        [JsonIgnore] public decimal UnitCost { get; set; }

    }

    public class PurchaseReturnReceiveReadDto
    {
        public int Id { get; set; }
        public decimal ReceivedAmount { get; set; }
        public DateTime ReceivedDate { get; set; }
        public string PaymentMethod { get; set; } = "Cash";
        public string? Note { get; set; }
    }

    public class PurchaseReturnReceiveWriteDto
    {
        [Range(0.01, double.MaxValue)]
        public decimal ReceivedAmount { get; set; }

        public DateTime ReceivedDate { get; set; } = DateTime.Now;

        [Required, RegularExpression("^(Cash|Card|Mobile Banking)$")]
        public string PaymentMethod { get; set; } = "Cash";

        [MaxLength(500)]
        public string? Note { get; set; }
    }

    public class PurchaseReturnReceiveSyncDto : PurchaseReturnReceiveWriteDto
    {
        // null or 0 => insert a new receive row; otherwise update the matching one.
        public int? Id { get; set; }
    }

    public class PurchaseReturnReadDto
    {
        public int Id { get; set; }
        public string ReturnNo { get; set; } = string.Empty;
        public DateTime ReturnDate { get; set; }
        public int PurchaseInvoiceId { get; set; }
        public decimal ReturnTotal { get; set; }
        public string? Reason { get; set; }
        public bool IsCompleted { get; set; }

        public string? ReceiptImagePath { get; set; }
        public byte[]? ReceiptImage { get; set; }
        public string? ReceiptImageContentType { get; set; }

        public List<PurchaseReturnItemReadDto> Items { get; set; } = new();
        public List<PurchaseReturnReceiveReadDto> Receives { get; set; } = new();
    }

    // Body for POST /api/purchasereturns — header + line items in one call,
    // same convention as PurchaseOrders. ReturnTotal is always computed
    // server-side as the sum of item SubTotals (Quantity * UnitCost).
    public class PurchaseReturnCreateDto
    {
        [Required, MaxLength(50)]
        public string ReturnNo { get; set; } = string.Empty;

        public DateTime ReturnDate { get; set; } = DateTime.Now;

        [Required]
        public int PurchaseInvoiceId { get; set; }

        [MaxLength(500)]
        public string? Reason { get; set; }

        // Optional receipt image — omit/leave null and no image is stored,
        // same as every other field here that isn't [Required].
        [MaxLength(500)]
        public string? ReceiptImagePath { get; set; }

        public byte[]? ReceiptImage { get; set; }

        [MaxLength(100)]
        public string? ReceiptImageContentType { get; set; }

        public List<PurchaseReturnItemWriteDto>? Items { get; set; }

        // Optional — usually a return is created first and the refund comes
        // in later via POST /{id}/receives, but you can seed one or more
        // receives in the same call if you already have them.
        public List<PurchaseReturnReceiveWriteDto>? Receives { get; set; }
    }

    // Body for PUT /api/purchasereturns/{id} — header fields, plus an
    // optional full sync of Receives. Omit Receives entirely to leave them
    // untouched; send it (even as []) to fully replace the set — same
    // "full sync" convention as ProductUpdateDto.Prices: no Id/unmatched Id
    // => insert, matching Id => update, existing row missing from the
    // payload => delete. Items still go through the dedicated /items
    // endpoints.
    public class PurchaseReturnUpdateDto
    {
        [MaxLength(500)]
        public string? Reason { get; set; }

        public bool IsCompleted { get; set; }

        // Optional — omit/leave null to leave the stored receipt untouched.
        [MaxLength(500)]
        public string? ReceiptImagePath { get; set; }

        public byte[]? ReceiptImage { get; set; }

        [MaxLength(100)]
        public string? ReceiptImageContentType { get; set; }

        public List<PurchaseReturnReceiveSyncDto>? Receives { get; set; }
    }
}
