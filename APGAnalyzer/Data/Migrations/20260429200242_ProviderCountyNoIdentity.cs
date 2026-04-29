using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace APGAnalyzer.Data.Migrations
{
    /// <summary>
    /// Removes the IDENTITY(1,1) annotation from provider_county.CountyCode
    /// so the loader can insert NYS DOH's published numeric county codes
    /// directly (60 = MANHATTAN, 58 = BRONX, etc.) instead of letting SQL
    /// Server auto-generate values.
    ///
    /// SQL Server doesn't allow changing the IDENTITY property of a column
    /// in place — the column must be dropped and recreated. The table is
    /// empty at this point in the schema lifecycle (provider_county was
    /// created in AddDomainTables but never populated, since the loader
    /// for it didn't exist until Phase 3), so we can rebuild the column
    /// without preserving data.
    /// </summary>
    public partial class ProviderCountyNoIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. Drop the unique index on CountyName (it depends on the table being intact)
            migrationBuilder.DropIndex(
                name: "IX_provider_county_CountyName",
                table: "provider_county");

            // 2. Drop the primary key — required before we can drop the PK column
            migrationBuilder.DropPrimaryKey(
                name: "PK_provider_county",
                table: "provider_county");

            // 3. Drop the IDENTITY column
            migrationBuilder.DropColumn(
                name: "CountyCode",
                table: "provider_county");

            // 4. Re-add it without IDENTITY
            migrationBuilder.AddColumn<int>(
                name: "CountyCode",
                table: "provider_county",
                type: "int",
                nullable: false,
                defaultValue: 0);

            // 5. Re-add the primary key
            migrationBuilder.AddPrimaryKey(
                name: "PK_provider_county",
                table: "provider_county",
                column: "CountyCode");

            // 6. Re-add the unique index on CountyName
            migrationBuilder.CreateIndex(
                name: "IX_provider_county_CountyName",
                table: "provider_county",
                column: "CountyName",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Reverse: drop & recreate with IDENTITY
            migrationBuilder.DropIndex(
                name: "IX_provider_county_CountyName",
                table: "provider_county");

            migrationBuilder.DropPrimaryKey(
                name: "PK_provider_county",
                table: "provider_county");

            migrationBuilder.DropColumn(
                name: "CountyCode",
                table: "provider_county");

            migrationBuilder.AddColumn<int>(
                name: "CountyCode",
                table: "provider_county",
                type: "int",
                nullable: false,
                defaultValue: 0)
                .Annotation("SqlServer:Identity", "1, 1");

            migrationBuilder.AddPrimaryKey(
                name: "PK_provider_county",
                table: "provider_county",
                column: "CountyCode");

            migrationBuilder.CreateIndex(
                name: "IX_provider_county_CountyName",
                table: "provider_county",
                column: "CountyName",
                unique: true);
        }
    }
}
