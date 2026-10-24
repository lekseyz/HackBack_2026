using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HackBack.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerRiskFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "BalanceDeficit",
                table: "Accounts",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "MonthlyRevenue",
                table: "Accounts",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "PrimaryReasons",
                table: "Accounts",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "RevenueAtRisk",
                table: "Accounts",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<DateTime>(
                name: "ScoredAt",
                table: "Accounts",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BalanceDeficit",
                table: "Accounts");

            migrationBuilder.DropColumn(
                name: "MonthlyRevenue",
                table: "Accounts");

            migrationBuilder.DropColumn(
                name: "PrimaryReasons",
                table: "Accounts");

            migrationBuilder.DropColumn(
                name: "RevenueAtRisk",
                table: "Accounts");

            migrationBuilder.DropColumn(
                name: "ScoredAt",
                table: "Accounts");
        }
    }
}
