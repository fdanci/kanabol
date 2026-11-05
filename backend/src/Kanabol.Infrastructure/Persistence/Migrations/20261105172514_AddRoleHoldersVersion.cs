using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kanabol.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRoleHoldersVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "holders_version",
                table: "roles",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "holders_version",
                table: "roles");
        }
    }
}
