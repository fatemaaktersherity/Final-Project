using System.ComponentModel.DataAnnotations;

namespace PharmacyV2.DTOs;

public class ProductGroupReadDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? GenericName { get; set; }
    public int? CompanyId { get; set; }
    public string? CompanyName { get; set; }
    public int VariantCount { get; set; }
}

public class ProductGroupDetailDto : ProductGroupReadDto
{
    public List<ProductVariantReadDto> Variants { get; set; } = new();
}

public class ProductGroupWriteDto
{
    [Required, MaxLength(200)]
    public string Name { get; set; } = string.Empty;
    [MaxLength(200)]
    public string? GenericName { get; set; }
    public int? CompanyId { get; set; }
}

public class ProductVariantReadDto
{
    public int Id { get; set; }
    public int ProductGroupId { get; set; }
    public string Strength { get; set; } = string.Empty;
    public int DosageFormId { get; set; }
    public string DosageFormName { get; set; } = string.Empty;
}

public class ProductVariantWriteDto
{
    [Required, MaxLength(100)]
    public string Strength { get; set; } = string.Empty;
    [Required]
    public int DosageFormId { get; set; }
}
