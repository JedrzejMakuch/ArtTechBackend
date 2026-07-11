using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArtTechGallery.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class UpdateShareCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "ShareCode",
                table: "Exhibitions",
                newName: "ExhibitionCode");

            migrationBuilder.RenameIndex(
                name: "IX_Exhibitions_ShareCode",
                table: "Exhibitions",
                newName: "IX_Exhibitions_ExhibitionCode");

            migrationBuilder.RenameColumn(
                name: "ShareCode",
                table: "ArtistProfiles",
                newName: "ProfileCode");

            migrationBuilder.RenameIndex(
                name: "IX_ArtistProfiles_ShareCode",
                table: "ArtistProfiles",
                newName: "IX_ArtistProfiles_ProfileCode");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "ExhibitionCode",
                table: "Exhibitions",
                newName: "ShareCode");

            migrationBuilder.RenameIndex(
                name: "IX_Exhibitions_ExhibitionCode",
                table: "Exhibitions",
                newName: "IX_Exhibitions_ShareCode");

            migrationBuilder.RenameColumn(
                name: "ProfileCode",
                table: "ArtistProfiles",
                newName: "ShareCode");

            migrationBuilder.RenameIndex(
                name: "IX_ArtistProfiles_ProfileCode",
                table: "ArtistProfiles",
                newName: "IX_ArtistProfiles_ShareCode");
        }
    }
}
