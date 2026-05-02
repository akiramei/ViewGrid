using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ViewGrid.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOccupySizeToGridPlacement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // OccupySize を ImageCopy（バリアント）単位の共有特性から GridPlacement（配置）単位の
            // 固有特性へ移管する第 1 段階。既存データは image_copies の値をその全配置に複製する。
            // defaultValue=1 は OccupySize の positive 制約（Width/Height >= 1）を満たすため。
            // 続く UPDATE で旧 image_copies.occupy_* の値で上書きする。
            migrationBuilder.AddColumn<int>(
                name: "occupy_height",
                table: "grid_placements",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "occupy_width",
                table: "grid_placements",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1);

            // 既存データの移行: 各 grid_placement に対して、参照する image_copy の OccupySize を copy。
            // 同じ image_copy を参照する複数 placement は、これまで同じ OccupySize を共有していたので
            // どの placement にも同じ値が入る → 移行後の挙動（配置単位）でも初期状態は完全に同じ。
            migrationBuilder.Sql(
                "UPDATE grid_placements " +
                "SET occupy_width = (SELECT occupy_width FROM image_copies WHERE image_copies.Id = grid_placements.CopyId), " +
                "    occupy_height = (SELECT occupy_height FROM image_copies WHERE image_copies.Id = grid_placements.CopyId) " +
                "WHERE EXISTS (SELECT 1 FROM image_copies WHERE image_copies.Id = grid_placements.CopyId);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "occupy_height",
                table: "grid_placements");

            migrationBuilder.DropColumn(
                name: "occupy_width",
                table: "grid_placements");
        }
    }
}
