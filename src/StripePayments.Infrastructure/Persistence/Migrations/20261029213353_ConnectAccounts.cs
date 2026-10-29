using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StripePayments.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ConnectAccounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "ApplicationFeeAmount",
                table: "orders",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ConnectedAccountId",
                table: "orders",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "connected_accounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StripeAccountId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    ChargesEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    PayoutsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    DetailsSubmitted = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_connected_accounts", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_connected_accounts_StripeAccountId",
                table: "connected_accounts",
                column: "StripeAccountId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "connected_accounts");

            migrationBuilder.DropColumn(
                name: "ApplicationFeeAmount",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "ConnectedAccountId",
                table: "orders");
        }
    }
}
