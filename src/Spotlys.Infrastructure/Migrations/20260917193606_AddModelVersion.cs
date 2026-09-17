using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Spotlys.Infrastructure.Migrations;

/// <inheritdoc />
public partial class AddModelVersion : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "model_version",
            columns: table => new
            {
                id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                zone = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                regime = table.Column<string>(type: "character varying(1)", maxLength: 1, nullable: false),
                quantile = table.Column<decimal>(type: "numeric(3,2)", nullable: false),
                trained_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                train_data_to = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                git_sha = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                onnx_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                onnx_path = table.Column<string>(type: "text", nullable: false),
                backtest_mae = table.Column<decimal>(type: "numeric(10,4)", nullable: true),
                backtest_skill = table.Column<decimal>(type: "numeric(6,4)", nullable: true),
                is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_model_version", x => x.id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_model_version_zone_regime_quantile",
            table: "model_version",
            columns: new[] { "zone", "regime", "quantile" },
            unique: true,
            filter: "is_active");

        migrationBuilder.CreateIndex(
            name: "IX_model_version_zone_regime_quantile_trained_at_utc",
            table: "model_version",
            columns: new[] { "zone", "regime", "quantile", "trained_at_utc" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "model_version");
    }
}
