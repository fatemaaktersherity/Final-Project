using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace PharmacyV2.DTOs
{
    public class SaleReturnItemReadDto
    {
        public int Id { get; set; }
        public int MedicineId { get; set; }
        public string? MedicineName { get; set; }
        public int UnitId { get; set; }
        public string? UnitName { get; set; }
        public decimal Quantity { get; set; }
        public decimal BaseQuantity { get; set; }
        public decimal SalesPrice { get; set; }
        public decimal SubTotal { get; set; }
    }

    public class SaleReturnItemWriteDto
    {
        // The original sale line is the single source of truth for product,
        // unit, package conversion and recorded sale price.
        [Range(1, int.MaxValue)] public int SaleItemId { get; set; }

        [Range(0.01, double.MaxValue)]
        public decimal Quantity { get; set; }

        // Kept only so obsolete item-level controller actions fail safely;
        // these values are never accepted from JSON and are not part of the API.
        [JsonIgnore] public int MedicineId { get; set; }
        [JsonIgnore] public int UnitId { get; set; }
        [JsonIgnore] public decimal BaseQuantity { get; set; }
        [JsonIgnore] public decimal SalesPrice { get; set; }

    }

    public class SaleReturnReadDto
    {
        public int Id { get; set; }
        public string ReturnNo { get; set; } = string.Empty;
        public DateTime ReturnDate { get; set; }
        public int SaleId { get; set; }
        public decimal ReturnTotal { get; set; }
        public string? Reason { get; set; }
        public bool IsDeleted { get; set; }
        public string? ReceiptImagePath { get; set; }
        public byte[]? ReceiptImage { get; set; }
        public string? ReceiptImageContentType { get; set; }
        public List<SaleReturnItemReadDto> Items { get; set; } = new();
        public decimal RefundedAmount { get; set; }
        public decimal CustomerCredit { get; set; }
    }

    public class SaleReturnRefundDto
    {
        [Range(0.01, double.MaxValue)] public decimal Amount { get; set; }
        [Required, RegularExpression("^(Cash|Card|Mobile Banking)$")] public string PaymentMethod { get; set; } = "Cash";
        [MaxLength(500)] public string? Note { get; set; }
    }

    // Body for POST /api/salereturns — header + line items in one call,
    // same convention as PurchaseOrders. ReturnTotal is always computed
    // server-side as the sum of item SubTotals (Quantity * SalesPrice).
    public class SaleReturnCreateDto
    {
        [Required, MaxLength(50)]
        public string ReturnNo { get; set; } = string.Empty;

        public DateTime ReturnDate { get; set; } = DateTime.Now;

        [Required]
        public int SaleId { get; set; }

        [MaxLength(500)]
        public string? Reason { get; set; }

        [MaxLength(500)]
        public string? ReceiptImagePath { get; set; }

        public byte[]? ReceiptImage { get; set; }

        [MaxLength(100)]
        public string? ReceiptImageContentType { get; set; }

        public List<SaleReturnItemWriteDto>? Items { get; set; }
    }

    // Body for PUT /api/salereturns/{id} — header fields only; use the
    // /items endpoints to add/update/remove line items.
    public class SaleReturnUpdateDto
    {
        [MaxLength(500)]
        public string? Reason { get; set; }

        [MaxLength(500)]
        public string? ReceiptImagePath { get; set; }

        public byte[]? ReceiptImage { get; set; }

        [MaxLength(100)]
        public string? ReceiptImageContentType { get; set; }
    }
}
