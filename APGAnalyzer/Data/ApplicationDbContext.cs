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
    }
}
