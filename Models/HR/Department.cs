using System.ComponentModel.DataAnnotations;

namespace PharmacyV2.Models.HR
{
    // Lookup table used as the dropdown source for Employee.DepartmentId.
    // Intentionally has no controller — it's reference data only, seeded
    // once via HasData() in PharmacyDbContext.OnModelCreating.
    public class Department
    {
        [Key]
        public int Id { get; set; }

        [Required, MaxLength(100)]
        public string Name { get; set; } = string.Empty;

        public List<Employee> Employees { get; set; } = new();
    }
}
