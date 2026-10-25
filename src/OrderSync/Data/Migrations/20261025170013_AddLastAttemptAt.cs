using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OrderSync.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddLastAttemptAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "LastAttemptAt",
                table: "SyncedOrders",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastAttemptAt",
                table: "SyncedOrders");
        }
    }
}
