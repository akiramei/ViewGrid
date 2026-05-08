using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ViewGrid.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddImageCopyRegions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "image_copy_regions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ImageCopyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    fill_mode = table.Column<int>(type: "INTEGER", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    rect_height = table.Column<double>(type: "REAL", nullable: false),
                    rect_width = table.Column<double>(type: "REAL", nullable: false),
                    rect_x = table.Column<double>(type: "REAL", nullable: false),
                    rect_y = table.Column<double>(type: "REAL", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_image_copy_regions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_image_copy_regions_image_copies_ImageCopyId",
                        column: x => x.ImageCopyId,
                        principalTable: "image_copies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_image_copy_regions_ImageCopyId",
                table: "image_copy_regions",
                column: "ImageCopyId");

            migrationBuilder.CreateIndex(
                name: "IX_image_copy_regions_ImageCopyId_SortOrder",
                table: "image_copy_regions",
                columns: new[] { "ImageCopyId", "SortOrder" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "image_copy_regions");
        }
    }
}
