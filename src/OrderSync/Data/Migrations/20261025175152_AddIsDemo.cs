using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OrderSync.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddIsDemo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsDemo",
                table: "SyncedOrders",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsDemo",
                table: "SyncedOrders");
        }
    }
}
