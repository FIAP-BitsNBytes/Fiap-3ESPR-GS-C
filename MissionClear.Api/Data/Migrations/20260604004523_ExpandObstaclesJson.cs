using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MissionClear.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class ExpandObstaclesJson : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "obstacles_json",
                table: "missions",
                type: "longtext",
                nullable: false,
                defaultValue: "[]",
                oldClrType: typeof(string),
                oldType: "varchar(8000)",
                oldMaxLength: 8000);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "obstacles_json",
                table: "missions",
                type: "varchar(8000)",
                maxLength: 8000,
                nullable: false,
                defaultValue: "[]",
                oldClrType: typeof(string),
                oldType: "longtext");
        }
    }
}
