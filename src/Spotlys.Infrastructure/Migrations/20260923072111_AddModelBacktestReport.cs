using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Spotlys.Infrastructure.Migrations;

/// <inheritdoc />
public partial class AddModelBacktestReport : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "model_backtest_report",
            columns: table => new
            {
                id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                zone = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                generated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                report_json = table.Column<string>(type: "jsonb", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_model_backtest_report", x => x.id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_model_backtest_report_zone_generated_at_utc",
            table: "model_backtest_report",
            columns: new[] { "zone", "generated_at_utc" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "model_backtest_report");
    }
}
