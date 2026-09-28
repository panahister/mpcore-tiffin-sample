using Microsoft.EntityFrameworkCore;
using MPCore.Audit.EntityFrameworkCore;
using MPCore.Domain.Events;
using MPCore.Persistence.EntityFrameworkCore.PostgreSql;

namespace Tiffin.Restaurants.Infrastructure.Persistence;

public sealed class AppDbContext(
    DbContextOptions<AppDbContext> options,
    TimeProvider timeProvider,
    IAggregateEventSink eventSink)
    : MPCoreDbContext(options, timeProvider, eventSink)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
        modelBuilder.ApplyMPCoreAudit();
        base.OnModelCreating(modelBuilder);
    }
}
