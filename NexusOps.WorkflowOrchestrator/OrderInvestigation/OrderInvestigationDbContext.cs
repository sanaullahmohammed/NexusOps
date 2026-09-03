using MassTransit;
using Microsoft.EntityFrameworkCore;
using NexusOps.Contracts.Dtos;

namespace NexusOps.WorkflowOrchestrator.OrderInvestigation;

/// <summary>
/// EF Core context backing <see cref="OrderInvestigationSaga"/>'s persisted state, plus MassTransit's
/// transactional outbox tables (008-order-investigation-outbox research.md Decision 2) — without
/// these, <c>Initially(When(Requested))</c>'s <c>Publish(BeginInvestigationFanOut)</c> is visible to
/// consumers before this context's own row-creating <c>SaveChanges()</c> commits, letting a fast
/// <c>OrderFindingReported</c> reply race ahead of the saga row it needs to attach to and get
/// silently discarded by <c>OnMissingInstance(m =&gt; m.Discard())</c>.
/// </summary>
public sealed class OrderInvestigationDbContext(DbContextOptions<OrderInvestigationDbContext> options)
    : DbContext(options)
{
    public DbSet<OrderInvestigationSagaState> Investigations => Set<OrderInvestigationSagaState>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OrderInvestigationSagaState>(b =>
        {
            b.ToTable("OrderInvestigationSagaState");
            b.HasKey(x => x.CorrelationId);

            b.Property(x => x.OrderId).IsRequired();
            b.Property(x => x.CurrentState).IsRequired();

            b.Property(x => x.OrderFinding).HasConversion<string>();
            b.Property(x => x.InventoryFinding).HasConversion<string>();
            b.Property(x => x.ProductFinding).HasConversion<string>();

            // Postgres has no rowversion/timestamp column type; a uint property marked IsRowVersion()
            // is what the Npgsql provider maps onto the xmin system column (the column that changes
            // on every UPDATE) -- the idiomatic Npgsql equivalent of SQL Server's ROWVERSION.
            b.Property(x => x.RowVersion).IsRowVersion();

            b.Ignore(x => x.AllSourcesReported);
        });

        // Deliberately the SAME physical InboxState/OutboxState/OutboxMessage tables
        // OrderActionDbContext (feature 006) already created in this database, under MassTransit's
        // default (unqualified, "public"-schema) names -- not a private copy. Two attempts at a
        // private copy (a renamed table, then a renamed schema) both broke: MassTransit's Postgres
        // row-lock statement provider generates its `SELECT ... FOR UPDATE` against a hardcoded,
        // unqualified "InboxState" reference regardless of how the entity's table/schema is actually
        // configured, so it always resolves to whichever "InboxState" is first on the connection's
        // search_path -- silently querying the *other* context's table and corrupting both. A single,
        // shared outbox per physical database is MassTransit's actual intended shape here: the
        // (MessageId, ConsumerId) key already partitions rows per receive endpoint (each saga's
        // endpoint gets its own ConsumerId), so there is no need for -- and, per the above, no safe
        // way to have -- a separate table set per saga sharing one database.
        modelBuilder.AddInboxStateEntity();
        modelBuilder.AddOutboxMessageEntity();
        modelBuilder.AddOutboxStateEntity();
    }
}
