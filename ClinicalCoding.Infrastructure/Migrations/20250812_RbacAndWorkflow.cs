using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicalCoding.Infrastructure.Migrations
{
    public partial class _20250812_RbacAndWorkflow : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "Episodes",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "SubmittedBy",
                table: "Episodes",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SubmittedOn",
                table: "Episodes",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReviewedBy",
                table: "Episodes",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ReviewedOn",
                table: "Episodes",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReviewNotes",
                table: "Episodes",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ClinicianQueries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EpisodeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ToClinician = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Subject = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Body = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedOn = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ExternalReference = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClinicianQueries", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ClinicianQueries_CreatedOn",
                table: "ClinicianQueries",
                column: "CreatedOn");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "ClinicianQueries");

            migrationBuilder.DropColumn(name: "Status", table: "Episodes");
            migrationBuilder.DropColumn(name: "SubmittedBy", table: "Episodes");
            migrationBuilder.DropColumn(name: "SubmittedOn", table: "Episodes");
            migrationBuilder.DropColumn(name: "ReviewedBy", table: "Episodes");
            migrationBuilder.DropColumn(name: "ReviewedOn", table: "Episodes");
            migrationBuilder.DropColumn(name: "ReviewNotes", table: "Episodes");
        }
    }
}
