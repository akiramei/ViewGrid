using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ViewGrid.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ExtendProtectedRegionColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<uint>(
                name: "fill_color",
                table: "image_copy_regions",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "offset_x_px",
                table: "image_copy_regions",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "offset_y_px",
                table: "image_copy_regions",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "fill_color",
                table: "image_copy_regions");

            migrationBuilder.DropColumn(
                name: "offset_x_px",
                table: "image_copy_regions");

            migrationBuilder.DropColumn(
                name: "offset_y_px",
                table: "image_copy_regions");
        }
    }
}
