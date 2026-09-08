using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PharmacyV2.Models.Accounts
{
    public class ChartOfAccount
    {
        public int Id { get; set; }

        [Required, MaxLength(200)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(50)]
        public string? Code { get; set; }

        [Required, MaxLength(50)]
        public string AccountType { get; set; } = string.Empty;

        public int? ParentId { get; set; }
        public ChartOfAccount? Parent { get; set; }

        public bool IsActive { get; set; } = true;
        public bool IsSystem { get; set; } = false;

        [Column(TypeName = "decimal(18,2)")]
        public decimal? BudgetAmount { get; set; }

        public ICollection<ChartOfAccount> Children { get; set; } = new List<ChartOfAccount>();
        public ICollection<LedgerAccount> LedgerAccounts { get; set; } = new List<LedgerAccount>();
    }
}

