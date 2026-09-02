using Microsoft.EntityFrameworkCore;
using NexusOps.Contracts.Dtos;

namespace NexusOps.WorkflowOrchestrator.OrderInvestigation;

/// <summary>
/// EF Core context backing <see cref="OrderInvestigationSaga"/>'s persisted state. The only
/// schema this feature owns — a future saga adds its own <see cref="DbContext"/>, not a table here.
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
    }
}
