using System.ComponentModel.DataAnnotations;

namespace PharmacyV2.Models.Branch
{
    // Lookup table for Warehouse.WarehouseTypeId. No controller — seeded via
    // HasData() in PharmacyDbContext.
    public class WarehouseType
    {
        [Key]
        public int Id { get; set; }

        [Required, MaxLength(50)]
        public string Name { get; set; } = string.Empty;

        public List<Warehouse> Warehouses { get; set; } = new();
    }
}
