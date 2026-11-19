using DocumentProcessing.Domain.Enums;
using DocumentProcessing.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DocumentProcessing.Infrastructure.Queue;

public interface IQueueLeaseRenewer
{
    /// <summary>Extends the lease if <paramref name="leaseOwner"/> still holds it; returns false when the lease was lost.</summary>
    Task<bool> RenewAsync(Guid commandId, string leaseOwner, TimeSpan leaseDuration, CancellationToken cancellationToken);
}

/// <summary>
/// Renews leases through its own <see cref="AppDbContext"/>, so it can run while the processor's context is busy.
/// </summary>
public sealed class QueueLeaseRenewer : IQueueLeaseRenewer
{
    private readonly IServiceScopeFactory _scopeFactory;

    public QueueLeaseRenewer(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public async Task<bool> RenewAsync(Guid commandId, string leaseOwner, TimeSpan leaseDuration, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var expiresAt = DateTime.UtcNow + leaseDuration;
        var renewed = await db.QueueCommands
            .Where(c => c.Id == commandId && c.LeaseOwner == leaseOwner && c.Status == QueueCommandStatus.Processing)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.LeaseExpiresAtUtc, expiresAt), cancellationToken);
        return renewed == 1;
    }
}
