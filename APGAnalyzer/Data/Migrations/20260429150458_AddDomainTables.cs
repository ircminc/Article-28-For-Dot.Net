using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace APGAnalyzer.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDomainTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "apg_base_rates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Source = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    PeerGroup = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CureCode = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    BaseRateCode = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    BlendRateCode = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    CapitalRateCode = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    Region = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    CheatFlag = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    EffectiveDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Rate = table.Column<decimal>(type: "decimal(12,4)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_apg_base_rates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "apg_weights",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Apg = table.Column<int>(type: "int", nullable: false),
                    ApgDescription = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    EffectiveDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Weight = table.Column<decimal>(type: "decimal(12,6)", nullable: false),
                    IsFinalRate = table.Column<bool>(type: "bit", nullable: false),
                    YearRate = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_apg_weights", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "fee_schedule",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Hcpcs = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    EffectiveDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Reimbursement = table.Column<decimal>(type: "decimal(12,2)", nullable: false),
                    MaxUnits = table.Column<decimal>(type: "decimal(10,2)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fee_schedule", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "hcpcs_to_eapg",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Hcpcs = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    Eapg = table.Column<int>(type: "int", nullable: false),
                    EapgDesc = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    EapgType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    EapgCategory = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    EapgServiceLine = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    QuarterEffectiveDate = table.Column<DateOnly>(type: "date", nullable: true),
                    QuarterEndDate = table.Column<DateOnly>(type: "date", nullable: true),
                    MidQuarterEffectiveDate = table.Column<DateOnly>(type: "date", nullable: true),
                    MidQuarterEndDate = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_hcpcs_to_eapg", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "icd10_to_eapg",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DxCode = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    Gender = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: true),
                    Eapg = table.Column<int>(type: "int", nullable: false),
                    EapgDesc = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    EapgType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    EapgCategory = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    EapgServiceLine = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    EffectiveDate = table.Column<DateOnly>(type: "date", nullable: true),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_icd10_to_eapg", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "provider_county",
                columns: table => new
                {
                    CountyCode = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CountyName = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    HealthHomePhase = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    Region = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_provider_county", x => x.CountyCode);
                });

            migrationBuilder.CreateTable(
                name: "px_based_weights",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Hcpcs = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    EffectiveDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Weight = table.Column<decimal>(type: "decimal(12,6)", nullable: false),
                    UnitsLimit = table.Column<decimal>(type: "decimal(10,2)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_px_based_weights", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_apg_base_rates_EffectiveDate",
                table: "apg_base_rates",
                column: "EffectiveDate");

            migrationBuilder.CreateIndex(
                name: "IX_apg_base_rates_PeerGroup",
                table: "apg_base_rates",
                column: "PeerGroup");

            migrationBuilder.CreateIndex(
                name: "IX_apg_base_rates_Region",
                table: "apg_base_rates",
                column: "Region");

            migrationBuilder.CreateIndex(
                name: "IX_apg_base_rates_Source",
                table: "apg_base_rates",
                column: "Source");

            migrationBuilder.CreateIndex(
                name: "ix_base_rate_lookup",
                table: "apg_base_rates",
                columns: new[] { "Source", "PeerGroup", "Region", "EffectiveDate" });

            migrationBuilder.CreateIndex(
                name: "IX_apg_weights_Apg",
                table: "apg_weights",
                column: "Apg");

            migrationBuilder.CreateIndex(
                name: "IX_apg_weights_EffectiveDate",
                table: "apg_weights",
                column: "EffectiveDate");

            migrationBuilder.CreateIndex(
                name: "uq_apg_weight_date",
                table: "apg_weights",
                columns: new[] { "Apg", "EffectiveDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_fee_schedule_EffectiveDate",
                table: "fee_schedule",
                column: "EffectiveDate");

            migrationBuilder.CreateIndex(
                name: "IX_fee_schedule_Hcpcs",
                table: "fee_schedule",
                column: "Hcpcs");

            migrationBuilder.CreateIndex(
                name: "ix_fee_schedule_lookup",
                table: "fee_schedule",
                columns: new[] { "Hcpcs", "EffectiveDate" });

            migrationBuilder.CreateIndex(
                name: "ix_hcpcs_eapg_code_date",
                table: "hcpcs_to_eapg",
                columns: new[] { "Hcpcs", "QuarterEffectiveDate" });

            migrationBuilder.CreateIndex(
                name: "IX_hcpcs_to_eapg_Eapg",
                table: "hcpcs_to_eapg",
                column: "Eapg");

            migrationBuilder.CreateIndex(
                name: "IX_hcpcs_to_eapg_EapgType",
                table: "hcpcs_to_eapg",
                column: "EapgType");

            migrationBuilder.CreateIndex(
                name: "IX_hcpcs_to_eapg_Hcpcs",
                table: "hcpcs_to_eapg",
                column: "Hcpcs");

            migrationBuilder.CreateIndex(
                name: "IX_hcpcs_to_eapg_QuarterEffectiveDate",
                table: "hcpcs_to_eapg",
                column: "QuarterEffectiveDate");

            migrationBuilder.CreateIndex(
                name: "ix_icd10_eapg_code_date",
                table: "icd10_to_eapg",
                columns: new[] { "DxCode", "EffectiveDate" });

            migrationBuilder.CreateIndex(
                name: "IX_icd10_to_eapg_DxCode",
                table: "icd10_to_eapg",
                column: "DxCode");

            migrationBuilder.CreateIndex(
                name: "IX_icd10_to_eapg_Eapg",
                table: "icd10_to_eapg",
                column: "Eapg");

            migrationBuilder.CreateIndex(
                name: "IX_icd10_to_eapg_EapgType",
                table: "icd10_to_eapg",
                column: "EapgType");

            migrationBuilder.CreateIndex(
                name: "IX_icd10_to_eapg_EffectiveDate",
                table: "icd10_to_eapg",
                column: "EffectiveDate");

            migrationBuilder.CreateIndex(
                name: "IX_provider_county_CountyName",
                table: "provider_county",
                column: "CountyName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_px_based_weights_EffectiveDate",
                table: "px_based_weights",
                column: "EffectiveDate");

            migrationBuilder.CreateIndex(
                name: "IX_px_based_weights_Hcpcs",
                table: "px_based_weights",
                column: "Hcpcs");

            migrationBuilder.CreateIndex(
                name: "ix_px_weight_lookup",
                table: "px_based_weights",
                columns: new[] { "Hcpcs", "EffectiveDate" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "apg_base_rates");

            migrationBuilder.DropTable(
                name: "apg_weights");

            migrationBuilder.DropTable(
                name: "fee_schedule");

            migrationBuilder.DropTable(
                name: "hcpcs_to_eapg");

            migrationBuilder.DropTable(
                name: "icd10_to_eapg");

            migrationBuilder.DropTable(
                name: "provider_county");

            migrationBuilder.DropTable(
                name: "px_based_weights");
        }
    }
}
