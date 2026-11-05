using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kanabol.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRoleNameCaseIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Role names are unique whatever their case. EF can't model an index on an expression, so it's
            // here, next to ux_roles_name, which stays for the exact name the model knows about.
            migrationBuilder.Sql("CREATE UNIQUE INDEX ux_roles_lower_name ON roles (lower(name));");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX ux_roles_lower_name;");
        }
    }
}
