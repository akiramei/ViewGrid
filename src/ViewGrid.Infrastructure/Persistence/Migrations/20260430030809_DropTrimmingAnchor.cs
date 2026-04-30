using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ViewGrid.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DropTrimmingAnchor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "trim_anchor_x",
                table: "image_copies");

            migrationBuilder.DropColumn(
                name: "trim_anchor_y",
                table: "image_copies");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "trim_anchor_x",
                table: "image_copies",
                type: "TEXT",
                maxLength: 8,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "trim_anchor_y",
                table: "image_copies",
                type: "TEXT",
                maxLength: 8,
                nullable: false,
                defaultValue: "");
        }
    }
}
