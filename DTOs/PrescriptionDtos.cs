using System.ComponentModel.DataAnnotations;

namespace PharmacyV2.DTOs
{
    public class PrescriptionItemReadDto
    {
        public int Id { get; set; }
        public int? ProductId { get; set; }
        public string? ProductName { get; set; }
        public string MedicineName { get; set; } = string.Empty;
        public string? Dosage { get; set; }
        public string? Duration { get; set; }
        public string? Instructions { get; set; }
    }

    public class PrescriptionItemWriteDto
    {
        public int? ProductId { get; set; }

        [Required, MaxLength(200)]
        public string MedicineName { get; set; } = string.Empty;

        [MaxLength(100)]
        public string? Dosage { get; set; }

        [MaxLength(100)]
        public string? Duration { get; set; }

        [MaxLength(300)]
        public string? Instructions { get; set; }
    }

    public class PrescriptionReadDto
    {
        public int PrescriptionId { get; set; }
        public int DoctorId { get; set; }
        public string? DoctorName { get; set; }
        public int CustomerId { get; set; }
        public string? CustomerName { get; set; }
        public DateTime PrescriptionDate { get; set; }
        public string? Diagnosis { get; set; }
        public string? Notes { get; set; }
        public string? ImagePath { get; set; }
        public string? ImageContentType { get; set; }
        public int? SaleId { get; set; }
        public DateTime CreatedAt { get; set; }
        public List<PrescriptionItemReadDto> Items { get; set; } = new();
    }

    public class PrescriptionCreateDto
    {
        [Required]
        public int DoctorId { get; set; }

        [Required]
        public int CustomerId { get; set; }

        public DateTime PrescriptionDate { get; set; } = DateTime.UtcNow;

        [MaxLength(1000)]
        public string? Diagnosis { get; set; }

        [MaxLength(1000)]
        public string? Notes { get; set; }

        public int? SaleId { get; set; }

        public List<PrescriptionItemWriteDto>? Items { get; set; }
    }

    public class PrescriptionUpdateDto
    {
        [Required]
        public int DoctorId { get; set; }

        [Required]
        public int CustomerId { get; set; }

        public DateTime? PrescriptionDate { get; set; }

        [MaxLength(1000)]
        public string? Diagnosis { get; set; }

        [MaxLength(1000)]
        public string? Notes { get; set; }

        public int? SaleId { get; set; }

        public List<PrescriptionItemSyncDto>? Items { get; set; }
    }

    public class PrescriptionItemSyncDto : PrescriptionItemWriteDto
    {
        public int? Id { get; set; }
    }
}