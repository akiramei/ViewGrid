using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ViewGrid.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RemoveIsActiveFromGridCanvas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_grid_canvases_IsActive",
                table: "grid_canvases");

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "grid_canvases");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "grid_canvases",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_grid_canvases_IsActive",
                table: "grid_canvases",
                column: "IsActive");
        }
    }
}
