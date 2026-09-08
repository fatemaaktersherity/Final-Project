using PharmacyV2.Enums;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PharmacyV2.Models.Accounts
{
    public class CompanyAsset
    {
        public int Id { get; set; }

        [Required, MaxLength(200)]
        public string Name { get; set; } = string.Empty;

        [Column(TypeName = "decimal(18,2)")]
        public decimal Value { get; set; }

        public DateTime AcquiredDate { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal SalvageValue { get; set; } = 0;

        public int UsefulLifeYears { get; set; } = 0;

        [Column(TypeName = "decimal(5,2)")]
        public decimal DepreciationRatePercent { get; set; } = 0;

        public DepreciationMethod DepreciationMethod { get; set; } = DepreciationMethod.StraightLine;

        [Column(TypeName = "decimal(18,2)")]
        public decimal AccumulatedDepreciation { get; set; } = 0;

        [Column(TypeName = "decimal(18,2)")]
        public decimal BookValue { get; set; }

        public DateTime? NextDepreciationDate { get; set; }
        public DateTime? DisposalDate { get; set; }

        [MaxLength(500)]
        public string? Description { get; set; }

        public bool IsActive { get; set; } = true;
    }
}

