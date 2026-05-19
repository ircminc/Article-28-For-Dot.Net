using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace APGAnalyzer.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCmsRateCache : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "cms_rate_cache",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Hcpcs = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    Modifier = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: false),
                    Locality = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: false),
                    Year = table.Column<int>(type: "int", nullable: false),
                    NonFacilityRate = table.Column<decimal>(type: "decimal(12,4)", nullable: true),
                    FacilityRate = table.Column<decimal>(type: "decimal(12,4)", nullable: true),
                    WorkRvu = table.Column<decimal>(type: "decimal(10,4)", nullable: true),
                    PeRvu = table.Column<decimal>(type: "decimal(10,4)", nullable: true),
                    MpRvu = table.Column<decimal>(type: "decimal(10,4)", nullable: true),
                    TotalRvu = table.Column<decimal>(type: "decimal(10,4)", nullable: true),
                    ConversionFactor = table.Column<decimal>(type: "decimal(10,4)", nullable: true),
                    RawPayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CachedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CachedUntil = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cms_rate_cache", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_cms_rate_cache_CachedUntil",
                table: "cms_rate_cache",
                column: "CachedUntil");

            migrationBuilder.CreateIndex(
                name: "uq_cms_rate_cache_key",
                table: "cms_rate_cache",
                columns: new[] { "Hcpcs", "Modifier", "Locality", "Year" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cms_rate_cache");
        }
    }
}
