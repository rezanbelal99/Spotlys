using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Spotlys.Infrastructure.Migrations;

/// <inheritdoc />
public partial class AddWeatherHydrology : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "hydrology_observation",
            columns: table => new
            {
                zone = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                week_start_date = table.Column<DateOnly>(type: "date", nullable: false),
                source = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                fetched_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                fill_fraction = table.Column<float>(type: "real", nullable: false),
                capacity_twh = table.Column<float>(type: "real", nullable: false),
                fill_twh = table.Column<float>(type: "real", nullable: false),
                next_publication_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_hydrology_observation", x => new { x.zone, x.week_start_date, x.source });
            });

        migrationBuilder.CreateTable(
            name: "hydrology_week_norm",
            columns: table => new
            {
                zone = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                iso_week = table.Column<int>(type: "integer", nullable: false),
                min_fill_fraction = table.Column<float>(type: "real", nullable: false),
                median_fill_fraction = table.Column<float>(type: "real", nullable: false),
                max_fill_fraction = table.Column<float>(type: "real", nullable: false),
                fetched_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_hydrology_week_norm", x => new { x.zone, x.iso_week });
            });

        migrationBuilder.CreateTable(
            name: "weather_point",
            columns: table => new
            {
                id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                zone = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                lat = table.Column<decimal>(type: "numeric(7,4)", nullable: false),
                lon = table.Column<decimal>(type: "numeric(7,4)", nullable: false),
                altitude_m = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_weather_point", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "weather_fetch_cache",
            columns: table => new
            {
                point_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                last_modified = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                fetched_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_weather_fetch_cache", x => x.point_id);
                table.ForeignKey(
                    name: "FK_weather_fetch_cache_weather_point_point_id",
                    column: x => x.point_id,
                    principalTable: "weather_point",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "weather_forecast",
            columns: table => new
            {
                point_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                issued_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                valid_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                fetched_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                temp_c = table.Column<float>(type: "real", nullable: true),
                wind_ms = table.Column<float>(type: "real", nullable: true),
                wind_dir_deg = table.Column<float>(type: "real", nullable: true),
                cloud_frac = table.Column<float>(type: "real", nullable: true),
                precip_mm = table.Column<float>(type: "real", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_weather_forecast", x => new { x.point_id, x.issued_at_utc, x.valid_at_utc });
                table.ForeignKey(
                    name: "FK_weather_forecast_weather_point_point_id",
                    column: x => x.point_id,
                    principalTable: "weather_point",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_weather_forecast_valid_at_utc",
            table: "weather_forecast",
            column: "valid_at_utc")
            .Annotation("Npgsql:IndexMethod", "brin");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "hydrology_observation");

        migrationBuilder.DropTable(
            name: "hydrology_week_norm");

        migrationBuilder.DropTable(
            name: "weather_fetch_cache");

        migrationBuilder.DropTable(
            name: "weather_forecast");

        migrationBuilder.DropTable(
            name: "weather_point");
    }
}
