using PharmacyV2.Models.Branch;
using PharmacyV2.Models.Purchase;
using PharmacyV2.Models.SaleInvoice;
using System.ComponentModel.DataAnnotations;

namespace PharmacyV2.Models.Other
{
    public class AppUser
    {
        public int Id { get; set; }

        [Required, MaxLength(100)]
        public string Username { get; set; } = string.Empty;

        [Required, MaxLength(200)]
        public string FullName { get; set; } = string.Empty;

        [MaxLength(100)]
        public string? Email { get; set; }

        [MaxLength(20)]
        public string? Phone { get; set; }

        [Required]
        public string PasswordHash { get; set; } = string.Empty;

        public bool IsActive { get; set; } = true;

        public int? RoleId { get; set; }
        public Role? Role { get; set; }

        public ICollection<Sale> SalesCreated { get; set; } = new List<Sale>();
        public ICollection<PurchaseInvoice> PurchasesCreated { get; set; } = new List<PurchaseInvoice>();
        public ICollection<StockTransfer> TransfersCreated { get; set; } = new List<StockTransfer>();
    }
}
