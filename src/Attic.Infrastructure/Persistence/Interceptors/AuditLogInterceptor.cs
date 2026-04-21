using Attic.Infrastructure.Audit;
using Attic.Domain.Abstractions;
using Attic.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Attic.Infrastructure.Persistence.Interceptors;

public sealed class AuditLogInterceptor(AuditLogContext auditContext, IClock clock) : SaveChangesInterceptor
{
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        var ctx = eventData.Context;
        if (ctx is null) return base.SavingChangesAsync(eventData, result, cancellationToken);

        var entries = auditContext.Drain();
        if (entries.Count == 0) return base.SavingChangesAsync(eventData, result, cancellationToken);

        var now = clock.UtcNow;
        foreach (var e in entries)
        {
            ctx.Add(AuditLog.Create(
                e.ActorUserId, e.Action, e.TargetChannelId, e.TargetUserId, e.TargetMessageId, e.DataJson, now));
        }

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }
}
