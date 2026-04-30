/* ============================================================================
   APG Rate Analyzer — SQL Server schema
   ============================================================================
   Target:   Azure SQL Database (or SQL Server 2016+ on-prem)
   Source:   Generated from EF Core 10 entity definitions in
             APGAnalyzer/Models/Domain/*.cs and APGAnalyzer/Data/
             ApplicationDbContext.cs
   Encoding: UTF-8, T-SQL, NVARCHAR for all strings (Unicode-safe)

   Tables (in dependency order):

       Auth & Identity (ASP.NET Core Identity defaults)
       ------------------------------------------------
       __EFMigrationsHistory     EF Core migration tracking
       AspNetRoles               role definitions ("admin", "analyst")
       AspNetUsers               user accounts (Argon2id-equivalent hash)
       AspNetUserClaims          claims attached to users
       AspNetUserRoles           many-to-many user ↔ role
       AspNetUserLogins          external-provider logins (unused)
       AspNetUserTokens          email/2FA token storage (unused)
       AspNetRoleClaims          claims attached to roles

       Reference data (loaded via Settings page uploaders)
       ----------------------------------------------------
       hcpcs_to_eapg             ~21k rows — eMedNY APG Crosswalk HCPCS sheet
       icd10_to_eapg             ~75k rows — eMedNY APG Crosswalk ICD-10 sheet
       apg_weights               ~21k rows — NYS DOH historical weights (long-form)
       apg_base_rates              ~180 rows — DTC + Hospital base rates
       provider_county                62 rows — NY county → Upstate/Downstate
       px_based_weights           ~5.3k rows — procedure-specific weight overrides
       fee_schedule               ~2.1k rows — flat-rate procedures

       Operational (claim processing)
       ------------------------------
       provider_config           current + historical provider settings
       parsed_claim              one row per CLP/CLM segment
       parsed_service_line       child of parsed_claim — SVC/SV1/SV2 lines
       claim_adjustment          child of parsed_claim — CAS rows
       apg_result                child of parsed_claim — cached engine output

   Money columns:  DECIMAL(14, 2) for dollars; DECIMAL(12, 4) for rates;
                   DECIMAL(12, 6) for APG weights. Never FLOAT/REAL.
   Date columns:   DATE (no time component) for claim DOS, effective dates.
                   DATETIME2 for audit timestamps (sub-second precision).
   String columns: NVARCHAR (Unicode) at the lengths declared on entities.
                   Enables future internationalization without migration.

   Cascade behavior:
       parsed_claim → its three child tables (lines, adjustments, apg_result)
                      → ON DELETE CASCADE (deleting a claim drops its graph)
       parsed_claim → parsed_claim (self-FK for 837↔835 linking)
                      → NO ACTION (SQL Server rejects multi-cascade-paths
                        on the same table; app code unlinks before delete)

   ============================================================================ */

USE [APGAnalyzer]
GO


/* ============================================================================
   1. EF Core migration tracking
   ============================================================================ */
CREATE TABLE [dbo].[__EFMigrationsHistory] (
    [MigrationId]    NVARCHAR(150)  NOT NULL,
    [ProductVersion] NVARCHAR(32)   NOT NULL,
    CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
);
GO


/* ============================================================================
   2. ASP.NET Core Identity tables
   ============================================================================
   These match what AddDefaultIdentity<IdentityUser>().AddRoles<IdentityRole>()
   generates. Standard identity layout with NVARCHAR(450) keys (max-key-size
   constraint for indexes).
   ============================================================================ */

CREATE TABLE [dbo].[AspNetRoles] (
    [Id]               NVARCHAR(450) NOT NULL,
    [Name]             NVARCHAR(256) NULL,
    [NormalizedName]   NVARCHAR(256) NULL,
    [ConcurrencyStamp] NVARCHAR(MAX) NULL,
    CONSTRAINT [PK_AspNetRoles] PRIMARY KEY ([Id])
);
CREATE UNIQUE INDEX [RoleNameIndex]
    ON [dbo].[AspNetRoles] ([NormalizedName])
    WHERE [NormalizedName] IS NOT NULL;
GO

CREATE TABLE [dbo].[AspNetUsers] (
    [Id]                   NVARCHAR(450)  NOT NULL,
    [UserName]             NVARCHAR(256)  NULL,
    [NormalizedUserName]   NVARCHAR(256)  NULL,
    [Email]                NVARCHAR(256)  NULL,
    [NormalizedEmail]      NVARCHAR(256)  NULL,
    [EmailConfirmed]       BIT            NOT NULL,
    [PasswordHash]         NVARCHAR(MAX)  NULL,
    [SecurityStamp]        NVARCHAR(MAX)  NULL,
    [ConcurrencyStamp]     NVARCHAR(MAX)  NULL,
    [PhoneNumber]          NVARCHAR(MAX)  NULL,
    [PhoneNumberConfirmed] BIT            NOT NULL,
    [TwoFactorEnabled]     BIT            NOT NULL,
    [LockoutEnd]           DATETIMEOFFSET NULL,
    [LockoutEnabled]       BIT            NOT NULL,
    [AccessFailedCount]    INT            NOT NULL,
    CONSTRAINT [PK_AspNetUsers] PRIMARY KEY ([Id])
);
CREATE UNIQUE INDEX [UserNameIndex]
    ON [dbo].[AspNetUsers] ([NormalizedUserName])
    WHERE [NormalizedUserName] IS NOT NULL;
CREATE INDEX [EmailIndex]
    ON [dbo].[AspNetUsers] ([NormalizedEmail]);
GO

CREATE TABLE [dbo].[AspNetRoleClaims] (
    [Id]         INT             IDENTITY(1,1) NOT NULL,
    [RoleId]     NVARCHAR(450)   NOT NULL,
    [ClaimType]  NVARCHAR(MAX)   NULL,
    [ClaimValue] NVARCHAR(MAX)   NULL,
    CONSTRAINT [PK_AspNetRoleClaims] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_AspNetRoleClaims_AspNetRoles_RoleId]
        FOREIGN KEY ([RoleId]) REFERENCES [dbo].[AspNetRoles] ([Id]) ON DELETE CASCADE
);
CREATE INDEX [IX_AspNetRoleClaims_RoleId] ON [dbo].[AspNetRoleClaims] ([RoleId]);
GO

CREATE TABLE [dbo].[AspNetUserClaims] (
    [Id]         INT             IDENTITY(1,1) NOT NULL,
    [UserId]     NVARCHAR(450)   NOT NULL,
    [ClaimType]  NVARCHAR(MAX)   NULL,
    [ClaimValue] NVARCHAR(MAX)   NULL,
    CONSTRAINT [PK_AspNetUserClaims] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_AspNetUserClaims_AspNetUsers_UserId]
        FOREIGN KEY ([UserId]) REFERENCES [dbo].[AspNetUsers] ([Id]) ON DELETE CASCADE
);
CREATE INDEX [IX_AspNetUserClaims_UserId] ON [dbo].[AspNetUserClaims] ([UserId]);
GO

CREATE TABLE [dbo].[AspNetUserLogins] (
    [LoginProvider]       NVARCHAR(450) NOT NULL,
    [ProviderKey]         NVARCHAR(450) NOT NULL,
    [ProviderDisplayName] NVARCHAR(MAX) NULL,
    [UserId]              NVARCHAR(450) NOT NULL,
    CONSTRAINT [PK_AspNetUserLogins] PRIMARY KEY ([LoginProvider], [ProviderKey]),
    CONSTRAINT [FK_AspNetUserLogins_AspNetUsers_UserId]
        FOREIGN KEY ([UserId]) REFERENCES [dbo].[AspNetUsers] ([Id]) ON DELETE CASCADE
);
CREATE INDEX [IX_AspNetUserLogins_UserId] ON [dbo].[AspNetUserLogins] ([UserId]);
GO

CREATE TABLE [dbo].[AspNetUserRoles] (
    [UserId] NVARCHAR(450) NOT NULL,
    [RoleId] NVARCHAR(450) NOT NULL,
    CONSTRAINT [PK_AspNetUserRoles] PRIMARY KEY ([UserId], [RoleId]),
    CONSTRAINT [FK_AspNetUserRoles_AspNetRoles_RoleId]
        FOREIGN KEY ([RoleId]) REFERENCES [dbo].[AspNetRoles] ([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_AspNetUserRoles_AspNetUsers_UserId]
        FOREIGN KEY ([UserId]) REFERENCES [dbo].[AspNetUsers] ([Id]) ON DELETE CASCADE
);
CREATE INDEX [IX_AspNetUserRoles_RoleId] ON [dbo].[AspNetUserRoles] ([RoleId]);
GO

CREATE TABLE [dbo].[AspNetUserTokens] (
    [UserId]        NVARCHAR(450) NOT NULL,
    [LoginProvider] NVARCHAR(450) NOT NULL,
    [Name]          NVARCHAR(450) NOT NULL,
    [Value]         NVARCHAR(MAX) NULL,
    CONSTRAINT [PK_AspNetUserTokens] PRIMARY KEY ([UserId], [LoginProvider], [Name]),
    CONSTRAINT [FK_AspNetUserTokens_AspNetUsers_UserId]
        FOREIGN KEY ([UserId]) REFERENCES [dbo].[AspNetUsers] ([Id]) ON DELETE CASCADE
);
GO


/* ============================================================================
   3. Reference data — APG / EAPG crosswalks and rate tables
   ============================================================================ */

/* hcpcs_to_eapg
   -----------------------------------------------------------------
   eMedNY APG Crosswalk's "HCPCS to EAPGs" sheet. ~21k rows.
   Quarter-effective dates allow date-scoped lookup at claim DOS time. */
CREATE TABLE [dbo].[hcpcs_to_eapg] (
    [Id]                         INT             IDENTITY(1,1) NOT NULL,
    [Hcpcs]                      NVARCHAR(12)    NOT NULL,    -- CPT/HCPCS code, uppercased
    [Description]                NVARCHAR(512)   NULL,
    [Eapg]                       INT             NOT NULL,
    [EapgDesc]                   NVARCHAR(512)   NULL,
    [EapgType]                   NVARCHAR(64)    NULL,        -- v3.18 string ("Significant Procedure", etc.)
    [EapgCategory]               NVARCHAR(256)   NULL,
    [EapgServiceLine]            NVARCHAR(32)    NULL,
    [QuarterEffectiveDate]       DATE            NULL,
    [QuarterEndDate]             DATE            NULL,
    [MidQuarterEffectiveDate]    DATE            NULL,
    [MidQuarterEndDate]          DATE            NULL,
    CONSTRAINT [PK_hcpcs_to_eapg] PRIMARY KEY ([Id])
);
CREATE INDEX [IX_hcpcs_to_eapg_Hcpcs]                ON [dbo].[hcpcs_to_eapg] ([Hcpcs]);
CREATE INDEX [IX_hcpcs_to_eapg_Eapg]                 ON [dbo].[hcpcs_to_eapg] ([Eapg]);
CREATE INDEX [IX_hcpcs_to_eapg_EapgType]             ON [dbo].[hcpcs_to_eapg] ([EapgType]);
CREATE INDEX [IX_hcpcs_to_eapg_QuarterEffectiveDate] ON [dbo].[hcpcs_to_eapg] ([QuarterEffectiveDate]);
CREATE INDEX [ix_hcpcs_eapg_code_date]               ON [dbo].[hcpcs_to_eapg] ([Hcpcs], [QuarterEffectiveDate]);
GO

/* icd10_to_eapg
   -----------------------------------------------------------------
   eMedNY APG Crosswalk's "ICD-10 DX to EAPGs" sheet. ~75k rows.
   DxCode is canonicalized: uppercase, dots stripped (Solventum's storage).
   Engine normalizes user input the same way at lookup time. */
CREATE TABLE [dbo].[icd10_to_eapg] (
    [Id]               INT             IDENTITY(1,1) NOT NULL,
    [DxCode]           NVARCHAR(12)    NOT NULL,    -- normalized form: I100, A000, etc.
    [Description]      NVARCHAR(512)   NULL,
    [Gender]           NVARCHAR(4)     NULL,        -- '0' (any), 'M', 'F'
    [Eapg]             INT             NOT NULL,
    [EapgDesc]         NVARCHAR(512)   NULL,
    [EapgType]         NVARCHAR(64)    NULL,
    [EapgCategory]     NVARCHAR(256)   NULL,
    [EapgServiceLine]  NVARCHAR(32)    NULL,
    [EffectiveDate]    DATE            NULL,
    [EndDate]          DATE            NULL,
    CONSTRAINT [PK_icd10_to_eapg] PRIMARY KEY ([Id])
);
CREATE INDEX [IX_icd10_to_eapg_DxCode]        ON [dbo].[icd10_to_eapg] ([DxCode]);
CREATE INDEX [IX_icd10_to_eapg_Eapg]          ON [dbo].[icd10_to_eapg] ([Eapg]);
CREATE INDEX [IX_icd10_to_eapg_EapgType]      ON [dbo].[icd10_to_eapg] ([EapgType]);
CREATE INDEX [IX_icd10_to_eapg_EffectiveDate] ON [dbo].[icd10_to_eapg] ([EffectiveDate]);
CREATE INDEX [ix_icd10_eapg_code_date]        ON [dbo].[icd10_to_eapg] ([DxCode], [EffectiveDate]);
GO

/* apg_weights
   -----------------------------------------------------------------
   APG relative weight by effective date, long-form. ~21k rows.
   "Final rate" rows use sentinel EffectiveDate = 9999-12-31 with
   IsFinalRate=true and YearRate set to the year they cover. */
CREATE TABLE [dbo].[apg_weights] (
    [Id]              INT             IDENTITY(1,1) NOT NULL,
    [Apg]             INT             NOT NULL,
    [ApgDescription]  NVARCHAR(512)   NULL,
    [EffectiveDate]   DATE            NOT NULL,
    [Weight]          DECIMAL(12, 6)  NOT NULL,
    [IsFinalRate]     BIT             NOT NULL DEFAULT (0),
    [YearRate]        INT             NULL,
    CONSTRAINT [PK_apg_weights] PRIMARY KEY ([Id])
);
CREATE INDEX [IX_apg_weights_Apg]           ON [dbo].[apg_weights] ([Apg]);
CREATE INDEX [IX_apg_weights_EffectiveDate] ON [dbo].[apg_weights] ([EffectiveDate]);
CREATE UNIQUE INDEX [uq_apg_weight_date]    ON [dbo].[apg_weights] ([Apg], [EffectiveDate]);
GO

/* apg_base_rates
   -----------------------------------------------------------------
   Base rate (dollars per APG-weight unit) by source/peer/region/effdate.
   ~180 rows. Engine selects exact peer_group + most-recent eff_date ≤ DOS. */
CREATE TABLE [dbo].[apg_base_rates] (
    [Id]               INT            IDENTITY(1,1) NOT NULL,
    [Source]           NVARCHAR(16)   NOT NULL,    -- 'dtc' | 'hospital'
    [PeerGroup]        NVARCHAR(64)   NOT NULL,    -- 'Clinic*', 'Amb Surg', etc.
    [CureCode]         NVARCHAR(32)   NULL,
    [BaseRateCode]     NVARCHAR(32)   NULL,
    [BlendRateCode]    NVARCHAR(32)   NULL,
    [CapitalRateCode]  NVARCHAR(32)   NULL,
    [Region]           NVARCHAR(16)   NOT NULL,    -- 'Upstate' | 'Downstate'
    [CheatFlag]        NVARCHAR(32)   NULL,        -- hospital-only metadata
    [EffectiveDate]    DATE           NOT NULL,
    [Rate]             DECIMAL(12, 4) NOT NULL,    -- dollars
    CONSTRAINT [PK_apg_base_rates] PRIMARY KEY ([Id])
);
CREATE INDEX [IX_apg_base_rates_Source]        ON [dbo].[apg_base_rates] ([Source]);
CREATE INDEX [IX_apg_base_rates_PeerGroup]     ON [dbo].[apg_base_rates] ([PeerGroup]);
CREATE INDEX [IX_apg_base_rates_Region]        ON [dbo].[apg_base_rates] ([Region]);
CREATE INDEX [IX_apg_base_rates_EffectiveDate] ON [dbo].[apg_base_rates] ([EffectiveDate]);
CREATE INDEX [ix_base_rate_lookup]
    ON [dbo].[apg_base_rates] ([Source], [PeerGroup], [Region], [EffectiveDate]);
GO

/* provider_county
   -----------------------------------------------------------------
   NY county → Upstate/Downstate mapping. 62 rows.
   CountyCode is the natural key — uses NYS DOH's published numeric code
   (e.g. 60 = MANHATTAN, 58 = BRONX). NOT auto-generated. */
CREATE TABLE [dbo].[provider_county] (
    [CountyCode]       INT           NOT NULL,    -- natural key, NOT IDENTITY
    [CountyName]       NVARCHAR(64)  NOT NULL,
    [HealthHomePhase]  NVARCHAR(32)  NULL,
    [Region]           NVARCHAR(16)  NOT NULL,    -- 'Upstate' | 'Downstate'
    CONSTRAINT [PK_provider_county] PRIMARY KEY ([CountyCode])
);
CREATE UNIQUE INDEX [IX_provider_county_CountyName] ON [dbo].[provider_county] ([CountyName]);
GO

/* px_based_weights
   -----------------------------------------------------------------
   HCPCS-specific weight OVERRIDE. ~5,300 rows.
   Priority #2 in the engine's pricing ladder — overrides apg_weights
   when present + non-zero for the DOS. */
CREATE TABLE [dbo].[px_based_weights] (
    [Id]             INT            IDENTITY(1,1) NOT NULL,
    [Hcpcs]          NVARCHAR(12)   NOT NULL,
    [Description]    NVARCHAR(256)  NULL,
    [EffectiveDate]  DATE           NOT NULL,
    [Weight]         DECIMAL(12, 6) NOT NULL,
    [UnitsLimit]     DECIMAL(10, 2) NULL,
    CONSTRAINT [PK_px_based_weights] PRIMARY KEY ([Id])
);
CREATE INDEX [IX_px_based_weights_Hcpcs]         ON [dbo].[px_based_weights] ([Hcpcs]);
CREATE INDEX [IX_px_based_weights_EffectiveDate] ON [dbo].[px_based_weights] ([EffectiveDate]);
CREATE INDEX [ix_px_weight_lookup]               ON [dbo].[px_based_weights] ([Hcpcs], [EffectiveDate]);
GO

/* fee_schedule
   -----------------------------------------------------------------
   Flat-rate fee for specific HCPCS codes. ~2,100 rows.
   Priority #1 in the engine's pricing ladder — bypasses APG formula. */
CREATE TABLE [dbo].[fee_schedule] (
    [Id]             INT            IDENTITY(1,1) NOT NULL,
    [Hcpcs]          NVARCHAR(12)   NOT NULL,
    [Description]    NVARCHAR(256)  NULL,
    [EffectiveDate]  DATE           NOT NULL,
    [Reimbursement]  DECIMAL(12, 2) NOT NULL,    -- dollars per unit
    [MaxUnits]       DECIMAL(10, 2) NULL,        -- caps billed units
    CONSTRAINT [PK_fee_schedule] PRIMARY KEY ([Id])
);
CREATE INDEX [IX_fee_schedule_Hcpcs]         ON [dbo].[fee_schedule] ([Hcpcs]);
CREATE INDEX [IX_fee_schedule_EffectiveDate] ON [dbo].[fee_schedule] ([EffectiveDate]);
CREATE INDEX [ix_fee_schedule_lookup]        ON [dbo].[fee_schedule] ([Hcpcs], [EffectiveDate]);
GO


/* ============================================================================
   4. Operational tables (claim processing)
   ============================================================================ */

/* provider_config
   -----------------------------------------------------------------
   Active + historical provider configuration. At most one row should
   have IsActive=1 at a time. The Save UI deactivates the old row and
   inserts a new one rather than updating-in-place — preserves history. */
CREATE TABLE [dbo].[provider_config] (
    [Id]                     INT             IDENTITY(1,1) NOT NULL,
    [IsActive]               BIT             NOT NULL DEFAULT (1),
    [ProviderName]           NVARCHAR(128)   NOT NULL,
    [Npi]                    NVARCHAR(16)    NULL,
    [CountyCode]             INT             NULL,        -- references provider_county.CountyCode (no FK enforced)
    [Region]                 NVARCHAR(16)    NULL,        -- 'Upstate' | 'Downstate', auto-derived from county
    [PeerGroup]              NVARCHAR(64)    NOT NULL,
    [ProviderType]           NVARCHAR(16)    NOT NULL,    -- 'dtc' | 'hospital'
    [CapitalAddonEligible]   BIT             NOT NULL DEFAULT (0),
    [CapitalAddonRate]       DECIMAL(12, 4)  NULL,
    [RateCodeOverride]       NVARCHAR(16)    NULL,
    [CmsLocality]            NVARCHAR(16)    NULL,
    [UpdatedAt]              DATETIME2       NOT NULL DEFAULT (GETUTCDATE()),
    CONSTRAINT [PK_provider_config] PRIMARY KEY ([Id])
);
CREATE INDEX [IX_provider_config_IsActive] ON [dbo].[provider_config] ([IsActive]);
GO

/* parsed_claim
   -----------------------------------------------------------------
   One row per CLP segment (835) or CLM segment (837). The parent
   table for the entire claim graph.

   Self-FK LinkedClaimIdFk pairs an 835 with its 837 sibling (set
   by the auto-linker after upload). NoAction cascade — SQL Server
   refuses cascading self-FKs when other CASCADE FKs exist on the
   same table. App code unlinks before delete. */
CREATE TABLE [dbo].[parsed_claim] (
    [Id]                      INT            IDENTITY(1,1) NOT NULL,
    [FileId]                  NVARCHAR(64)   NOT NULL,    -- upload batch identifier
    [FileType]                NVARCHAR(8)    NOT NULL,    -- '835I' | '835P' | '837I' | '837P'
    [PayerName]               NVARCHAR(128)  NULL,
    [PayerId]                 NVARCHAR(32)   NULL,
    [ProviderNpi]             NVARCHAR(16)   NULL,
    [ProviderName]            NVARCHAR(128)  NULL,
    [ClaimId]                 NVARCHAR(64)   NOT NULL,    -- CLP01 / CLM01
    [PatientName]             NVARCHAR(128)  NULL,
    [PatientId]               NVARCHAR(64)   NULL,
    [DateOfService]           DATE           NULL,
    [ClaimStatus]             NVARCHAR(8)    NULL,        -- CLP02
    [BilledAmount]            DECIMAL(14, 2) NOT NULL DEFAULT (0),
    [AllowedAmount]           DECIMAL(14, 2) NOT NULL DEFAULT (0),
    [PaidAmount]              DECIMAL(14, 2) NOT NULL DEFAULT (0),
    [PatientResponsibility]   DECIMAL(14, 2) NOT NULL DEFAULT (0),
    [ClaimFilingIndicator]    NVARCHAR(4)    NULL,        -- CLP09
    [PrincipalDiagnosis]      NVARCHAR(16)   NULL,        -- normalized: uppercase, no dots
    [OtherDiagnosesJson]      NVARCHAR(MAX)  NULL,        -- JSON list[str]
    [LinkedClaimIdFk]         INT            NULL,        -- self-FK; sibling 837/835
    [CreatedAt]               DATETIME2      NOT NULL DEFAULT (GETUTCDATE()),
    CONSTRAINT [PK_parsed_claim] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_parsed_claim_parsed_claim_LinkedClaimIdFk]
        FOREIGN KEY ([LinkedClaimIdFk]) REFERENCES [dbo].[parsed_claim] ([Id])
        ON DELETE NO ACTION
);
CREATE INDEX [IX_parsed_claim_FileId]           ON [dbo].[parsed_claim] ([FileId]);
CREATE INDEX [IX_parsed_claim_FileType]         ON [dbo].[parsed_claim] ([FileType]);
CREATE INDEX [IX_parsed_claim_ClaimId]          ON [dbo].[parsed_claim] ([ClaimId]);
CREATE INDEX [IX_parsed_claim_ProviderNpi]      ON [dbo].[parsed_claim] ([ProviderNpi]);
CREATE INDEX [IX_parsed_claim_DateOfService]    ON [dbo].[parsed_claim] ([DateOfService]);
CREATE INDEX [IX_parsed_claim_LinkedClaimIdFk]  ON [dbo].[parsed_claim] ([LinkedClaimIdFk]);
GO

/* parsed_service_line
   -----------------------------------------------------------------
   SVC (835) or SV1/SV2 (837) line. CASCADE delete with parent claim. */
CREATE TABLE [dbo].[parsed_service_line] (
    [Id]               INT            IDENTITY(1,1) NOT NULL,
    [ClaimIdFk]        INT            NOT NULL,
    [LineSeq]          INT            NOT NULL,
    [ProcedureCode]    NVARCHAR(12)   NOT NULL,
    [ModifiersJson]    NVARCHAR(MAX)  NULL,        -- JSON list[str], e.g. ["U6"]
    [RevenueCode]      NVARCHAR(8)    NULL,
    [BilledAmount]     DECIMAL(14, 2) NOT NULL DEFAULT (0),
    [AllowedAmount]    DECIMAL(14, 2) NOT NULL DEFAULT (0),
    [PaidAmount]       DECIMAL(14, 2) NOT NULL DEFAULT (0),
    [Units]            INT            NOT NULL DEFAULT (1),
    [DateOfService]    DATE           NULL,
    CONSTRAINT [PK_parsed_service_line] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_parsed_service_line_parsed_claim_ClaimIdFk]
        FOREIGN KEY ([ClaimIdFk]) REFERENCES [dbo].[parsed_claim] ([Id])
        ON DELETE CASCADE
);
CREATE INDEX [IX_parsed_service_line_ClaimIdFk]     ON [dbo].[parsed_service_line] ([ClaimIdFk]);
CREATE INDEX [IX_parsed_service_line_ProcedureCode] ON [dbo].[parsed_service_line] ([ProcedureCode]);
GO

/* claim_adjustment
   -----------------------------------------------------------------
   CAS rows. LineSeq null = claim-level adjustment; non-null = line-level.
   GroupCode: CO=Contractual, PR=Patient Resp, OA=Other, PI=Payer Initiated, CR=Correction. */
CREATE TABLE [dbo].[claim_adjustment] (
    [Id]          INT            IDENTITY(1,1) NOT NULL,
    [ClaimIdFk]   INT            NOT NULL,
    [LineSeq]     INT            NULL,
    [GroupCode]   NVARCHAR(4)    NOT NULL,    -- CO | PR | OA | PI | CR
    [ReasonCode]  NVARCHAR(8)    NOT NULL,    -- CARC code
    [Amount]      DECIMAL(14, 2) NOT NULL,
    [Quantity]    INT            NULL,
    CONSTRAINT [PK_claim_adjustment] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_claim_adjustment_parsed_claim_ClaimIdFk]
        FOREIGN KEY ([ClaimIdFk]) REFERENCES [dbo].[parsed_claim] ([Id])
        ON DELETE CASCADE
);
CREATE INDEX [IX_claim_adjustment_ClaimIdFk] ON [dbo].[claim_adjustment] ([ClaimIdFk]);
GO

/* apg_result
   -----------------------------------------------------------------
   Cached APG calculation per claim. 1:1 with parsed_claim — PK is the FK.
   Refreshed when the linker fires a re-calc after dx enrichment. */
CREATE TABLE [dbo].[apg_result] (
    [ClaimIdFk]            INT            NOT NULL,
    [CorrectApgPayment]    DECIMAL(14, 2) NOT NULL,
    [ActualPaid]           DECIMAL(14, 2) NOT NULL,
    [Variance]             DECIMAL(14, 2) NOT NULL,
    [CompressionPct]       DECIMAL(10, 4) NOT NULL,
    [Underpaid]            BIT            NOT NULL DEFAULT (0),
    [Overpaid]             BIT            NOT NULL DEFAULT (0),
    [BaseRateApplied]      DECIMAL(12, 4) NOT NULL,
    [PeerGroup]            NVARCHAR(64)   NOT NULL,
    [Region]               NVARCHAR(16)   NOT NULL,
    [DiscountingApplied]   BIT            NOT NULL DEFAULT (0),
    [U6Applied]            BIT            NOT NULL DEFAULT (0),
    [CapitalApplied]       BIT            NOT NULL DEFAULT (0),
    [LineDetailsJson]      NVARCHAR(MAX)  NOT NULL DEFAULT ('[]'),  -- serialized APGLineResult[]
    [CalculatedAt]         DATETIME2      NOT NULL DEFAULT (GETUTCDATE()),
    CONSTRAINT [PK_apg_result] PRIMARY KEY ([ClaimIdFk]),
    CONSTRAINT [FK_apg_result_parsed_claim_ClaimIdFk]
        FOREIGN KEY ([ClaimIdFk]) REFERENCES [dbo].[parsed_claim] ([Id])
        ON DELETE CASCADE
);
GO


/* ============================================================================
   5. End of schema
   ============================================================================
   Notes for production deployment on Azure SQL:

   1. Database creation:
      CREATE DATABASE [APGAnalyzer]
        COLLATE SQL_Latin1_General_CP1_CI_AS;

      Or whatever the Azure portal default is — Identity NVARCHAR
      indexes work fine across collations.

   2. Service tier sizing for beta:
      - Basic / S0 (5 DTU) handles a single-tenant beta with 100s of
        claims comfortably.
      - Scale to S2 (50 DTU) once you hit ~10k claims or > 5 concurrent
        users. EF Core's lookups are well-indexed enough that even S0
        produces sub-second engine round-trips.

   3. Backup: Azure SQL has automatic point-in-time restore for 7 days
      on Basic tier, 35 days on Standard+. No additional setup.

   4. Migrations: managed by EF Core via __EFMigrationsHistory.
      For Azure deployment, run `dotnet ef database update --project APGAnalyzer`
      against the Azure SQL connection string. New migrations apply
      automatically on subsequent deploys via the same command in CI/CD.

   5. Encryption: Azure SQL enables TDE (Transparent Data Encryption)
      by default. No application changes required.

   6. Always Encrypted: NOT enabled. If columns containing patient
      identifiers (PatientName, PatientId, PrincipalDiagnosis) need
      additional protection, that's a future enhancement post-launch.

   ============================================================================ */
