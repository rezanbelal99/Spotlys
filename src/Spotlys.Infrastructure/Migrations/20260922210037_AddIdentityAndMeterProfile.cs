// Named "AddIdentityAndMeterProfile" rather than split into the two migrations the plan
// sketched ("AddIdentity" then "AddMeterProfileAndConsumption"): both sets of entity types
// were modeled before either `dotnet ef migrations add` ran, so EF computed one diff
// against the (empty) prior snapshot and there was nothing left for a second migration to
// contain. Bundling was the honest outcome, not a deviation worth working around -- these
// are all brand-new tables shipping together with zero production rows either way. Lock
// review: every operation here is CREATE SCHEMA / CREATE TABLE / CREATE INDEX against
// tables that don't exist yet, so there's no lock contention with live data. Rollback:
// Down() drops every table it created, acceptable since this ships with zero production
// rows (docs/DEVOPS.md §6).
using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Spotlys.Infrastructure.Migrations;

/// <inheritdoc />
public partial class AddIdentityAndMeterProfile : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(
            name: "metering");

        migrationBuilder.CreateTable(
            name: "app_user",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UserName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                NormalizedUserName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                NormalizedEmail = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                EmailConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                PasswordHash = table.Column<string>(type: "text", nullable: true),
                SecurityStamp = table.Column<string>(type: "text", nullable: true),
                ConcurrencyStamp = table.Column<string>(type: "text", nullable: true),
                PhoneNumber = table.Column<string>(type: "text", nullable: true),
                PhoneNumberConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                TwoFactorEnabled = table.Column<bool>(type: "boolean", nullable: false),
                LockoutEnd = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                LockoutEnabled = table.Column<bool>(type: "boolean", nullable: false),
                AccessFailedCount = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_app_user", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "consumption_retention_purge",
            columns: table => new
            {
                id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                meter_profile_id = table.Column<Guid>(type: "uuid", nullable: false),
                purged_through_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                rows_deleted = table.Column<int>(type: "integer", nullable: false),
                run_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_consumption_retention_purge", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "app_user_claim",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                UserId = table.Column<Guid>(type: "uuid", nullable: false),
                ClaimType = table.Column<string>(type: "text", nullable: true),
                ClaimValue = table.Column<string>(type: "text", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_app_user_claim", x => x.Id);
                table.ForeignKey(
                    name: "FK_app_user_claim_app_user_UserId",
                    column: x => x.UserId,
                    principalTable: "app_user",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "app_user_login",
            columns: table => new
            {
                LoginProvider = table.Column<string>(type: "text", nullable: false),
                ProviderKey = table.Column<string>(type: "text", nullable: false),
                ProviderDisplayName = table.Column<string>(type: "text", nullable: true),
                UserId = table.Column<Guid>(type: "uuid", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_app_user_login", x => new { x.LoginProvider, x.ProviderKey });
                table.ForeignKey(
                    name: "FK_app_user_login_app_user_UserId",
                    column: x => x.UserId,
                    principalTable: "app_user",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "app_user_token",
            columns: table => new
            {
                UserId = table.Column<Guid>(type: "uuid", nullable: false),
                LoginProvider = table.Column<string>(type: "text", nullable: false),
                Name = table.Column<string>(type: "text", nullable: false),
                Value = table.Column<string>(type: "text", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_app_user_token", x => new { x.UserId, x.LoginProvider, x.Name });
                table.ForeignKey(
                    name: "FK_app_user_token_app_user_UserId",
                    column: x => x.UserId,
                    principalTable: "app_user",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "meter_profile",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                user_id = table.Column<Guid>(type: "uuid", nullable: false),
                zone = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                grid_company_id = table.Column<string>(type: "character varying(50)", nullable: false),
                support_scheme = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                is_cabin = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                supplier_markup_ore = table.Column<decimal>(type: "numeric(8,4)", nullable: false, defaultValue: 0m),
                supplier_monthly_fee_nok = table.Column<decimal>(type: "numeric(8,2)", nullable: false, defaultValue: 0m),
                consumption_retention_years = table.Column<int>(type: "integer", nullable: false, defaultValue: 3),
                created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_meter_profile", x => x.id);
                table.ForeignKey(
                    name: "FK_meter_profile_app_user_user_id",
                    column: x => x.user_id,
                    principalTable: "app_user",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_meter_profile_grid_company_grid_company_id",
                    column: x => x.grid_company_id,
                    principalTable: "grid_company",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "consumption_reading",
            schema: "metering",
            columns: table => new
            {
                meter_profile_id = table.Column<Guid>(type: "uuid", nullable: false),
                hour_start_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                kwh = table.Column<decimal>(type: "numeric(10,4)", nullable: false),
                source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                imported_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_consumption_reading", x => new { x.meter_profile_id, x.hour_start_utc });
                table.ForeignKey(
                    name: "FK_consumption_reading_meter_profile_meter_profile_id",
                    column: x => x.meter_profile_id,
                    principalTable: "meter_profile",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "EmailIndex",
            table: "app_user",
            column: "NormalizedEmail");

        migrationBuilder.CreateIndex(
            name: "UserNameIndex",
            table: "app_user",
            column: "NormalizedUserName",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_app_user_claim_UserId",
            table: "app_user_claim",
            column: "UserId");

        migrationBuilder.CreateIndex(
            name: "IX_app_user_login_UserId",
            table: "app_user_login",
            column: "UserId");

        migrationBuilder.CreateIndex(
            name: "IX_consumption_reading_hour_start_utc",
            schema: "metering",
            table: "consumption_reading",
            column: "hour_start_utc")
            .Annotation("Npgsql:IndexMethod", "brin");

        migrationBuilder.CreateIndex(
            name: "IX_meter_profile_grid_company_id",
            table: "meter_profile",
            column: "grid_company_id");

        migrationBuilder.CreateIndex(
            name: "IX_meter_profile_user_id",
            table: "meter_profile",
            column: "user_id");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "app_user_claim");

        migrationBuilder.DropTable(
            name: "app_user_login");

        migrationBuilder.DropTable(
            name: "app_user_token");

        migrationBuilder.DropTable(
            name: "consumption_reading",
            schema: "metering");

        migrationBuilder.DropTable(
            name: "consumption_retention_purge");

        migrationBuilder.DropTable(
            name: "meter_profile");

        migrationBuilder.DropTable(
            name: "app_user");
    }
}
