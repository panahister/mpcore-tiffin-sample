using Microsoft.EntityFrameworkCore;
using MPCore.Audit.EntityFrameworkCore;
using MPCore.Domain.Events;
using MPCore.Idempotency.EntityFrameworkCore;
using MPCore.Persistence.EntityFrameworkCore.PostgreSql;

namespace Tiffin.Payments.Infrastructure.Persistence;

public sealed class AppDbContext(
    DbContextOptions<AppDbContext> options,
    TimeProvider timeProvider,
    IAggregateEventSink eventSink)
    : MPCoreDbContext(options, timeProvider, eventSink)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
        // The audit trail and the inbox belong to this context, so they commit with the change they describe.
        modelBuilder.ApplyMPCoreAudit();
        modelBuilder.ApplyMPCoreIdempotency();
        base.OnModelCreating(modelBuilder);
    }
}
