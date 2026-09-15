using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Callout.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SecurityHardening : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SubmittedEmail",
                table: "Bookings",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SubmittedName",
                table: "Bookings",
                type: "character varying(120)",
                maxLength: 120,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SubmittedPhone",
                table: "Bookings",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "");

            // Preserve the best available contact history before anonymous profile edits stop.
            migrationBuilder.Sql("""
                UPDATE "Bookings" AS b SET "SubmittedName" = c."Name",
                    "SubmittedPhone" = c."Phone", "SubmittedEmail" = c."Email"
                FROM "Clients" AS c WHERE b."ClientId" = c."Id";
                """);

            migrationBuilder.CreateTable(
                name: "AdminSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CredentialVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RevokedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdminSessions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UsedAdminCodes",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UsedAdminCodes", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AdminSessions_ExpiresAtUtc",
                table: "AdminSessions",
                column: "ExpiresAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_UsedAdminCodes_ExpiresAtUtc",
                table: "UsedAdminCodes",
                column: "ExpiresAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AdminSessions");

            migrationBuilder.DropTable(
                name: "UsedAdminCodes");

            migrationBuilder.DropColumn(
                name: "SubmittedEmail",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "SubmittedName",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "SubmittedPhone",
                table: "Bookings");
        }
    }
}
