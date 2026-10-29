using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StripePayments.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OrderPromotionCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PromotionCode",
                table: "orders",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PromotionCode",
                table: "orders");
        }
    }
}
