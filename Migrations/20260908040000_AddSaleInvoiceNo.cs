using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PharmacyV2.Migrations
{
    /// <inheritdoc />
    public partial class AddSaleInvoiceNo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Add as nullable first so it can be backfilled row-by-row before
            // the NOT NULL + UNIQUE constraints are applied below — a plain
            // NOT NULL default would give every existing row the same blank
            // value and immediately violate the unique index.
            migrationBuilder.AddColumn<string>(
                name: "InvoiceNo",
                table: "Sales",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            // One-time backfill for rows that existed before this column did.
            // Deterministic per-row (based on SaleId, which is already unique)
            // rather than random, so it's reproducible and obviously
            // distinguishable from a normally-generated "SINV-..." code if
            // anyone spots one in old data.
            migrationBuilder.Sql(
                "UPDATE Sales SET InvoiceNo = CONCAT('SINV-LEGACY-', RIGHT('000000' + CAST(SaleId AS varchar(6)), 6)) WHERE InvoiceNo IS NULL;");

            migrationBuilder.AlterColumn<string>(
                name: "InvoiceNo",
                table: "Sales",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: string.Empty,
                oldClrType: typeof(string),
                oldType: "nvarchar(50)",
                oldMaxLength: 50,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Sales_InvoiceNo",
                table: "Sales",
                column: "InvoiceNo",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Sales_InvoiceNo",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "InvoiceNo",
                table: "Sales");
        }
    }
}
