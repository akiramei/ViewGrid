using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ViewGrid.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "grid_canvases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    GridRows = table.Column<int>(type: "INTEGER", nullable: false),
                    GridCols = table.Column<int>(type: "INTEGER", nullable: false),
                    col_weights = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    row_weights = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<string>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<string>(type: "TEXT", nullable: false),
                    canvas_height = table.Column<int>(type: "INTEGER", nullable: false),
                    canvas_width = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_grid_canvases", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "image_assets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SourceType = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    OriginalFilename = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    StoredRelativePath = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    FileHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    FileSizeBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    MimeType = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<string>(type: "TEXT", nullable: false),
                    height = table.Column<int>(type: "INTEGER", nullable: false),
                    width = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_image_assets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "image_copies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    AssetId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CopyName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    scaling_mode = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<string>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<string>(type: "TEXT", nullable: false),
                    align_x = table.Column<string>(type: "TEXT", maxLength: 8, nullable: false),
                    align_y = table.Column<string>(type: "TEXT", maxLength: 8, nullable: false),
                    occupy_height = table.Column<int>(type: "INTEGER", nullable: false),
                    occupy_width = table.Column<int>(type: "INTEGER", nullable: false),
                    flip_x = table.Column<bool>(type: "INTEGER", nullable: false),
                    flip_y = table.Column<bool>(type: "INTEGER", nullable: false),
                    rotation = table.Column<int>(type: "INTEGER", nullable: false),
                    trim_anchor_x = table.Column<string>(type: "TEXT", maxLength: 8, nullable: false),
                    trim_anchor_y = table.Column<string>(type: "TEXT", maxLength: 8, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_image_copies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_image_copies_image_assets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "image_assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "grid_placements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    GridId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CopyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PixelOffsetX = table.Column<int>(type: "INTEGER", nullable: false),
                    PixelOffsetY = table.Column<int>(type: "INTEGER", nullable: false),
                    PlacementOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<string>(type: "TEXT", nullable: false),
                    grid_x = table.Column<int>(type: "INTEGER", nullable: false),
                    grid_y = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_grid_placements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_grid_placements_grid_canvases_GridId",
                        column: x => x.GridId,
                        principalTable: "grid_canvases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_grid_placements_image_copies_CopyId",
                        column: x => x.CopyId,
                        principalTable: "image_copies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_grid_canvases_IsActive",
                table: "grid_canvases",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_grid_canvases_UpdatedAt",
                table: "grid_canvases",
                column: "UpdatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_grid_placements_CopyId",
                table: "grid_placements",
                column: "CopyId");

            migrationBuilder.CreateIndex(
                name: "IX_grid_placements_GridId",
                table: "grid_placements",
                column: "GridId");

            migrationBuilder.CreateIndex(
                name: "IX_grid_placements_GridId_PlacementOrder",
                table: "grid_placements",
                columns: new[] { "GridId", "PlacementOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_image_assets_CreatedAt",
                table: "image_assets",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_image_assets_FileHash",
                table: "image_assets",
                column: "FileHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_image_copies_AssetId",
                table: "image_copies",
                column: "AssetId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "grid_placements");

            migrationBuilder.DropTable(
                name: "grid_canvases");

            migrationBuilder.DropTable(
                name: "image_copies");

            migrationBuilder.DropTable(
                name: "image_assets");
        }
    }
}
