using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace APGAnalyzer.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddProviderConfig : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "provider_config",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    ProviderName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Npi = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    CountyCode = table.Column<int>(type: "int", nullable: true),
                    Region = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    PeerGroup = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ProviderType = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    CapitalAddonEligible = table.Column<bool>(type: "bit", nullable: false),
                    CapitalAddonRate = table.Column<decimal>(type: "decimal(12,4)", nullable: true),
                    RateCodeOverride = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    CmsLocality = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_provider_config", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_provider_config_IsActive",
                table: "provider_config",
                column: "IsActive");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "provider_config");
        }
    }
}
