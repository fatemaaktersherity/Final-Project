using System.ComponentModel.DataAnnotations;

namespace PharmacyV2.DTOs
{
    public class PaymentMethodReadDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? LedgerAccountCode { get; set; }
        public bool IsActive { get; set; }
    }

    public class PaymentMethodWriteDto
    {
        [Required, MaxLength(50)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(20)]
        public string? LedgerAccountCode { get; set; }

        public bool IsActive { get; set; } = true;
    }
}