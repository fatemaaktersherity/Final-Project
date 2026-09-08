using System.ComponentModel.DataAnnotations;

namespace PharmacyV2.DTOs
{
    public class DoctorReadDto
    {
        public int DoctorId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Specialization { get; set; }
        public string? RegistrationNo { get; set; }
        public string? Hospital { get; set; }
        public string? Phone { get; set; }
        public string? Email { get; set; }
        public string? Address { get; set; }
        public bool IsActive { get; set; }
        public int PrescriptionCount { get; set; }
    }

    public class DoctorWriteDto
    {
        [Required, MaxLength(150)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(100)]
        public string? Specialization { get; set; }

        [MaxLength(100)]
        public string? RegistrationNo { get; set; }

        [MaxLength(150)]
        public string? Hospital { get; set; }

        [MaxLength(20)]
        public string? Phone { get; set; }

        [MaxLength(150)]
        public string? Email { get; set; }

        [MaxLength(300)]
        public string? Address { get; set; }

        public bool IsActive { get; set; } = true;
    }
}