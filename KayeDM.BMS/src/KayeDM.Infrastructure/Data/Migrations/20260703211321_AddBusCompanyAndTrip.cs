using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KayeDM.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBusCompanyAndTrip : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BusCompanies",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    ContactPerson = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    CrewMealAllowancePerTrip = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BusCompanies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BusTrips",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BusCompanyId = table.Column<int>(type: "int", nullable: false),
                    BusNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ArrivedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DepartedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Route = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    EstimatedPassengers = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BusTrips", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BusTrips_BusCompanies_BusCompanyId",
                        column: x => x.BusCompanyId,
                        principalTable: "BusCompanies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BusTrips_BusCompanyId",
                table: "BusTrips",
                column: "BusCompanyId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BusTrips");

            migrationBuilder.DropTable(
                name: "BusCompanies");
        }
    }
}
