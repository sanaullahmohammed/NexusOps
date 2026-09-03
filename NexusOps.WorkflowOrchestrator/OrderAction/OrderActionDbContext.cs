using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace NexusOps.WorkflowOrchestrator.OrderAction;

/// <summary>
/// EF Core context backing <see cref="OrderActionSaga"/>'s persisted state, plus MassTransit's
/// transactional outbox tables (research.md Decision 6) — this saga publishes side-effecting
/// commands, unlike feature 005's read-only investigation saga, so a redelivered message must not
/// be able to double-execute a mutation.
/// </summary>
public sealed class OrderActionDbContext(DbContextOptions<OrderActionDbContext> options) : DbContext(options)
{
    public DbSet<OrderActionSagaState> OrderActions => Set<OrderActionSagaState>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OrderActionSagaState>(b =>
        {
            b.ToTable("OrderActionSagaState");
            b.HasKey(x => x.CorrelationId);

            b.Property(x => x.OrderId).IsRequired();
            b.Property(x => x.CurrentState).IsRequired();

            b.Property(x => x.ActionType).HasConversion<string>();
            b.Property(x => x.ExecutionOutcome).HasConversion<string>();

            // Same Npgsql xmin convention as OrderInvestigationSagaState (005's research.md Decision 3).
            b.Property(x => x.RowVersion).IsRowVersion();
        });

        modelBuilder.AddInboxStateEntity();
        modelBuilder.AddOutboxMessageEntity();
        modelBuilder.AddOutboxStateEntity();
    }
}
