using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ViewGrid.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRegionTransformColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "region_flip_x",
                table: "image_copy_regions",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "region_flip_y",
                table: "image_copy_regions",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "region_rotation",
                table: "image_copy_regions",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "region_flip_x",
                table: "image_copy_regions");

            migrationBuilder.DropColumn(
                name: "region_flip_y",
                table: "image_copy_regions");

            migrationBuilder.DropColumn(
                name: "region_rotation",
                table: "image_copy_regions");
        }
    }
}
