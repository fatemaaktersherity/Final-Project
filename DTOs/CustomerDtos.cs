using System.ComponentModel.DataAnnotations;

namespace PharmacyV2.DTOs
{
    public class CustomerListItemDto
    {
        public int CustomerId { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? Phone { get; set; }
        public bool IsActive { get; set; }
        public bool HasPhoto { get; set; }

        // Just the short URL path (not raw bytes) — matches EmployeeListItemDto.PhotoPath.
        public string? PhotoPath { get; set; }
        public int CustomerTypeId { get; set; }
        public string CustomerTypeName { get; set; } = string.Empty;
        // Per-customer addresses, same shape as GetById returns.
        public List<CustomerAddressReadDto> Addresses { get; set; } = new();
    }

    public class CustomerReadDto
    {
        public int CustomerId { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? Phone { get; set; }
        public decimal CreditLimit { get; set; }
        public DateTime? DateOfBirth { get; set; }
        public bool IsActive { get; set; }
        public byte[]? Photo { get; set; }
        public string? PhotoContentType { get; set; }

        // Short URL path to the physical file, not raw bytes — combine with
        // your API's base URL to load it, e.g. https://localhost:7079 + PhotoPath.
        public string? PhotoPath { get; set; }
        public int CustomerTypeId { get; set; }
        public string CustomerTypeName { get; set; } = string.Empty;
        public List<CustomerAddressReadDto> Addresses { get; set; } = new();
    }

    public class CustomerAddressReadDto
    {
        public int Id { get; set; }
        public string Label { get; set; } = string.Empty;
        public string AddressLine { get; set; } = string.Empty;
        public string? City { get; set; }
        public bool IsDefault { get; set; }
    }

    public class CustomerCreateDto
    {
        [MaxLength(50)]
        public string? FirstName { get; set; }

        [MaxLength(50)]
        public string? LastName { get; set; }

        public string? Phone { get; set; } // must be unique if provided

        [Range(0, double.MaxValue)]
        public decimal CreditLimit { get; set; } = 0;

        public DateTime? DateOfBirth { get; set; }

        public bool IsActive { get; set; } = true;

        public byte[]? Photo { get; set; }

        [MaxLength(100)]
        public string? PhotoContentType { get; set; }

        [MaxLength(500)]
        public string? PhotoPath { get; set; }

        [Required]
        public int CustomerTypeId { get; set; }

        public List<CustomerAddressWriteDto>? Addresses { get; set; }
    }

    public class CustomerUpdateDto
    {
        [MaxLength(50)]
        public string? FirstName { get; set; }

        [MaxLength(50)]
        public string? LastName { get; set; }

        public string? Phone { get; set; }

        [Range(0, double.MaxValue)]
        public decimal CreditLimit { get; set; }

        public DateTime? DateOfBirth { get; set; }

        public bool IsActive { get; set; }

        public byte[]? Photo { get; set; }

        [MaxLength(100)]
        public string? PhotoContentType { get; set; }

        [MaxLength(500)]
        public string? PhotoPath { get; set; }

        [Required]
        public int CustomerTypeId { get; set; }

        // Full sync, same convention as EmployeeUpdateDto.Documents.
        public List<CustomerAddressSyncDto>? Addresses { get; set; }
    }

    public class CustomerAddressWriteDto
    {
        [Required, MaxLength(50)]
        public string Label { get; set; } = string.Empty;

        [Required, MaxLength(250)]
        public string AddressLine { get; set; } = string.Empty;

        [MaxLength(100)]
        public string? City { get; set; }

        public bool IsDefault { get; set; } = false;
    }

    public class CustomerAddressSyncDto : CustomerAddressWriteDto
    {
        // null or 0 => insert a new address; otherwise update the matching one.
        public int? Id { get; set; }
    }
}