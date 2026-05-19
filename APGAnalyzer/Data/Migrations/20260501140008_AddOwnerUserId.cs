using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace APGAnalyzer.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddOwnerUserId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OwnerUserId",
                table: "provider_config",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OwnerUserId",
                table: "parsed_claim",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_provider_config_owner_active",
                table: "provider_config",
                columns: new[] { "OwnerUserId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "ix_parsed_claim_owner_created",
                table: "parsed_claim",
                columns: new[] { "OwnerUserId", "CreatedAt" });

            // Backfill: assign every existing row to the seeded admin user
            // (the first user in the admin role, which on this app is the
            // bootstrap admin@test.com). Q2 = Option A — clean ownership for
            // all legacy data, no NULL "shared" bucket.
            migrationBuilder.Sql(@"
                DECLARE @adminId NVARCHAR(450) = (
                    SELECT TOP 1 u.Id
                    FROM AspNetUsers u
                    INNER JOIN AspNetUserRoles ur ON ur.UserId = u.Id
                    INNER JOIN AspNetRoles      r  ON r.Id = ur.RoleId
                    WHERE r.NormalizedName = N'ADMIN'
                    ORDER BY u.Id
                );

                IF @adminId IS NOT NULL
                BEGIN
                    UPDATE parsed_claim
                       SET OwnerUserId = @adminId
                     WHERE OwnerUserId IS NULL;

                    UPDATE provider_config
                       SET OwnerUserId = @adminId
                     WHERE OwnerUserId IS NULL;
                END
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_provider_config_owner_active",
                table: "provider_config");

            migrationBuilder.DropIndex(
                name: "ix_parsed_claim_owner_created",
                table: "parsed_claim");

            migrationBuilder.DropColumn(
                name: "OwnerUserId",
                table: "provider_config");

            migrationBuilder.DropColumn(
                name: "OwnerUserId",
                table: "parsed_claim");
        }
    }
}
