using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PharmacyV2.Models.HR
{
    // Master record. Demonstrates the six required data types in one entity:
    // text, number, date, boolean, image and relational (dropdown).
    // EmployeeDocument (below) is the "details" side of the master-details pair.
    public class Employee
    {
        [Key]
        public int Id { get; set; }

        // ----- text -----
        [Required, MaxLength(150)]
        public string FullName { get; set; } = string.Empty;

        [MaxLength(200)]
        public string? Email { get; set; }

        // ----- number -----
        [Column(TypeName = "decimal(18,2)")]
        public decimal Salary { get; set; }

        // ----- date -----
        public DateTime HireDate { get; set; }

        // ----- boolean -----
        public bool IsActive { get; set; } = true;

        // ----- image -----
        // Raw image bytes. System.Text.Json reads/writes byte[] as a base64
        // string automatically, so Postman just sends/receives base64 text
        // in the JSON body — no extra file-upload plumbing needed.
        public byte[]? Photo { get; set; }

        [MaxLength(100)]
        public string? PhotoContentType { get; set; } // e.g. "image/png", "image/jpeg"

        // Public URL path to the physical file under wwwroot/images/employees,
        // e.g. "/images/employees/3_a1b2c3d4.jpg". Set by the dedicated
        // POST /api/employees/{id}/photo upload endpoint — same pattern as
        // Product.ImagePath.
        [MaxLength(500)]
        public string? PhotoPath { get; set; }

        // ----- relational data (dropdown) -----
        // Same pattern as Product.UnitId -> Unit: the client picks a
        // DepartmentId from a dropdown populated by the seeded Department rows.
        public int DepartmentId { get; set; }
        public Department Department { get; set; } = null!;

        // ----- details side of the master-details pair -----
        public ICollection<EmployeeDocument> Documents { get; set; } = new List<EmployeeDocument>();
    }
}