using PharmacyV2.Models.People;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PharmacyV2.Models.Payment
{
    // "Paid" sheet, top block — the saved payment voucher header (Paid No e.g. A00001).
    // The "Payment" sheet is the staging screen that lists a supplier's outstanding
    // purchases (Due/Pre-Paid/Paid) before "Save & print" writes them here as a
    // SupplierPayment + its SupplierPaymentDetail allocation rows.
    public class SupplierPayment
    {
        [Key]
        public int Id { get; set; }

        // "Paid No" e.g. A00001
        [Required, MaxLength(50)]
        public string PaidNo { get; set; } = string.Empty;

        // "Paid date"
        public DateTime PaidDate { get; set; } = DateTime.Now;

        [Required, MaxLength(30)]
        public string PaymentMethod { get; set; } = "Cash";

        public int SupplierId { get; set; }
        public Supplier Supplier { get; set; } = null!;

        // Sum of Details[].PaidAmount — kept denormalized for fast list/print views,
        // same pattern as PurchaseInvoice.Total.
        [Column(TypeName = "decimal(18,2)")]
        public decimal TotalAmount { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // ----- boolean -----
        // True if this voucher was voided/reversed after being saved (e.g. a
        // data-entry mistake or a bounced payment). False (default) for a
        // normal, standing payment. Mirrors LedgerAccount.IsReversed.
        public bool IsCancelled { get; set; } = false;

        // Scanned/photographed proof of payment (bank slip, cash receipt, mobile
        // banking screenshot, etc.) attached to the voucher. Follows the same
        // optional path/bytes/content-type pattern as Product's image fields —
        // all three are nullable and default to null, so existing rows and any
        // code that doesn't supply a receipt keep working without change.
        [MaxLength(500)]
        public string? ReceiptImagePath { get; set; }

        public byte[]? ReceiptImage { get; set; }

        [MaxLength(100)]
        public string? ReceiptImageContentType { get; set; }

        public ICollection<SupplierPaymentDetail> Details { get; set; } = new List<SupplierPaymentDetail>();
    }
}
