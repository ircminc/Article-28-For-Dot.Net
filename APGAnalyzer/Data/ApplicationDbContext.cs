using APGAnalyzer.Models.Domain;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace APGAnalyzer.Data;

/// <summary>
/// EF Core context. Inherits IdentityDbContext so ASP.NET Core Identity's
/// auth tables (AspNetUsers, AspNetRoles, etc.) live alongside our domain
/// tables in the same database.
///
/// Domain entities map to the lower_case_underscore table names that match
/// the Python service's schema (see docs/DATABASE_SCHEMA.txt). Each entity
/// carries [Table("...")] so future renames don't break the contract.
/// </summary>
public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : IdentityDbContext(options)
{
    // Reference data (loaded from NYS DOH / eMedNY / PMTAC files)
    public DbSet<HcpcsToEapg> HcpcsToEapg => Set<HcpcsToEapg>();
    public DbSet<Icd10ToEapg> Icd10ToEapg => Set<Icd10ToEapg>();
    public DbSet<ApgWeight> ApgWeights => Set<ApgWeight>();
    public DbSet<ApgBaseRate> ApgBaseRates => Set<ApgBaseRate>();
    public DbSet<ProviderCounty> ProviderCounties => Set<ProviderCounty>();
    public DbSet<PxBasedWeight> PxBasedWeights => Set<PxBasedWeight>();
    public DbSet<FeeScheduleItem> FeeSchedule => Set<FeeScheduleItem>();

    // Operational
    public DbSet<ProviderConfig> ProviderConfigs => Set<ProviderConfig>();
    public DbSet<ParsedClaim> ParsedClaims => Set<ParsedClaim>();
    public DbSet<ParsedServiceLine> ParsedServiceLines => Set<ParsedServiceLine>();
    public DbSet<ClaimAdjustment> ClaimAdjustments => Set<ClaimAdjustment>();
    public DbSet<ApgResultRecord> ApgResults => Set<ApgResultRecord>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        // Single-column lookup indexes for hot paths in the APG engine.
        // Compound indexes are declared on the entities themselves via [Index].
        b.Entity<HcpcsToEapg>().HasIndex(x => x.Hcpcs);
        b.Entity<HcpcsToEapg>().HasIndex(x => x.Eapg);
        b.Entity<HcpcsToEapg>().HasIndex(x => x.EapgType);
        b.Entity<HcpcsToEapg>().HasIndex(x => x.QuarterEffectiveDate);
        b.Entity<HcpcsToEapg>()
            .HasIndex(x => new { x.Hcpcs, x.QuarterEffectiveDate })
            .HasDatabaseName("ix_hcpcs_eapg_code_date");

        b.Entity<Icd10ToEapg>().HasIndex(x => x.DxCode);
        b.Entity<Icd10ToEapg>().HasIndex(x => x.Eapg);
        b.Entity<Icd10ToEapg>().HasIndex(x => x.EapgType);
        b.Entity<Icd10ToEapg>().HasIndex(x => x.EffectiveDate);
        b.Entity<Icd10ToEapg>()
            .HasIndex(x => new { x.DxCode, x.EffectiveDate })
            .HasDatabaseName("ix_icd10_eapg_code_date");

        b.Entity<ApgWeight>().HasIndex(x => x.Apg);
        b.Entity<ApgWeight>().HasIndex(x => x.EffectiveDate);
        b.Entity<ApgWeight>()
            .HasIndex(x => new { x.Apg, x.EffectiveDate })
            .IsUnique()
            .HasDatabaseName("uq_apg_weight_date");

        b.Entity<ApgBaseRate>().HasIndex(x => x.Source);
        b.Entity<ApgBaseRate>().HasIndex(x => x.PeerGroup);
        b.Entity<ApgBaseRate>().HasIndex(x => x.Region);
        b.Entity<ApgBaseRate>().HasIndex(x => x.EffectiveDate);
        b.Entity<ApgBaseRate>()
            .HasIndex(x => new { x.Source, x.PeerGroup, x.Region, x.EffectiveDate })
            .HasDatabaseName("ix_base_rate_lookup");

        b.Entity<ProviderCounty>().HasIndex(x => x.CountyName).IsUnique();

        b.Entity<PxBasedWeight>().HasIndex(x => x.Hcpcs);
        b.Entity<PxBasedWeight>().HasIndex(x => x.EffectiveDate);
        b.Entity<PxBasedWeight>()
            .HasIndex(x => new { x.Hcpcs, x.EffectiveDate })
            .HasDatabaseName("ix_px_weight_lookup");

        b.Entity<FeeScheduleItem>().HasIndex(x => x.Hcpcs);
        b.Entity<FeeScheduleItem>().HasIndex(x => x.EffectiveDate);
        b.Entity<FeeScheduleItem>()
            .HasIndex(x => new { x.Hcpcs, x.EffectiveDate })
            .HasDatabaseName("ix_fee_schedule_lookup");

        b.Entity<ProviderConfig>().HasIndex(x => x.IsActive);

        // Operational tables — claims, lines, adjustments, APG results
        b.Entity<ParsedClaim>().HasIndex(x => x.FileId);
        b.Entity<ParsedClaim>().HasIndex(x => x.FileType);
        b.Entity<ParsedClaim>().HasIndex(x => x.ClaimId);
        b.Entity<ParsedClaim>().HasIndex(x => x.ProviderNpi);
        b.Entity<ParsedClaim>().HasIndex(x => x.DateOfService);

        b.Entity<ParsedClaim>()
            .HasMany(x => x.ServiceLines)
            .WithOne(x => x.Claim)
            .HasForeignKey(x => x.ClaimIdFk)
            .OnDelete(DeleteBehavior.Cascade);
        b.Entity<ParsedClaim>()
            .HasMany(x => x.Adjustments)
            .WithOne(x => x.Claim)
            .HasForeignKey(x => x.ClaimIdFk)
            .OnDelete(DeleteBehavior.Cascade);
        b.Entity<ParsedClaim>()
            .HasOne(x => x.ApgResult)
            .WithOne(x => x.Claim)
            .HasForeignKey<ApgResultRecord>(x => x.ClaimIdFk)
            .OnDelete(DeleteBehavior.Cascade);
        // Self-referencing FK for 835↔837 linking (Phase 4 Session B). Use
        // NoAction instead of SetNull because SQL Server refuses to create
        // a cascading FK on a table that already has cascading FKs from
        // child tables (parsed_service_line, claim_adjustment, apg_result)
        // — it sees that as a potential cycle even though SetNull would
        // be safe in practice. NoAction means: prevent the delete if any
        // claim still links to this one. Application code will unlink
        // before deleting (Phase 4 Session B will add that).
        b.Entity<ParsedClaim>()
            .HasOne(x => x.LinkedClaim)
            .WithMany()
            .HasForeignKey(x => x.LinkedClaimIdFk)
            .OnDelete(DeleteBehavior.NoAction);

        b.Entity<ParsedServiceLine>().HasIndex(x => x.ClaimIdFk);
        b.Entity<ParsedServiceLine>().HasIndex(x => x.ProcedureCode);
        b.Entity<ClaimAdjustment>().HasIndex(x => x.ClaimIdFk);
    }
}
