using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NexusOps.WorkflowOrchestrator.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OrderInvestigationSagaState",
                columns: table => new
                {
                    CorrelationId = table.Column<Guid>(type: "uuid", nullable: false),
                    CurrentState = table.Column<string>(type: "text", nullable: false),
                    OrderId = table.Column<string>(type: "text", nullable: false),
                    ResponseAddress = table.Column<string>(type: "text", nullable: true),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: true),
                    OrderFinding = table.Column<string>(type: "text", nullable: false),
                    InventoryFinding = table.Column<string>(type: "text", nullable: false),
                    ProductFinding = table.Column<string>(type: "text", nullable: false),
                    OrderResultJson = table.Column<string>(type: "text", nullable: true),
                    InventoryResultJson = table.Column<string>(type: "text", nullable: true),
                    ProductResultJson = table.Column<string>(type: "text", nullable: true),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderInvestigationSagaState", x => x.CorrelationId);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OrderInvestigationSagaState");
        }
    }
}
