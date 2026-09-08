using System.ComponentModel.DataAnnotations;

namespace PharmacyV2.Models.Other;

public class AuditLog
{
    public long Id { get; set; }
    [Required, MaxLength(100)] public string Action { get; set; } = string.Empty;
    [Required, MaxLength(100)] public string EntityName { get; set; } = string.Empty;
    public int EntityId { get; set; }
    [MaxLength(2000)] public string? Details { get; set; }
    public string? UserId { get; set; }
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
}
