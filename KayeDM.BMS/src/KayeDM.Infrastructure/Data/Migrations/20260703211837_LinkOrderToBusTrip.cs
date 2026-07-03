using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KayeDM.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class LinkOrderToBusTrip : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Orders_BusTripId",
                table: "Orders",
                column: "BusTripId");

            migrationBuilder.AddForeignKey(
                name: "FK_Orders_BusTrips_BusTripId",
                table: "Orders",
                column: "BusTripId",
                principalTable: "BusTrips",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Orders_BusTrips_BusTripId",
                table: "Orders");

            migrationBuilder.DropIndex(
                name: "IX_Orders_BusTripId",
                table: "Orders");
        }
    }
}
