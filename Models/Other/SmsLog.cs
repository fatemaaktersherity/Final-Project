using System.ComponentModel.DataAnnotations;

namespace PharmacyV2.Models.Other
{
    public class SmsLog
    {
        public int Id { get; set; }

        [Required, MaxLength(20)]
        public string PhoneNumber { get; set; } = string.Empty;

        [Required, MaxLength(500)]
        public string Message { get; set; } = string.Empty;

        public DateTime SentAt { get; set; } = DateTime.Now;

        public bool IsSuccess { get; set; }

        [MaxLength(500)]
        public string? Response { get; set; }
    }
}