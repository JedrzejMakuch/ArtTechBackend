using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArtTechGallery.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ExhibitionPublicationStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "Exhibitions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // Before this lifecycle existed, active exhibitions were already public.
            migrationBuilder.Sql("UPDATE \"Exhibitions\" SET \"Status\" = CASE WHEN \"IsActive\" THEN 1 ELSE 2 END");
            migrationBuilder.DropColumn(name: "IsActive", table: "Exhibitions");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Exhibitions_Status",
                table: "Exhibitions",
                sql: "\"Status\" IN (0, 1, 2)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Exhibitions_Status",
                table: "Exhibitions");

            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "Exhibitions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // A rollback cannot represent drafts; keep all non-published content hidden.
            migrationBuilder.Sql("UPDATE \"Exhibitions\" SET \"IsActive\" = (\"Status\" = 1)");
            migrationBuilder.DropColumn(name: "Status", table: "Exhibitions");
        }
    }
}
