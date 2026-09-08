using System.ComponentModel.DataAnnotations;

namespace PharmacyV2.Models.HR
{
    // Detail record — many documents belong to one Employee (master).
    // Managed entirely through EmployeesController (nested create/update,
    // plus dedicated /documents endpoints), same as Product/ProductDetails.
    public class EmployeeDocument
    {
        [Key]
        public int Id { get; set; }

        public int EmployeeId { get; set; }
        public Employee Employee { get; set; } = null!;

        [Required, MaxLength(150)]
        public string DocumentTitle { get; set; } = string.Empty; // e.g. "National ID", "Certificate"

        [MaxLength(100)]
        public string? DocumentNumber { get; set; }

        public DateTime IssueDate { get; set; }
        public DateTime? ExpiryDate { get; set; }

        public bool IsVerified { get; set; } = false;
    }
}
