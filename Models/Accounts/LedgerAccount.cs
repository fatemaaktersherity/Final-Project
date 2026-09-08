using PharmacyV2.Enums;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PharmacyV2.Models.Accounts
{
    // The General Ledger. ChartOfAccount defines *what* accounts exist
    // (Cash, Sales Revenue, Accounts Payable, ...); LedgerAccount holds the
    // individual debit/credit postings *against* those accounts, e.g. one row
    // per Sale, PurchaseInvoice, SupplierPayment, manual journal entry, etc.
    // Every real-world transaction should post here as one or more balanced
    // double-entry rows (total debits == total credits per transaction).
    public class LedgerAccount
    {
        public int Id { get; set; }

        public int ChartOfAccountId { get; set; }
        public ChartOfAccount ChartOfAccount { get; set; } = null!;

        // Groups the rows that make up a single balanced double-entry transaction
        // (e.g. all lines of one Sale share the same VoucherNo).
        [Required, MaxLength(50)]
        public string VoucherNo { get; set; } = string.Empty;

        public DateTime TransactionDate { get; set; } = DateTime.Now;

        [Column(TypeName = "decimal(18,2)")]
        public decimal DebitAmount { get; set; } = 0;

        [Column(TypeName = "decimal(18,2)")]
        public decimal CreditAmount { get; set; } = 0;

        // Running balance of the owning ChartOfAccount immediately after this
        // posting — denormalized for fast ledger/statement views, same pattern
        // as PurchaseInvoice.Total / SupplierPayment.TotalAmount elsewhere.
        [Column(TypeName = "decimal(18,2)")]
        public decimal RunningBalance { get; set; } = 0;

        [MaxLength(500)]
        public string? Description { get; set; }

        // Where this posting came from, e.g. Sale, PurchaseInvoice, SupplierPayment.
        public LedgerSourceType SourceType { get; set; } = LedgerSourceType.Manual;

        // Id of the originating document (SaleId, PurchaseInvoiceId, ...).
        // Deliberately not a real FK: the source table varies with SourceType,
        // and several of those modules (Sale, PurchaseInvoice, ...) aren't wired
        // into PharmacyDbContext yet either.
        public int? SourceId { get; set; }

        public bool IsReversed { get; set; } = false;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public string? CreatedByUserId { get; set; }
    }
}
