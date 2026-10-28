using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InvoiceBridge.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddInvoiceSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CustomerName",
                table: "SyncedOrders",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "InvoiceDate",
                table: "SyncedOrders",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Total",
                table: "SyncedOrders",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "InvoiceLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SyncedOrderId = table.Column<int>(type: "INTEGER", nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Quantity = table.Column<int>(type: "INTEGER", nullable: false),
                    UnitPrice = table.Column<decimal>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvoiceLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InvoiceLines_SyncedOrders_SyncedOrderId",
                        column: x => x.SyncedOrderId,
                        principalTable: "SyncedOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceLines_SyncedOrderId",
                table: "InvoiceLines",
                column: "SyncedOrderId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InvoiceLines");

            migrationBuilder.DropColumn(
                name: "CustomerName",
                table: "SyncedOrders");

            migrationBuilder.DropColumn(
                name: "InvoiceDate",
                table: "SyncedOrders");

            migrationBuilder.DropColumn(
                name: "Total",
                table: "SyncedOrders");
        }
    }
}
