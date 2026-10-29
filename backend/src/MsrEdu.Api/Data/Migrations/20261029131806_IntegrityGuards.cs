using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MsrEdu.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class IntegrityGuards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql(SqlScripts.Read("integrity_guards.up.sql"));

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql(SqlScripts.Read("integrity_guards.down.sql"));
    }
}
