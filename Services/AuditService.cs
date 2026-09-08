using PharmacyV2.Data;
using PharmacyV2.Models.Other;

namespace PharmacyV2.Services;

public interface IAuditService { Task LogAsync(string action, string entityName, int entityId, string? details, string? userId); }
public sealed class AuditService : IAuditService
{
    private readonly PharmacyDbContext _db;
    public AuditService(PharmacyDbContext db) => _db = db;
    public async Task LogAsync(string action, string entityName, int entityId, string? details, string? userId)
    {
        _db.AuditLogs.Add(new AuditLog { Action = action, EntityName = entityName, EntityId = entityId, Details = details, UserId = userId, OccurredAt = DateTime.UtcNow });
        await _db.SaveChangesAsync();
    }
}
