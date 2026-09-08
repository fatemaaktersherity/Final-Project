using PharmacyV2.Enums;
using System.ComponentModel.DataAnnotations;

namespace PharmacyV2.DTOs
{
    // ===================== ChartOfAccount =====================
    public class ChartOfAccountListItemDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Code { get; set; }
        public string AccountType { get; set; } = string.Empty;
        public int? ParentId { get; set; }
        public string? ParentName { get; set; }
        public bool IsActive { get; set; }
        public bool IsSystem { get; set; }
        public decimal? BudgetAmount { get; set; }
        public int ChildCount { get; set; }
    }

    public class ChartOfAccountReadDto : ChartOfAccountListItemDto
    {
        public List<ChartOfAccountListItemDto> Children { get; set; } = new();
    }

    public class ChartOfAccountWriteDto
    {
        [Required, MaxLength(200)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(50)]
        public string? Code { get; set; }

        [Required, MaxLength(50)]
        public string AccountType { get; set; } = string.Empty;

        public int? ParentId { get; set; }

        public bool IsActive { get; set; } = true;

        [Range(0, double.MaxValue)]
        public decimal? BudgetAmount { get; set; }
    }

    // ===================== LedgerAccount (General Ledger) =====================
    public class LedgerLineReadDto
    {
        public int Id { get; set; }
        public int ChartOfAccountId { get; set; }
        public string ChartOfAccountName { get; set; } = string.Empty;
        public string VoucherNo { get; set; } = string.Empty;
        public DateTime TransactionDate { get; set; }
        public decimal DebitAmount { get; set; }
        public decimal CreditAmount { get; set; }
        public decimal RunningBalance { get; set; }
        public string? Description { get; set; }
        public LedgerSourceType SourceType { get; set; }
        public int? SourceId { get; set; }
        public bool IsReversed { get; set; }
        public DateTime CreatedAt { get; set; }
        public string? CreatedByUserId { get; set; }
    }

    // One line of a journal entry — exactly one of DebitAmount/CreditAmount
    // must be > 0 (matches the CK_LedgerAccount_DebitXorCredit constraint).
    public class JournalLineWriteDto
    {
        public int ChartOfAccountId { get; set; }

        [Range(0, double.MaxValue)]
        public decimal DebitAmount { get; set; }

        [Range(0, double.MaxValue)]
        public decimal CreditAmount { get; set; }

        [MaxLength(500)]
        public string? Description { get; set; }
    }

    // POST body for creating one balanced double-entry transaction (2+ lines
    // sharing a single VoucherNo, total debits == total credits).
    public class JournalEntryCreateDto
    {
        // Leave blank to auto-generate one.
        [MaxLength(50)]
        public string? VoucherNo { get; set; }

        public DateTime? TransactionDate { get; set; }

        [MaxLength(500)]
        public string? Description { get; set; }

        public LedgerSourceType SourceType { get; set; } = LedgerSourceType.Manual;

        public int? SourceId { get; set; }

        [MinLength(2)]
        public List<JournalLineWriteDto> Lines { get; set; } = new();
    }

    public class JournalEntryReadDto
    {
        public string VoucherNo { get; set; } = string.Empty;
        public DateTime TransactionDate { get; set; }
        public List<LedgerLineReadDto> Lines { get; set; } = new();
        public decimal TotalDebit { get; set; }
        public decimal TotalCredit { get; set; }
    }

    // ===================== CompanyAsset =====================
    public class CompanyAssetReadDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public decimal Value { get; set; }
        public DateTime AcquiredDate { get; set; }
        public decimal SalvageValue { get; set; }
        public int UsefulLifeYears { get; set; }
        public decimal DepreciationRatePercent { get; set; }
        public DepreciationMethod DepreciationMethod { get; set; }
        public decimal AccumulatedDepreciation { get; set; }
        public decimal BookValue { get; set; }
        public DateTime? NextDepreciationDate { get; set; }
        public DateTime? DisposalDate { get; set; }
        public string? Description { get; set; }
        public bool IsActive { get; set; }
    }

    public class CompanyAssetWriteDto
    {
        [Required, MaxLength(200)]
        public string Name { get; set; } = string.Empty;

        [Range(0, double.MaxValue)]
        public decimal Value { get; set; }

        public DateTime AcquiredDate { get; set; }

        [Range(0, double.MaxValue)]
        public decimal SalvageValue { get; set; } = 0;

        [Range(0, int.MaxValue)]
        public int UsefulLifeYears { get; set; } = 0;

        [Range(0, 100)]
        public decimal DepreciationRatePercent { get; set; } = 0;

        public DepreciationMethod DepreciationMethod { get; set; } = DepreciationMethod.StraightLine;

        public string? Description { get; set; }

        public bool IsActive { get; set; } = true;
    }

    // ===================== CompanyLiability =====================
    public class CompanyLiabilityReadDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public LiabilityType LiabilityType { get; set; }
        public decimal Amount { get; set; }
        public decimal PaidAmount { get; set; }
        public decimal OutstandingAmount { get; set; }
        public DateTime LiabilityDate { get; set; }
        public DateTime? DueDate { get; set; }
        public decimal InterestRate { get; set; }
        public string? Description { get; set; }
        public bool IsActive { get; set; }
    }

    public class CompanyLiabilityWriteDto
    {
        [Required, MaxLength(200)]
        public string Name { get; set; } = string.Empty;

        public LiabilityType LiabilityType { get; set; } = LiabilityType.Other;

        [Range(0, double.MaxValue)]
        public decimal Amount { get; set; }

        public DateTime LiabilityDate { get; set; }
        public DateTime? DueDate { get; set; }

        [Range(0, 100)]
        public decimal InterestRate { get; set; } = 0;

        public string? Description { get; set; }

        public bool IsActive { get; set; } = true;
    }

    public class LiabilityPaymentDto
    {
        [Range(0.01, double.MaxValue)]
        public decimal Amount { get; set; }
    }
}