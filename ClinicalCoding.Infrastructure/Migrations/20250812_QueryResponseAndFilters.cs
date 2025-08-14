using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicalCoding.Infrastructure.Migrations
{
    public partial class _20250812_QueryResponseAndFilters : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ResponseText",
                table: "ClinicianQueries",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RespondedOn",
                table: "ClinicianQueries",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RespondedBy",
                table: "ClinicianQueries",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Episodes_AdmissionDate",
                table: "Episodes",
                column: "AdmissionDate");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Episodes_AdmissionDate",
                table: "Episodes");

            migrationBuilder.DropColumn(name: "ResponseText", table: "ClinicianQueries");
            migrationBuilder.DropColumn(name: "RespondedOn", table: "ClinicianQueries");
            migrationBuilder.DropColumn(name: "RespondedBy", table: "ClinicianQueries");
        }
    }
}
