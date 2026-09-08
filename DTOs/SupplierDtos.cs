using System.ComponentModel.DataAnnotations;

namespace PharmacyV2.DTOs
{
    public class SupplierListItemDto
    {
        public int SupplierId { get; set; }
        public string SupplierName { get; set; } = string.Empty;
        public int? CompanyId { get; set; }
        public string? CompanyName { get; set; }
        public string? Phone { get; set; }
        public bool Distributor { get; set; }
        public int SupplierTypeId { get; set; }
        public string SupplierTypeName { get; set; } = string.Empty;
        public string? LogoPath { get; set; }
        // Per-supplier contacts, same shape as GetById returns.
        public List<SupplierContactReadDto> Contacts { get; set; } = new();
    }

    public class SupplierReadDto
    {
        public int SupplierId { get; set; }
        public string SupplierName { get; set; } = string.Empty;
        public int? CompanyId { get; set; }
        public string? CompanyName { get; set; }
        public string? ContactPerson { get; set; }
        public string? Phone { get; set; }
        public string? Email { get; set; }
        public string? Address { get; set; }
        public bool Distributor { get; set; }
        public DateTime CreatedAt { get; set; }
        public decimal OpeningBalance { get; set; }
        public byte[]? Logo { get; set; }
        public string? LogoContentType { get; set; }
        public string? LogoPath { get; set; }
        public int SupplierTypeId { get; set; }
        public string SupplierTypeName { get; set; } = string.Empty;
        public List<SupplierContactReadDto> Contacts { get; set; } = new();
    }

    public class SupplierContactReadDto
    {
        public int Id { get; set; }
        public string ContactName { get; set; } = string.Empty;
        public string? Designation { get; set; }
        public string? Phone { get; set; }
        public bool IsPrimary { get; set; }
    }

    public class SupplierCreateDto
    {
        [Required, MaxLength(100)]
        public string SupplierName { get; set; } = string.Empty;

        // Company-র নিজস্ব distributor হলে সেট করো; individual/third-party
        // supplier হলে null রাখো — Company dropdown-এ "Others/Individual"
        // সিলেক্ট করলে frontend থেকে null পাঠাবে, SupplierName তখন manually
        // type করা যাবে।
        public int? CompanyId { get; set; }

        [MaxLength(100)]
        public string? ContactPerson { get; set; }

        [MaxLength(20)]
        public string? Phone { get; set; }

        [MaxLength(100), EmailAddress]
        public string? Email { get; set; }

        [MaxLength(200)]
        public string? Address { get; set; }

        public bool Distributor { get; set; } = false;

        [Range(0, double.MaxValue)]
        public decimal OpeningBalance { get; set; } = 0;

        public byte[]? Logo { get; set; }

        [MaxLength(100)]
        public string? LogoContentType { get; set; }

        [Required]
        public int SupplierTypeId { get; set; }

        public DateTime? CreatedAt { get; set; }

        public List<SupplierContactWriteDto>? Contacts { get; set; }
    }

    public class SupplierUpdateDto
    {
        [Required, MaxLength(100)]
        public string SupplierName { get; set; } = string.Empty;

        public int? CompanyId { get; set; }

        [MaxLength(100)]
        public string? ContactPerson { get; set; }

        [MaxLength(20)]
        public string? Phone { get; set; }

        [MaxLength(100), EmailAddress]
        public string? Email { get; set; }

        [MaxLength(200)]
        public string? Address { get; set; }

        public bool Distributor { get; set; }

        [Range(0, double.MaxValue)]
        public decimal OpeningBalance { get; set; }

        public byte[]? Logo { get; set; }

        [MaxLength(100)]
        public string? LogoContentType { get; set; }

        [Required]
        public int SupplierTypeId { get; set; }

        public DateTime? CreatedAt { get; set; }

        public List<SupplierContactSyncDto>? Contacts { get; set; }
    }

    public class SupplierContactWriteDto
    {
        [Required, MaxLength(100)]
        public string ContactName { get; set; } = string.Empty;

        [MaxLength(100)]
        public string? Designation { get; set; }

        [MaxLength(20)]
        public string? Phone { get; set; }

        public bool IsPrimary { get; set; } = false;
    }

    public class SupplierContactSyncDto : SupplierContactWriteDto
    {
        // null or 0 => insert a new contact; otherwise update the matching one.
        public int? Id { get; set; }
    }
}