using System.ComponentModel.DataAnnotations;

namespace PharmacyV2.DTOs
{
    // ===================== Department =====================
    public class DepartmentReadDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public int EmployeeCount { get; set; }
    }

    public class DepartmentWriteDto
    {
        [Required, MaxLength(100)]
        public string Name { get; set; } = string.Empty;
    }

    // ===================== Unit =====================
    public class UnitReadDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    public class UnitWriteDto
    {
        [Required, MaxLength(50)]
        public string Name { get; set; } = string.Empty;
    }

    // ===================== Role =====================
    public class RoleReadDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public bool IsActive { get; set; }
        public int UserCount { get; set; }
    }

    public class RoleWriteDto
    {
        [Required, MaxLength(100)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(500)]
        public string? Description { get; set; }

        public bool IsActive { get; set; } = true;
    }

    // ===================== CustomerType =====================
    public class CustomerTypeReadDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public int CustomerCount { get; set; }
    }

    public class CustomerTypeWriteDto
    {
        [Required, MaxLength(50)]
        public string Name { get; set; } = string.Empty;
    }

    // ===================== SupplierType =====================
    public class SupplierTypeReadDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public int SupplierCount { get; set; }
    }

    public class SupplierTypeWriteDto
    {
        [Required, MaxLength(50)]
        public string Name { get; set; } = string.Empty;
    }

    // ===================== WarehouseType =====================
    public class WarehouseTypeReadDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public int WarehouseCount { get; set; }
    }

    public class WarehouseTypeWriteDto
    {
        [Required, MaxLength(50)]
        public string Name { get; set; } = string.Empty;
    }

    // ===================== Warehouse =====================
    public class WarehouseListItemDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Address { get; set; }
        public bool IsActive { get; set; }
        public decimal Capacity { get; set; }
        public DateTime? EstablishedDate { get; set; }
        public bool HasPhoto { get; set; }
        public int WarehouseTypeId { get; set; }
        public string WarehouseTypeName { get; set; } = string.Empty;
    }

    public class WarehouseReadDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Address { get; set; }
        public bool IsActive { get; set; }
        public decimal Capacity { get; set; }
        public DateTime? EstablishedDate { get; set; }
        public bool HasPhoto { get; set; }
        public string? PhotoContentType { get; set; }
        public int WarehouseTypeId { get; set; }
        public string WarehouseTypeName { get; set; } = string.Empty;
    }

    public class WarehouseWriteDto
    {
        [Required, MaxLength(200)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(500)]
        public string? Address { get; set; }

        public bool IsActive { get; set; } = true;

        [Range(0, double.MaxValue)]
        public decimal Capacity { get; set; }

        public DateTime? EstablishedDate { get; set; }

        public int WarehouseTypeId { get; set; }
    }

    // ===================== Company =====================
    public class CompanyReadDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public int ProductCount { get; set; }
    }

    public class CompanyWriteDto
    {
        [Required, MaxLength(150)]
        public string Name { get; set; } = string.Empty;

        public bool IsActive { get; set; } = true;
    }

    // ===================== DosageForm =====================
    public class DosageFormReadDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public int ProductCount { get; set; }
    }

    public class DosageFormWriteDto
    {
        [Required, MaxLength(100)]
        public string Name { get; set; } = string.Empty;
    }

    // ===================== ProductRak =====================
    public class ProductRakReadDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public int WarehouseId { get; set; }
        public string WarehouseName { get; set; } = string.Empty;
        public int StockRowCount { get; set; }
    }

    public class ProductRakWriteDto
    {
        [Required, MaxLength(100)]
        public string Name { get; set; } = string.Empty;

        public bool IsActive { get; set; } = true;

        public int WarehouseId { get; set; }
    }
}