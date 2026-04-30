using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ViewGrid.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddManualCropToImageCopy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "manual_crop_height",
                table: "image_copies",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "manual_crop_width",
                table: "image_copies",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "manual_crop_x",
                table: "image_copies",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "manual_crop_y",
                table: "image_copies",
                type: "REAL",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "manual_crop_height",
                table: "image_copies");

            migrationBuilder.DropColumn(
                name: "manual_crop_width",
                table: "image_copies");

            migrationBuilder.DropColumn(
                name: "manual_crop_x",
                table: "image_copies");

            migrationBuilder.DropColumn(
                name: "manual_crop_y",
                table: "image_copies");
        }
    }
}
