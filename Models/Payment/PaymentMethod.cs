using System.ComponentModel.DataAnnotations;

namespace PharmacyV2.Models.Payment
{
    public class PaymentMethod
    {
        public int Id { get; set; }

        [Required, MaxLength(50)]
        public string Name { get; set; } = string.Empty;   // "Cash", "Card", "Mobile Banking"

        [MaxLength(20)]
        public string? LedgerAccountCode { get; set; }      // e.g. "1000", "1010", "1020"

        public bool IsActive { get; set; } = true;
    }
}