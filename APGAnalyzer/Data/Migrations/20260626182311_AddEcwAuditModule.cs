using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace APGAnalyzer.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddEcwAuditModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ecw_audit_batch",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PracticeName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    AuditDate = table.Column<DateOnly>(type: "date", nullable: false),
                    UploadedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    OwnerUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ecw_audit_batch", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ecw_billing_lag",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BatchId = table.Column<int>(type: "int", nullable: false),
                    EncounterId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    PatientAcctNo = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    PatientName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Provider = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    VisitType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    AppointmentDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ProgressNoteLastLockedOn = table.Column<DateOnly>(type: "date", nullable: true),
                    ChartLockStatus = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    DaysApptToLocked = table.Column<int>(type: "int", nullable: true),
                    ClaimNo = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ClaimDate = table.Column<DateOnly>(type: "date", nullable: true),
                    DaysPnToClaimCreated = table.Column<int>(type: "int", nullable: true),
                    WorkflowStatus = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ecw_billing_lag", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ecw_billing_lag_ecw_audit_batch_BatchId",
                        column: x => x.BatchId,
                        principalTable: "ecw_audit_batch",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ecw_claim_financial",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BatchId = table.Column<int>(type: "int", nullable: false),
                    ClaimNo = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ServiceDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ClaimDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ClaimStatusCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    ClaimStatusGroupName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    PrimaryPayer = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    SecondaryPayer = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    TertiaryPayer = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Facility = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    FacilityPos = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    AppointmentProvider = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    RenderingProvider = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Patient = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    PatientAcctNo = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    PatientGender = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    PatientAge = table.Column<int>(type: "int", nullable: true),
                    VisitType = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    ClaimVoided = table.Column<bool>(type: "bit", nullable: false),
                    BilledCharge = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    PayerCharge = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    SelfCharge = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Payments = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    PayerPayment = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    PatientPayment = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    ContractualAdjustment = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    PayerWithheld = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    WriteoffAdjustment = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Refund = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Balance = table.Column<decimal>(type: "decimal(18,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ecw_claim_financial", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ecw_claim_financial_ecw_audit_batch_BatchId",
                        column: x => x.BatchId,
                        principalTable: "ecw_audit_batch",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ecw_cpt_line",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BatchId = table.Column<int>(type: "int", nullable: false),
                    ClaimNo = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    PatientAcctNo = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Patient = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ServiceDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ClaimDate = table.Column<DateOnly>(type: "date", nullable: true),
                    PrimaryPayer = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Facility = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    FacilityPos = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    RenderingProvider = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CptCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    CptDescription = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    CptGroupName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Modifier1 = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    Modifier2 = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    Modifier3 = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    Modifier4 = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    Icd1Code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Icd1Name = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Icd2Code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Icd3Code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Icd4Code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    BilledCharge = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    TotalPayment = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    PayerPayment = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    PatientPayment = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    ContractualAdjustment = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    WriteoffAdjustment = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Balance = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    FeeScheduleAllowedFee = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    BilledUnits = table.Column<int>(type: "int", nullable: false),
                    IsTelevisit = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ecw_cpt_line", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ecw_cpt_line_ecw_audit_batch_BatchId",
                        column: x => x.BatchId,
                        principalTable: "ecw_audit_batch",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ecw_patient_aging",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BatchId = table.Column<int>(type: "int", nullable: false),
                    PatientName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    PatientAcctNo = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    PatientDob = table.Column<DateOnly>(type: "date", nullable: true),
                    ClaimNo = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ClaimDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ServiceDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ClaimAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Balance = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Days0To30 = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Days31To60 = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Days61To90 = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Days91To120 = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Days121To150 = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Days151To180 = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    DaysOver180 = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    NoOfStatementsSent = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ecw_patient_aging", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ecw_patient_aging_ecw_audit_batch_BatchId",
                        column: x => x.BatchId,
                        principalTable: "ecw_audit_batch",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ecw_payer_aging",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BatchId = table.Column<int>(type: "int", nullable: false),
                    IsPrimary = table.Column<bool>(type: "bit", nullable: false),
                    PayerName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    PatientName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    PatientAcctNo = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    AgingDays = table.Column<int>(type: "int", nullable: false),
                    ClaimDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ServiceDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ClaimFirstSubmittedDate = table.Column<DateOnly>(type: "date", nullable: true),
                    LastSubmissionDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ClaimNo = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Charges = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    DaysCurrent = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Days31To60 = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Days61To90 = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Days91To120 = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    DaysOver120 = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Balance = table.Column<decimal>(type: "decimal(18,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ecw_payer_aging", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ecw_payer_aging_ecw_audit_batch_BatchId",
                        column: x => x.BatchId,
                        principalTable: "ecw_audit_batch",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ecw_submission",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BatchId = table.Column<int>(type: "int", nullable: false),
                    ClaimNo = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    PatientAcctNo = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    PatientName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ServiceDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ClaimDate = table.Column<DateOnly>(type: "date", nullable: true),
                    SubmissionType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    SubmissionDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ClaimFirstSubmissionDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ClaimLastSubmissionDate = table.Column<DateOnly>(type: "date", nullable: true),
                    PayerName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    SubmissionCount = table.Column<int>(type: "int", nullable: false),
                    Charges = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    LogMessage = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ecw_submission", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ecw_submission_ecw_audit_batch_BatchId",
                        column: x => x.BatchId,
                        principalTable: "ecw_audit_batch",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ecw_billing_lag_BatchId",
                table: "ecw_billing_lag",
                column: "BatchId");

            migrationBuilder.CreateIndex(
                name: "IX_ecw_claim_financial_BatchId",
                table: "ecw_claim_financial",
                column: "BatchId");

            migrationBuilder.CreateIndex(
                name: "IX_ecw_claim_financial_ClaimNo",
                table: "ecw_claim_financial",
                column: "ClaimNo");

            migrationBuilder.CreateIndex(
                name: "IX_ecw_cpt_line_BatchId",
                table: "ecw_cpt_line",
                column: "BatchId");

            migrationBuilder.CreateIndex(
                name: "IX_ecw_cpt_line_ClaimNo",
                table: "ecw_cpt_line",
                column: "ClaimNo");

            migrationBuilder.CreateIndex(
                name: "IX_ecw_patient_aging_BatchId",
                table: "ecw_patient_aging",
                column: "BatchId");

            migrationBuilder.CreateIndex(
                name: "IX_ecw_payer_aging_BatchId",
                table: "ecw_payer_aging",
                column: "BatchId");

            migrationBuilder.CreateIndex(
                name: "IX_ecw_payer_aging_IsPrimary",
                table: "ecw_payer_aging",
                column: "IsPrimary");

            migrationBuilder.CreateIndex(
                name: "IX_ecw_submission_BatchId",
                table: "ecw_submission",
                column: "BatchId");

            migrationBuilder.CreateIndex(
                name: "IX_ecw_submission_ClaimNo",
                table: "ecw_submission",
                column: "ClaimNo");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ecw_billing_lag");

            migrationBuilder.DropTable(
                name: "ecw_claim_financial");

            migrationBuilder.DropTable(
                name: "ecw_cpt_line");

            migrationBuilder.DropTable(
                name: "ecw_patient_aging");

            migrationBuilder.DropTable(
                name: "ecw_payer_aging");

            migrationBuilder.DropTable(
                name: "ecw_submission");

            migrationBuilder.DropTable(
                name: "ecw_audit_batch");
        }
    }
}
