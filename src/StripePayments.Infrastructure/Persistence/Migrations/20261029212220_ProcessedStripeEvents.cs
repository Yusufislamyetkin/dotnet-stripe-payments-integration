using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StripePayments.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProcessedStripeEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "processed_stripe_events",
                columns: table => new
                {
                    EventId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Handled = table.Column<bool>(type: "boolean", nullable: false),
                    ProcessedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_processed_stripe_events", x => x.EventId);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "processed_stripe_events");
        }
    }
}
