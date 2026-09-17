using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Spotlys.Infrastructure.Migrations;

/// <inheritdoc />
public partial class AddGridTariffSchemeParameter : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "grid_company",
            columns: table => new
            {
                id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_grid_company", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "grid_tariff",
            columns: table => new
            {
                grid_company_id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                valid_from = table.Column<DateOnly>(type: "date", nullable: false),
                energy_day_ore = table.Column<decimal>(type: "numeric(8,4)", nullable: false),
                energy_night_ore = table.Column<decimal>(type: "numeric(8,4)", nullable: false),
                capacity_steps = table.Column<string>(type: "jsonb", nullable: false),
                source_url = table.Column<string>(type: "text", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_grid_tariff", x => new { x.grid_company_id, x.valid_from });
            });

        migrationBuilder.CreateTable(
            name: "scheme_parameter",
            columns: table => new
            {
                scheme = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                parameter = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                valid_from = table.Column<DateOnly>(type: "date", nullable: false),
                valid_to = table.Column<DateOnly>(type: "date", nullable: true),
                value = table.Column<decimal>(type: "numeric(12,4)", nullable: false),
                source_url = table.Column<string>(type: "text", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_scheme_parameter", x => new { x.scheme, x.parameter, x.valid_from });
            });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "grid_company");

        migrationBuilder.DropTable(
            name: "grid_tariff");

        migrationBuilder.DropTable(
            name: "scheme_parameter");
    }
}
