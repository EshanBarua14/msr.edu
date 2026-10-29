using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MsrEdu.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class DriveSync : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "drive_mime_type",
                table: "resource_sources",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "drive_folder_id",
                table: "courses",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "drive_synced_at",
                table: "courses",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "drive_mime_type",
                table: "resource_sources");

            migrationBuilder.DropColumn(
                name: "drive_folder_id",
                table: "courses");

            migrationBuilder.DropColumn(
                name: "drive_synced_at",
                table: "courses");
        }
    }
}
