using PharmacyV2.Models.Purchase;
using System.ComponentModel.DataAnnotations.Schema;

namespace PharmacyV2.Models.Payment
{
    // "Paid Details Table" (also mirrors the row shape of the "Payment" sheet).
    // Many-to-one -> SupplierPayment (one voucher can cover several purchase invoices).
    // Many-to-one -> PurchaseInvoice (one invoice can, in turn, be paid off across
    // several vouchers if it's settled in installments), so this table is the
    // classic many-to-many join/allocation entity between the two.
    public class SupplierPaymentDetail
    {
        public int Id { get; set; }

        public int SupplierPaymentId { get; set; }
        public SupplierPayment SupplierPayment { get; set; } = null!;

        public int PurchaseInvoiceId { get; set; }
        public PurchaseInvoice PurchaseInvoice { get; set; } = null!;

        // "Amount No" — the running/sequence amount id used on the printed voucher line
        public int LineNo { get; set; }

        // Snapshot of the invoice's total at the moment of payment
        [Column(TypeName = "decimal(18,2)")]
        public decimal TotalAmount { get; set; }

        // Outstanding "Due" on the invoice just before this allocation was applied
        [Column(TypeName = "decimal(18,2)")]
        public decimal DueBeforePayment { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal? PrePaid { get; set; }

        // "Paid" — amount actually settled by this allocation row
        [Column(TypeName = "decimal(18,2)")]
        public decimal PaidAmount { get; set; }
    }
}

