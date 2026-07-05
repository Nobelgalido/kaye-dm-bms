using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KayeDM.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDailyClosing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DailyClosings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Date = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TotalSales = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    CashSales = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    GCashSales = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    OrderCount = table.Column<int>(type: "int", nullable: false),
                    VoidedCount = table.Column<int>(type: "int", nullable: false),
                    CrewMealsGiven = table.Column<int>(type: "int", nullable: false),
                    TotalExpenses = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    NetForDay = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    ClosedById = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    ClosedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DailyClosings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DailyClosings_Date",
                table: "DailyClosings",
                column: "Date",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DailyClosings");
        }
    }
}
