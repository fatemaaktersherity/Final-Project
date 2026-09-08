using System.ComponentModel.DataAnnotations;

namespace PharmacyV2.DTOs
{
    public class SupplierPaymentDetailReadDto
    {
        public int Id { get; set; }
        public int PurchaseInvoiceId { get; set; }
        public string? InvoiceNo { get; set; }
        public int LineNo { get; set; }
        public decimal TotalAmount { get; set; }
        public decimal DueBeforePayment { get; set; }
        public decimal? PrePaid { get; set; }
        public decimal PaidAmount { get; set; }
    }

    // Body for one allocation row — "pay PaidAmount of this voucher toward
    // this specific PurchaseInvoice". LineNo/TotalAmount/DueBeforePayment
    // are always computed server-side (never trust the client for these),
    // so they're deliberately absent from this write DTO.
    public class SupplierPaymentDetailWriteDto
    {
        [Required]
        public int PurchaseInvoiceId { get; set; }

        [Range(0.01, double.MaxValue)]
        public decimal PaidAmount { get; set; }

        public decimal? PrePaid { get; set; }
    }

    public class SupplierPaymentReadDto
    {
        public int Id { get; set; }
        public string PaidNo { get; set; } = string.Empty;
        public DateTime PaidDate { get; set; }
        public string PaymentMethod { get; set; } = "Cash";
        public int SupplierId { get; set; }
        public string? SupplierName { get; set; }
        public decimal TotalAmount { get; set; }
        public DateTime CreatedAt { get; set; }
        public bool IsCancelled { get; set; }

        public string? ReceiptImagePath { get; set; }
        public byte[]? ReceiptImage { get; set; }
        public string? ReceiptImageContentType { get; set; }

        public List<SupplierPaymentDetailReadDto> Details { get; set; } = new();
    }

    // Body for POST /api/supplierpayments — header + allocation rows in one
    // call (same convention as Sales/PurchaseInvoices). Every PurchaseInvoiceId
    // in Details must already exist AND belong to this same SupplierId — you
    // can't pay off someone else's invoice. Each row's PaidAmount can't
    // exceed that invoice's current Due.
    public class SupplierPaymentCreateDto
    {
        [Required, MaxLength(50)]
        public string PaidNo { get; set; } = string.Empty;

        public DateTime PaidDate { get; set; } = DateTime.Now;

        [Required, MaxLength(30)]
        public string PaymentMethod { get; set; } = "Cash";

        [Required]
        public int SupplierId { get; set; }

        [MaxLength(500)]
        public string? ReceiptImagePath { get; set; }

        public byte[]? ReceiptImage { get; set; }

        [MaxLength(100)]
        public string? ReceiptImageContentType { get; set; }

        [MinLength(1)]
        public List<SupplierPaymentDetailWriteDto> Details { get; set; } = new();
    }

    // Body for PUT /api/supplierpayments/5 — header fields only (PaidNo/PaidDate,
    // receipt). Details are managed through the dedicated /details endpoints
    // below, same pattern as Sales items.
    // IsCancelled: false -> true voids the voucher (every allocation row is
    // excluded from Due calculations from then on, restoring Due on the
    // affected invoices). Once cancelled it cannot be un-cancelled through
    // this endpoint — create a fresh voucher instead, so there's always a
    // clean audit trail of what was voided and when.
    public class SupplierPaymentUpdateDto
    {
        [Required, MaxLength(50)]
        public string PaidNo { get; set; } = string.Empty;

        public DateTime PaidDate { get; set; }

        [Required, MaxLength(30)]
        public string PaymentMethod { get; set; } = "Cash";

        public bool IsCancelled { get; set; } = false;

        [MaxLength(500)]
        public string? ReceiptImagePath { get; set; }

        public byte[]? ReceiptImage { get; set; }

        [MaxLength(100)]
        public string? ReceiptImageContentType { get; set; }
    }
}
