using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace APGAnalyzer.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddClaimTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "parsed_claim",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FileId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    FileType = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: false),
                    PayerName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    PayerId = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    ProviderNpi = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    ProviderName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    ClaimId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    PatientName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    PatientId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    DateOfService = table.Column<DateOnly>(type: "date", nullable: true),
                    ClaimStatus = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: true),
                    BilledAmount = table.Column<decimal>(type: "decimal(14,2)", nullable: false),
                    AllowedAmount = table.Column<decimal>(type: "decimal(14,2)", nullable: false),
                    PaidAmount = table.Column<decimal>(type: "decimal(14,2)", nullable: false),
                    PatientResponsibility = table.Column<decimal>(type: "decimal(14,2)", nullable: false),
                    ClaimFilingIndicator = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: true),
                    PrincipalDiagnosis = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    OtherDiagnosesJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LinkedClaimIdFk = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_parsed_claim", x => x.Id);
                    table.ForeignKey(
                        name: "FK_parsed_claim_parsed_claim_LinkedClaimIdFk",
                        column: x => x.LinkedClaimIdFk,
                        principalTable: "parsed_claim",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "apg_result",
                columns: table => new
                {
                    ClaimIdFk = table.Column<int>(type: "int", nullable: false),
                    CorrectApgPayment = table.Column<decimal>(type: "decimal(14,2)", nullable: false),
                    ActualPaid = table.Column<decimal>(type: "decimal(14,2)", nullable: false),
                    Variance = table.Column<decimal>(type: "decimal(14,2)", nullable: false),
                    CompressionPct = table.Column<decimal>(type: "decimal(10,4)", nullable: false),
                    Underpaid = table.Column<bool>(type: "bit", nullable: false),
                    Overpaid = table.Column<bool>(type: "bit", nullable: false),
                    BaseRateApplied = table.Column<decimal>(type: "decimal(12,4)", nullable: false),
                    PeerGroup = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Region = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    DiscountingApplied = table.Column<bool>(type: "bit", nullable: false),
                    U6Applied = table.Column<bool>(type: "bit", nullable: false),
                    CapitalApplied = table.Column<bool>(type: "bit", nullable: false),
                    LineDetailsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CalculatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_apg_result", x => x.ClaimIdFk);
                    table.ForeignKey(
                        name: "FK_apg_result_parsed_claim_ClaimIdFk",
                        column: x => x.ClaimIdFk,
                        principalTable: "parsed_claim",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "claim_adjustment",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ClaimIdFk = table.Column<int>(type: "int", nullable: false),
                    LineSeq = table.Column<int>(type: "int", nullable: true),
                    GroupCode = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: false),
                    ReasonCode = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(14,2)", nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_claim_adjustment", x => x.Id);
                    table.ForeignKey(
                        name: "FK_claim_adjustment_parsed_claim_ClaimIdFk",
                        column: x => x.ClaimIdFk,
                        principalTable: "parsed_claim",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "parsed_service_line",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ClaimIdFk = table.Column<int>(type: "int", nullable: false),
                    LineSeq = table.Column<int>(type: "int", nullable: false),
                    ProcedureCode = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    ModifiersJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RevenueCode = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: true),
                    BilledAmount = table.Column<decimal>(type: "decimal(14,2)", nullable: false),
                    AllowedAmount = table.Column<decimal>(type: "decimal(14,2)", nullable: false),
                    PaidAmount = table.Column<decimal>(type: "decimal(14,2)", nullable: false),
                    Units = table.Column<int>(type: "int", nullable: false),
                    DateOfService = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_parsed_service_line", x => x.Id);
                    table.ForeignKey(
                        name: "FK_parsed_service_line_parsed_claim_ClaimIdFk",
                        column: x => x.ClaimIdFk,
                        principalTable: "parsed_claim",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_claim_adjustment_ClaimIdFk",
                table: "claim_adjustment",
                column: "ClaimIdFk");

            migrationBuilder.CreateIndex(
                name: "IX_parsed_claim_ClaimId",
                table: "parsed_claim",
                column: "ClaimId");

            migrationBuilder.CreateIndex(
                name: "IX_parsed_claim_DateOfService",
                table: "parsed_claim",
                column: "DateOfService");

            migrationBuilder.CreateIndex(
                name: "IX_parsed_claim_FileId",
                table: "parsed_claim",
                column: "FileId");

            migrationBuilder.CreateIndex(
                name: "IX_parsed_claim_FileType",
                table: "parsed_claim",
                column: "FileType");

            migrationBuilder.CreateIndex(
                name: "IX_parsed_claim_LinkedClaimIdFk",
                table: "parsed_claim",
                column: "LinkedClaimIdFk");

            migrationBuilder.CreateIndex(
                name: "IX_parsed_claim_ProviderNpi",
                table: "parsed_claim",
                column: "ProviderNpi");

            migrationBuilder.CreateIndex(
                name: "IX_parsed_service_line_ClaimIdFk",
                table: "parsed_service_line",
                column: "ClaimIdFk");

            migrationBuilder.CreateIndex(
                name: "IX_parsed_service_line_ProcedureCode",
                table: "parsed_service_line",
                column: "ProcedureCode");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "apg_result");

            migrationBuilder.DropTable(
                name: "claim_adjustment");

            migrationBuilder.DropTable(
                name: "parsed_service_line");

            migrationBuilder.DropTable(
                name: "parsed_claim");
        }
    }
}
