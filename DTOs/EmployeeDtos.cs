using System.ComponentModel.DataAnnotations;

namespace PharmacyV2.DTOs
{
    // ---------- Read DTOs ----------

    public class EmployeeListItemDto
    {
        public int Id { get; set; }
        public string FullName { get; set; } = string.Empty;
        public string? Email { get; set; }
        public decimal Salary { get; set; }
        public DateTime HireDate { get; set; }
        public bool IsActive { get; set; }
        public int DepartmentId { get; set; }
        public string DepartmentName { get; set; } = string.Empty;
        public bool HasPhoto { get; set; }

        // Just the short URL path (not raw bytes) — matches ProductListItemDto.ImagePath.
        public string? PhotoPath { get; set; }
        // Per-employee documents, same shape as GetById returns.
        public List<EmployeeDocumentReadDto> Documents { get; set; } = new();
    }

    // Full shape for GET /api/employees/{id}
    public class EmployeeReadDto
    {
        public int Id { get; set; }
        public string FullName { get; set; } = string.Empty;
        public string? Email { get; set; }
        public decimal Salary { get; set; }
        public DateTime HireDate { get; set; }
        public bool IsActive { get; set; }

        // Short URL path to the physical file, not raw bytes — combine with
        // your API's base URL to load it, e.g. https://localhost:7079 + PhotoPath.
        public string? PhotoPath { get; set; }
        public string? PhotoContentType { get; set; }

        public int DepartmentId { get; set; }
        public string DepartmentName { get; set; } = string.Empty;

        public List<EmployeeDocumentReadDto> Documents { get; set; } = new();
    }

    public class EmployeeDocumentReadDto
    {
        public int Id { get; set; }
        public string DocumentTitle { get; set; } = string.Empty;
        public string? DocumentNumber { get; set; }
        public DateTime IssueDate { get; set; }
        public DateTime? ExpiryDate { get; set; }
        public bool IsVerified { get; set; }
    }

    // ---------- Write DTOs ----------

    public class EmployeeCreateDto
    {
        [Required, MaxLength(150)]
        public string FullName { get; set; } = string.Empty;

        [MaxLength(200), EmailAddress]
        public string? Email { get; set; }

        [Range(0, double.MaxValue)]
        public decimal Salary { get; set; }

        [Required]
        public DateTime HireDate { get; set; }

        public bool IsActive { get; set; } = true;

        public byte[]? Photo { get; set; }

        [MaxLength(100)]
        public string? PhotoContentType { get; set; }

        [MaxLength(500)]
        public string? PhotoPath { get; set; }

        [Required]
        public int DepartmentId { get; set; }

        public List<EmployeeDocumentWriteDto>? Documents { get; set; }
    }

    // Full replace on PUT — mirrors ProductUpdateDto's shape/behavior.
    public class EmployeeUpdateDto
    {
        [Required, MaxLength(150)]
        public string FullName { get; set; } = string.Empty;

        [MaxLength(200), EmailAddress]
        public string? Email { get; set; }

        [Range(0, double.MaxValue)]
        public decimal Salary { get; set; }

        [Required]
        public DateTime HireDate { get; set; }

        public bool IsActive { get; set; }

        public byte[]? Photo { get; set; }

        [MaxLength(100)]
        public string? PhotoContentType { get; set; }

        [MaxLength(500)]
        public string? PhotoPath { get; set; }

        [Required]
        public int DepartmentId { get; set; }

        // Full sync of the detail rows: existing Id -> update, no Id -> insert,
        // any existing document left out of this list -> deleted.
        public List<EmployeeDocumentSyncDto>? Documents { get; set; }
    }

    public class EmployeeDocumentWriteDto
    {
        [Required, MaxLength(150)]
        public string DocumentTitle { get; set; } = string.Empty;

        [MaxLength(100)]
        public string? DocumentNumber { get; set; }

        [Required]
        public DateTime IssueDate { get; set; }

        public DateTime? ExpiryDate { get; set; }

        public bool IsVerified { get; set; } = false;
    }

    public class EmployeeDocumentSyncDto : EmployeeDocumentWriteDto
    {
        // null or 0 => insert a new document; otherwise update the matching one.
        public int? Id { get; set; }
    }
}