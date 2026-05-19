// ============================================================================
// APG Rate Analyzer — Technical Documentation builder
// ============================================================================
// Generates a single comprehensive .docx covering architecture, build, and
// Azure deployment. Run with:  node build_doc.js
// ============================================================================
const fs = require('fs');
const path = require('path');
const {
  Document, Packer, Paragraph, TextRun, Table, TableRow, TableCell,
  Header, Footer, AlignmentType, PageOrientation, LevelFormat,
  ExternalHyperlink, TabStopType, TabStopPosition,
  TableOfContents, HeadingLevel, BorderStyle, WidthType, ShadingType,
  VerticalAlign, PageNumber, PageBreak,
} = require('docx');

// ============================================================================
// Helpers — keep document construction readable
// ============================================================================

const BORDER = { style: BorderStyle.SINGLE, size: 1, color: 'CCCCCC' };
const BORDERS_ALL = { top: BORDER, bottom: BORDER, left: BORDER, right: BORDER };

const PMTAC_PURPLE = '6E50CD';
const HEADER_GREY = 'F4F3F8';
const CODE_GREY = 'F4F4F6';
const ACCENT_GREEN = '22A06B';
const ACCENT_RED = 'E5484D';

// 1 inch = 1440 DXA. Letter portrait: 12240 x 15840. Content area = 9360.
const CONTENT_WIDTH = 9360;

// ----- Paragraph helpers --------------------------------------------------

const p = (text, opts = {}) =>
  new Paragraph({
    children: Array.isArray(text)
      ? text
      : [new TextRun({ text, ...(opts.run || {}) })],
    spacing: opts.spacing || { after: 120 },
    ...opts.para,
  });

const h1 = (text) =>
  new Paragraph({
    heading: HeadingLevel.HEADING_1,
    children: [new TextRun({ text })],
    spacing: { before: 360, after: 180 },
    pageBreakBefore: true,
  });

const h1NoBreak = (text) =>
  new Paragraph({
    heading: HeadingLevel.HEADING_1,
    children: [new TextRun({ text })],
    spacing: { before: 360, after: 180 },
  });

const h2 = (text) =>
  new Paragraph({
    heading: HeadingLevel.HEADING_2,
    children: [new TextRun({ text })],
    spacing: { before: 280, after: 140 },
  });

const h3 = (text) =>
  new Paragraph({
    heading: HeadingLevel.HEADING_3,
    children: [new TextRun({ text })],
    spacing: { before: 200, after: 100 },
  });

const bullet = (text, level = 0) =>
  new Paragraph({
    numbering: { reference: 'bullets', level },
    children: parseInline(text),
    spacing: { after: 80 },
  });

const numbered = (text, level = 0) =>
  new Paragraph({
    numbering: { reference: 'numbers', level },
    children: parseInline(text),
    spacing: { after: 80 },
  });

// Parse inline markdown-ish: **bold** and `code`. Returns TextRun[].
function parseInline(text) {
  const runs = [];
  // Match either **bold** or `code` or plain text
  const regex = /(\*\*[^*]+\*\*|`[^`]+`)/g;
  let lastIndex = 0;
  let match;
  while ((match = regex.exec(text)) !== null) {
    if (match.index > lastIndex) {
      runs.push(new TextRun({ text: text.substring(lastIndex, match.index) }));
    }
    const token = match[0];
    if (token.startsWith('**')) {
      runs.push(new TextRun({ text: token.slice(2, -2), bold: true }));
    } else if (token.startsWith('`')) {
      runs.push(new TextRun({ text: token.slice(1, -1), font: 'Consolas', size: 20 }));
    }
    lastIndex = match.index + token.length;
  }
  if (lastIndex < text.length) {
    runs.push(new TextRun({ text: text.substring(lastIndex) }));
  }
  return runs.length === 0 ? [new TextRun({ text })] : runs;
}

// ----- Code block (monospace, grey background) ----------------------------

function code(text, opts = {}) {
  const lang = opts.lang || '';
  const lines = text.split('\n');
  const rows = lines.map(
    (line) =>
      new TableRow({
        children: [
          new TableCell({
            borders: { top: { style: BorderStyle.NONE }, bottom: { style: BorderStyle.NONE }, left: { style: BorderStyle.NONE }, right: { style: BorderStyle.NONE } },
            width: { size: CONTENT_WIDTH, type: WidthType.DXA },
            shading: { fill: CODE_GREY, type: ShadingType.CLEAR },
            margins: { top: 40, bottom: 40, left: 160, right: 160 },
            children: [
              new Paragraph({
                children: [
                  new TextRun({
                    text: line || ' ',
                    font: 'Consolas',
                    size: 18,
                  }),
                ],
                spacing: { after: 0, before: 0 },
              }),
            ],
          }),
        ],
      })
  );
  const result = [
    new Table({
      width: { size: CONTENT_WIDTH, type: WidthType.DXA },
      columnWidths: [CONTENT_WIDTH],
      rows,
    }),
    new Paragraph({ children: [new TextRun(' ')], spacing: { after: 120 } }),
  ];
  if (lang) {
    result.unshift(
      new Paragraph({
        children: [new TextRun({ text: lang, italics: true, color: '888888', size: 18 })],
        spacing: { after: 0 },
      })
    );
  }
  return result;
}

// ----- Note / callout boxes -----------------------------------------------

function callout(label, lines, color) {
  const fill = color || HEADER_GREY;
  const borderColor = color === ACCENT_RED ? ACCENT_RED : (color === ACCENT_GREEN ? ACCENT_GREEN : PMTAC_PURPLE);
  const para = [
    new Paragraph({
      children: [new TextRun({ text: label, bold: true, color: borderColor })],
      spacing: { after: 60 },
    }),
    ...lines.map((l) =>
      new Paragraph({
        children: parseInline(l),
        spacing: { after: 40 },
      })
    ),
  ];
  return [
    new Table({
      width: { size: CONTENT_WIDTH, type: WidthType.DXA },
      columnWidths: [CONTENT_WIDTH],
      rows: [
        new TableRow({
          children: [
            new TableCell({
              borders: {
                top: { style: BorderStyle.SINGLE, size: 6, color: borderColor },
                bottom: { style: BorderStyle.SINGLE, size: 6, color: borderColor },
                left: { style: BorderStyle.SINGLE, size: 18, color: borderColor },
                right: { style: BorderStyle.SINGLE, size: 6, color: borderColor },
              },
              width: { size: CONTENT_WIDTH, type: WidthType.DXA },
              shading: { fill: fill, type: ShadingType.CLEAR },
              margins: { top: 160, bottom: 160, left: 240, right: 200 },
              children: para,
            }),
          ],
        }),
      ],
    }),
    new Paragraph({ children: [new TextRun(' ')], spacing: { after: 120 } }),
  ];
}

// ----- Tables --------------------------------------------------------------

function makeTable(headers, rows, colWidths) {
  const widths = colWidths || headers.map(() => Math.floor(CONTENT_WIDTH / headers.length));
  const headerRow = new TableRow({
    tableHeader: true,
    children: headers.map((h, i) =>
      new TableCell({
        borders: BORDERS_ALL,
        width: { size: widths[i], type: WidthType.DXA },
        shading: { fill: PMTAC_PURPLE, type: ShadingType.CLEAR },
        margins: { top: 100, bottom: 100, left: 140, right: 140 },
        children: [
          new Paragraph({
            children: [new TextRun({ text: h, bold: true, color: 'FFFFFF', size: 20 })],
          }),
        ],
      })
    ),
  });
  const dataRows = rows.map(
    (row) =>
      new TableRow({
        children: row.map((cell, i) =>
          new TableCell({
            borders: BORDERS_ALL,
            width: { size: widths[i], type: WidthType.DXA },
            margins: { top: 80, bottom: 80, left: 140, right: 140 },
            children: [
              new Paragraph({
                children: parseInline(String(cell)),
                spacing: { after: 0 },
              }),
            ],
          })
        ),
      })
  );
  return new Table({
    width: { size: CONTENT_WIDTH, type: WidthType.DXA },
    columnWidths: widths,
    rows: [headerRow, ...dataRows],
  });
}

const spacer = () => new Paragraph({ children: [new TextRun(' ')], spacing: { after: 200 } });

// ============================================================================
// Document content
// ============================================================================

const children = [];

// ============================================================================
// COVER PAGE
// ============================================================================
children.push(
  new Paragraph({
    children: [new TextRun(' ')],
    spacing: { before: 2400, after: 600 },
  }),
  new Paragraph({
    alignment: AlignmentType.CENTER,
    children: [new TextRun({ text: 'PMTAC', bold: true, size: 48, color: PMTAC_PURPLE, font: 'Arial' })],
    spacing: { after: 200 },
  }),
  new Paragraph({
    alignment: AlignmentType.CENTER,
    children: [new TextRun({ text: 'APG Rate Analyzer', size: 56, bold: true, font: 'Arial' })],
    spacing: { after: 200 },
  }),
  new Paragraph({
    alignment: AlignmentType.CENTER,
    children: [new TextRun({ text: 'Technical Documentation', size: 36, color: '555555', font: 'Arial' })],
    spacing: { after: 600 },
  }),
  new Paragraph({
    alignment: AlignmentType.CENTER,
    children: [
      new TextRun({
        text: 'Architecture · Build · Azure Trial Deployment',
        italics: true,
        size: 26,
        color: '666666',
      }),
    ],
    spacing: { after: 1200 },
  }),
  new Paragraph({
    alignment: AlignmentType.CENTER,
    children: [
      new TextRun({ text: 'Version: ', bold: true }),
      new TextRun({ text: '1.0  ·  Beta deployment ' }),
    ],
    spacing: { after: 100 },
  }),
  new Paragraph({
    alignment: AlignmentType.CENTER,
    children: [
      new TextRun({ text: 'Date: ', bold: true }),
      new TextRun({ text: 'May 2026' }),
    ],
    spacing: { after: 100 },
  }),
  new Paragraph({
    alignment: AlignmentType.CENTER,
    children: [
      new TextRun({ text: 'Prepared for: ', bold: true }),
      new TextRun({ text: 'PMTAC IT and Engineering' }),
    ],
    spacing: { after: 100 },
  }),
  new Paragraph({
    alignment: AlignmentType.CENTER,
    children: [
      new TextRun({ text: 'Stack: ', bold: true }),
      new TextRun({ text: '.NET 10 · ASP.NET Core MVC · EF Core 10 · Azure SQL · Azure App Service' }),
    ],
    spacing: { after: 1200 },
  }),
  new Paragraph({
    alignment: AlignmentType.CENTER,
    children: [
      new TextRun({
        text: 'ISO 27001:2022 Certified  ·  © 2026 PMTAC',
        size: 18,
        color: '888888',
      }),
    ],
  }),
  new Paragraph({ children: [new PageBreak()] })
);

// ============================================================================
// TABLE OF CONTENTS
// ============================================================================
children.push(
  new Paragraph({
    children: [new TextRun({ text: 'Table of Contents', bold: true, size: 36 })],
    spacing: { after: 240 },
  }),
  new TableOfContents('Table of Contents', {
    hyperlink: true,
    headingStyleRange: '1-3',
  }),
  new Paragraph({ children: [new PageBreak()] })
);

// ============================================================================
// 1. EXECUTIVE SUMMARY
// ============================================================================
children.push(h1NoBreak('1. Executive Summary'));

children.push(
  p(
    'The APG Rate Analyzer is a web application built for PMTAC (a Medicaid Article 28 ' +
      'compliance and analytics consultancy) to inspect, validate, and audit medical claim ' +
      'reimbursements against the New York State Department of Health (NYS DOH) ambulatory ' +
      'patient group (APG) methodology. The application ingests X12 EDI 835 (electronic ' +
      'remittance advice) and 837 (claim submission) files, parses each claim, calculates the ' +
      'expected APG payment using NYS DOH reference data, and reports the variance between ' +
      'what should have been paid and what was actually paid.'
  ),
  p(
    'Beyond APG analytics, the application also computes the equivalent CMS Medicare Physician ' +
      'Fee Schedule (PFS) rate for each procedure, allowing PMTAC analysts to compare what a ' +
      'professional service would pay under Medicare against what was paid under NYS Medicaid.'
  ),
  h2('Project goals'),
  bullet(
    '**Replace** an internal Python prototype with a production-ready .NET application IT can deploy and maintain.'
  ),
  bullet(
    '**Calculate** correct APG payments using the NYS DOH-published reference workbooks (HCPCS crosswalks, weights, base rates, fee schedule).'
  ),
  bullet(
    '**Audit** payer behavior — flag underpaid claims, identify CARC denial patterns, and surface compression by procedure / EAPG / peer group.'
  ),
  bullet(
    '**Compare** payments against CMS Medicare PFS for cross-program benchmarking.'
  ),
  bullet(
    '**Isolate** data per analyst — each user sees only their own uploaded claims; admins have organization-wide visibility.'
  ),
  bullet(
    '**Deploy** to Microsoft Azure for both production (managed by IT) and a free-tier beta environment (managed by the project lead).'
  ),
  h2('What this document covers'),
  bullet(
    'The complete technical architecture of the application — runtime stack, domain model, database schema, security model, and external integrations.'
  ),
  bullet(
    'The build phases and the rationale behind each decision — useful for any developer inheriting the codebase.'
  ),
  bullet(
    'The full Azure trial-deployment playbook — every step from creating the subscription to handing the URL to beta testers.'
  ),
  bullet(
    'Operational notes — common failure modes, recovery procedures, and the trade-offs between Azure free, Basic, and Standard tiers.'
  ),
  bullet('A roadmap of future work and deferred items.')
);

children.push(
  ...callout(
    'Audience',
    [
      'This document is technical but should be readable by domain experts (medical billing analysts) ' +
        'who want to understand *what* the system does. Deep .NET internals are confined to the ' +
        '"Architecture" and "Build phases" sections, which can be skipped on a first pass.',
    ],
    HEADER_GREY
  )
);

// ============================================================================
// 2. ARCHITECTURE OVERVIEW
// ============================================================================
children.push(h1('2. Architecture Overview'));

children.push(
  p(
    'The APG Rate Analyzer is a server-rendered web application following the canonical ' +
      'ASP.NET Core MVC pattern. There is no separate API layer or single-page front-end; ' +
      'each user interaction is a standard HTTP form post or GET request, and the server ' +
      'returns fully-rendered Razor HTML. This deliberately keeps the architecture simple ' +
      'and aligns with PMTAC IT\'s existing skill set.'
  ),
  h2('2.1 Technology stack'),
  makeTable(
    ['Layer', 'Technology', 'Version', 'Why'],
    [
      ['Runtime', '.NET', '10.0 (LTS)', 'Long-term support, mature, native to the Azure stack PMTAC IT manages'],
      ['Web framework', 'ASP.NET Core MVC', '10.0', 'Server-rendered Razor; least JavaScript surface area'],
      ['ORM', 'Entity Framework Core', '10.0', 'Code-first migrations, LINQ-to-SQL, no hand-written ADO.NET'],
      ['Database (dev)', 'SQL Server LocalDB', 'Latest', 'Zero-install local dev; ships with Visual Studio'],
      ['Database (prod/beta)', 'Azure SQL Database', 'Serverless / Basic', 'Managed, free-tier eligible, identical T-SQL dialect'],
      ['Identity', 'ASP.NET Core Identity', '10.0', 'Cookie auth, role-based authorization, password hashing'],
      ['CSS', 'Bootstrap', '5.x', 'Grid, components, dark/light theming hooks'],
      ['Charts', 'Chart.js', '4.4.6 (CDN)', 'Pure browser; no Node tooling on server'],
      ['Excel', 'ClosedXML', '0.x', '.xlsx read/write without Office interop'],
      ['Legacy .xls', 'NPOI', '2.x', 'BIFF reader for older NYS DOH workbooks'],
      ['PDF', 'QuestPDF (Community)', '2024.x', 'Layout-engine PDF generation; UB-04 / CMS-1500 forms'],
      ['Hosting', 'Azure App Service (Windows)', 'F1 free / B1 basic', 'Managed IIS-equivalent, integrated with VS Publish wizard'],
      ['Build tooling', 'Visual Studio 2022 / dotnet CLI', 'Latest', 'Both work; Publish wizard for one-click deploys'],
    ],
    [1500, 2300, 1800, 3760]
  ),
  spacer(),
  h2('2.2 High-level architecture'),
  p('The system has three logical tiers that all run inside the same App Service worker process:'),
  bullet('**Web / UI tier** — Razor views rendered server-side. Bootstrap 5 + custom PMTAC theme. Chart.js loaded from CDN for analytics.'),
  bullet('**Service / engine tier** — APG calculation engine, CMS Medicare engine, EDI parsers, claim linker, exporters. Pure C# with dependency-injected database access.'),
  bullet('**Persistence tier** — SQL Server (LocalDB in dev, Azure SQL in prod) accessed exclusively through EF Core. No stored procedures; all logic lives in C#.'),
  p(
    'External dependencies are limited to a single outbound integration: the CMS PFS DKAN ' +
      'datastore at pfs.data.cms.gov, used live by the CMS rate engine.'
  ),
  h2('2.3 Cross-cutting concerns'),
  bullet('**Authentication** — cookie-based Identity. No JWT, no OAuth, no SSO yet (deferred until IT decides between corporate Entra ID and standalone).'),
  bullet('**Authorization** — role-based ([Authorize(Roles="admin")] etc.). Three roles: admin, analyst, viewer.'),
  bullet('**Data isolation** — every business-relevant entity carries an OwnerUserId. Read queries are filtered through an OwnedBy() extension method based on the current user\'s effective scope.'),
  bullet('**Logging** — Microsoft.Extensions.Logging at Information / Warning / Error. Console output is captured by Azure App Service Log Stream.'),
  bullet('**Configuration** — appsettings.json for non-secret defaults; Azure App Service Configuration for the connection string and any production-only overrides.'),
  bullet('**Caching** — none in V1 except the in-process CMS catalog cache and a 24-hour DB cache for CMS rates. Reference data is loaded into SQL once and queried per-request.')
);

// ============================================================================
// 3. DOMAIN MODEL
// ============================================================================
children.push(h1('3. Domain Model'));

children.push(
  p(
    'The domain centers on a single concept: a **claim** — one CLP segment from an X12 EDI ' +
      'file, representing one patient encounter being submitted to or remitted by a payer. ' +
      'Every other entity hangs off the claim or is reference data used to price it.'
  ),
  h2('3.1 Core entities'),
  makeTable(
    ['Entity', 'Table', 'Cardinality', 'Purpose'],
    [
      ['ParsedClaim', 'parsed_claim', '1', 'Claim header — CLP01, dates, parties, totals, FK to APG result'],
      ['ParsedServiceLine', 'parsed_service_line', 'N per claim', 'SVC / SV1 / SV2 lines — one row per CPT/HCPCS line'],
      ['ClaimAdjustment', 'claim_adjustment', 'N per claim', 'CAS rows — both claim-level and line-level adjustments (CARC codes)'],
      ['ApgResultRecord', 'apg_result', '1 per claim', 'Cached engine output: correct payment, variance, line-by-line math'],
    ],
    [1700, 1800, 1500, 4360]
  ),
  spacer(),
  h2('3.2 Reference data (loaded from NYS DOH / eMedNY workbooks)'),
  makeTable(
    ['Entity', 'Table', 'Approx. rows', 'Source'],
    [
      ['HcpcsToEapg', 'hcpcs_to_eapg', '~21,000', 'eMedNY APG Crosswalk'],
      ['Icd10ToEapg', 'icd10_to_eapg', '~75,000', 'eMedNY APG Crosswalk'],
      ['ApgWeight', 'apg_weights', '~21,000', 'NYS DOH weights history'],
      ['ApgBaseRate', 'apg_base_rates', '~180', 'PMTAC Fee Calculator (or NYS DOH DTC inventory)'],
      ['PxBasedWeight', 'px_based_weights', '~5,300', 'NYS DOH weights history'],
      ['FeeScheduleItem', 'fee_schedule', '~2,100', 'NYS DOH weights history'],
      ['ProviderCounty', 'provider_county', '62', 'PMTAC Fee Calculator (NY counties → region)'],
    ],
    [1700, 1900, 1500, 4260]
  ),
  spacer(),
  h2('3.3 Operational entities'),
  makeTable(
    ['Entity', 'Table', 'Purpose'],
    [
      ['ProviderConfig', 'provider_config', 'Active provider (per user) — peer group, county, region, CMS locality'],
      ['CmsRateCache', 'cms_rate_cache', '24h cache of CMS PFS rate lookups (HCPCS × modifier × locality × year)'],
      ['AspNetUsers / AspNetRoles', 'AspNet*', 'Standard ASP.NET Core Identity tables for auth'],
    ],
    [2200, 1900, 5260]
  ),
  spacer(),
  h2('3.4 NYS APG methodology — the math'),
  p(
    'The APG calculation determines the "correct" payment for a Medicaid Article 28 outpatient ' +
      'claim using a priority ladder. For each service line:'
  ),
  numbered(
    '**Fee Schedule** check — if the procedure code appears in the NYS DOH fee schedule for the date of service, use the flat fee (typically applies to packaged drugs and supplies).'
  ),
  numbered(
    '**Procedure-based weight** check — if the HCPCS has an explicit Px weight on file, compute weight × base_rate.'
  ),
  numbered(
    '**APG weight** check — fall through to APG-level weight (the procedure\'s assigned EAPG\'s weight) × base_rate.'
  ),
  numbered(
    '**Visit-purpose ICD override** — for incidental E/M codes (99201–99499), if the principal diagnosis maps to a different EAPG that pays more, use that instead. This is the rule that produces the canonical "$132.09" result for a 99213 + E11.9 encounter.'
  ),
  numbered(
    '**Modifiers** — the U6 modifier multiplies by 1.5×; multi-procedure discounting reduces secondary lines by 50%; capital add-on fee is added to the claim total when the provider is eligible.'
  ),
  p(
    'The base rate is selected by joining the active provider\'s peer group (e.g., "Clinic*", ' +
      '"Amb Surg", "Renal") with their region (Upstate / Downstate, derived from the configured ' +
      'county) and the date of service. The same procedure can have different base rates over ' +
      'time as NYS DOH publishes updates.'
  ),
  h2('3.5 The 837/835 link'),
  p(
    'Claims arrive in two complementary formats: the **837** (the provider submitting the claim) ' +
      'and the **835** (the payer\'s remittance with paid amounts and adjustments). When both ' +
      'land in the database for the same CLP01 / CLM01 ID, the application links them via ' +
      '**ParsedClaim.LinkedClaimIdFk** — a bidirectional self-FK. Linking enriches the 835 ' +
      'with diagnosis codes from the 837 (835s rarely carry dx codes), which lets the visit-' +
      'purpose ICD override fire correctly when re-pricing.'
  )
);

// ============================================================================
// 4. DATABASE SCHEMA
// ============================================================================
children.push(h1('4. Database Schema'));

children.push(
  p(
    'The database is generated by EF Core code-first migrations. Every schema change is a ' +
      'numbered migration in `APGAnalyzer/Data/Migrations/`. The current set is listed below ' +
      'in chronological order — applying them all (`dotnet ef database update`) brings a fresh ' +
      'database to the current schema.'
  ),
  h2('4.1 Migration history'),
  makeTable(
    ['Migration', 'Purpose'],
    [
      ['00000000000000_CreateIdentitySchema', 'ASP.NET Core Identity tables (AspNetUsers, AspNetRoles, AspNetUserRoles, etc.)'],
      ['20260429150458_AddDomainTables', 'Reference tables: hcpcs_to_eapg, icd10_to_eapg, apg_weights, etc.'],
      ['20260429194457_AddProviderConfig', 'Active provider configuration table'],
      ['20260429200242_ProviderCountyNoIdentity', 'Drop IDENTITY column on provider_county (county codes are externally assigned)'],
      ['20260429204928_AddClaimTables', 'Operational tables: parsed_claim, parsed_service_line, claim_adjustment, apg_result'],
      ['20260501140008_AddOwnerUserId', 'Per-user data isolation: OwnerUserId column on parsed_claim and provider_config + backfill SQL'],
      ['20260501142326_AddCmsRateCache', 'CMS Medicare PFS rate cache table'],
    ],
    [4400, 4960]
  ),
  spacer(),
  h2('4.2 Indexes'),
  p('Indexes are defined in the DbContext OnModelCreating method. Notable ones:'),
  bullet('**ix_hcpcs_eapg_code_date** on `(Hcpcs, QuarterEffectiveDate)` — primary lookup for procedure → EAPG mapping'),
  bullet('**ix_icd10_eapg_code_date** on `(DxCode, EffectiveDate)` — diagnosis → EAPG (visit-purpose override)'),
  bullet('**uq_apg_weight_date** on `(Apg, EffectiveDate)`, unique — at most one weight per APG per effective date'),
  bullet('**ix_base_rate_lookup** on `(Source, PeerGroup, Region, EffectiveDate)` — join target during pricing'),
  bullet('**ix_parsed_claim_owner_created** on `(OwnerUserId, CreatedAt)` — drives the Claims list page (sorted desc by CreatedAt, scoped per user)'),
  bullet('**ix_provider_config_owner_active** on `(OwnerUserId, IsActive)` — single row hot-path read at every claim calculation'),
  bullet('**uq_cms_rate_cache_key** on `(Hcpcs, Modifier, Locality, Year)`, unique — ensures one cache row per parameter set'),
  spacer(),
  h2('4.3 Cascade behavior'),
  p('Foreign keys behave as follows when a parent is deleted:'),
  makeTable(
    ['Parent', 'Child', 'On parent delete'],
    [
      ['parsed_claim', 'parsed_service_line', 'CASCADE — lines are useless without their claim'],
      ['parsed_claim', 'claim_adjustment', 'CASCADE — same reason'],
      ['parsed_claim', 'apg_result', 'CASCADE — cached output goes with the claim'],
      ['parsed_claim (linked sibling)', 'parsed_claim', 'NO ACTION — application code nulls LinkedClaimIdFk before deleting'],
    ],
    [2300, 2300, 4760]
  ),
  spacer()
);
children.push(
  ...callout(
    'Implementation note',
    [
      'The SQL Server engine refuses to create a cascading FK on a table that already has cascading ' +
        'children (potential cycle). The 837↔835 self-link is therefore declared NoAction; the bulk-' +
        'delete code in `ClaimsController.DeleteSelected` explicitly nulls `LinkedClaimIdFk` on every ' +
        'affected row in a separate `SaveChangesAsync()` round-trip before deleting.',
    ],
    HEADER_GREY
  )
);

// ============================================================================
// 5. BUILD PHASES
// ============================================================================
children.push(h1('5. Build Phases'));

children.push(
  p(
    'The application was built in distinct phases, each producing a working slice. The phasing ' +
      'made it possible to validate each layer (parser, engine, UI) independently and avoid the ' +
      'big-bang integration risk that often sinks rewrite projects.'
  )
);

// Phase 1
children.push(h2('5.1 Phase 1 — Skeleton'));
children.push(
  p('**Output**: a runnable empty MVC application with Identity, EF Core, Bootstrap 5, and a homepage that confirms the database is reachable.'),
  bullet('Bootstrapped with `dotnet new mvc --auth Individual`'),
  bullet('Switched UI from default jQuery + bootstrap.bundle to PMTAC theme (purple navbar, icon-over-label nav)'),
  bullet('Connected to LocalDB; first migration creates the AspNet* Identity tables'),
  bullet('Verified login/register works'),
  bullet('Output: a deployable shell ready for domain code')
);

// Phase 2
children.push(h2('5.2 Phase 2 — Reference data loaders'));
children.push(
  p('**Output**: an admin-only Settings page that ingests the four reference workbooks into the database.'),
  bullet('**ICrosswalkLoader** — reads eMedNY APG Crosswalk .xlsx, replaces hcpcs_to_eapg + icd10_to_eapg'),
  bullet('**IWeightsHistoryLoader** — reads NYS DOH weights .xls (BIFF format via NPOI), populates apg_weights, px_based_weights, fee_schedule'),
  bullet('**IPmtacFeeCalculatorLoader** — reads PMTAC Fee Calculator .xlsm, populates apg_base_rates + provider_county'),
  bullet('**IDtcBaseRatesLoader** — alternative source for base rates only'),
  bullet('**IMasterResetService** — destructive, wipes every reference table (preserves users, audit log, claims, providers)'),
  bullet('Each loader is idempotent: running twice with the same file produces the same final state')
);

// Phase 3
children.push(h2('5.3 Phase 3 — APG calculation engine'));
children.push(
  p('**Output**: the IApgEngine service that converts a parsed claim DTO + provider config into an APGResult.'),
  bullet('Direct port of `backend/engines/apg_engine.py`'),
  bullet('Implements the priority ladder: fee schedule > Px weight > APG weight'),
  bullet('Handles modifiers: U6 (×1.5), multi-procedure discounting (50% off secondaries), capital add-on'),
  bullet('Visit-purpose ICD override for incidental E/M codes'),
  bullet('Returns line-by-line `APGLineResult` plus claim-level totals and notes'),
  bullet('Verified against the reference value: 99213 + E11.9 in Manhattan = $132.09 (matches Python)'),
  bullet('Provided through DI as a scoped service'),
);

// Phase 4
children.push(h2('5.4 Phase 4 — EDI parsers and upload pipeline'));
children.push(
  p('**Output**: an Upload page that accepts X12 EDI 835/837 files, parses them, stores claims and lines, and prices them via the APG engine.'),
  h3('Parsers'),
  bullet('**Edi835IParser / Edi835PParser** — institutional and professional remittance parsers; both produce a Parsed835IResult with claim list'),
  bullet('**Edi837Parser** — handles both institutional (837I) and professional (837P) submissions; auto-detects from GS08 implementation guide identifier'),
  bullet('**EdiCommon** — segment splitter, element splitter, qualifier helpers shared across parsers'),
  h3('Upload simplification (later session)'),
  bullet('UI dropped from 4-option (835I/835P/837I/837P) to 2-option (835 / 837); the specific subtype is auto-detected'),
  bullet('Multi-file upload — the user picks any number of EDI files at once'),
  bullet('**EdiFileTypeDetector** — inspects bytes:'),
  bullet('  · 837: GS08 implementation guide (X222 = P, X223 = I); fallback to SV1 vs SV2 segment presence', 1),
  bullet('  · 835: SVC qualifier scan (NU = institutional revenue codes, HC only = likely professional); defaults to 835I in ambiguous cases', 1),
  h3('Upload service'),
  bullet('**ClaimUploadService** — orchestrates: parse → save → price → link'),
  bullet('Each parsed claim goes through the engine immediately if a provider config exists'),
  bullet('After a batch, **ClaimLinkerService** auto-links any 837s/835s that share the same CLP01 ID, copies dx codes from 837 → 835, and re-prices the 835')
);

// Phase 5
children.push(h2('5.5 Phase 5 — Analytics and exports'));
children.push(
  p('**Output**: the Analytics dashboard and Excel/PDF export plumbing.'),
  h3('Analytics service'),
  bullet('**IAnalyticsService.ComputeAsync(filters, ct)** — single call returns a fully-populated AnalyticsViewModel'),
  bullet('Per-user isolated via `OwnedBy(currentUser)` at the query root'),
  bullet('Six panels: Summary KPIs, Trends (monthly/quarterly), Denials by CARC, Top underpaid procedures, Compression breakdown (group-by EAPG/CPT/Peer/Region/Year), Payer scorecard'),
  bullet('All aggregations are server-side LINQ → translated to SQL by EF Core'),
  bullet('Time-series uses GroupBy on Year+Month then optionally rolls up to Quarter in memory'),
  h3('Exports'),
  bullet('**ExportService.BuildClaimsListXlsx** — list export'),
  bullet('**ExportService.BuildClaimDetailXlsx** — single-claim every-field export'),
  bullet('**ExportService.BuildFullDataXlsx** — multi-sheet workbook joinable on ClaimId/ClaimIdFk'),
  bullet('**ExportService.BuildAnalyticsXlsx** — 9-sheet analytics dashboard export'),
  bullet('**ExportService.BuildClaimDetailPdf** — narrative PDF (QuestPDF)'),
  bullet('**ExportService.BuildCms1500Pdf / BuildUb04Pdf** — form-shaped PDFs')
);

// Phase 6
children.push(h2('5.6 Phase 6 — Polish, theming, deployment readiness'));
children.push(
  bullet('Custom **PMTAC theme** in `wwwroot/css/pmtac-theme.css`: purple #6E50CD primary, navbar, breadcrumbs, cards, badges'),
  bullet('Layout rebuild — icon-over-label nav, brand block on left, bell + avatar on right'),
  bullet('Bulk delete on Claims list (admin/analyst only) with checkboxes, master select-all, and per-row delete'),
  bullet('Status messages via TempData on delete'),
  bullet('Per-row CMS-1500 / UB-04 / Excel / Full Data export buttons on each claim'),
  bullet('Smoke-test fixtures: `samples/paired_835i.edi` and `samples/paired_837i.edi` demonstrating the linker + visit-purpose override pathway')
);

// Post go-live phases
children.push(h2('5.7 Post-go-live extensions'));
children.push(
  p(
    'After the initial six phases shipped, several feature extensions were added based on real ' +
      'use feedback. These are the same code in the same project, just delivered later.'
  ),
  h3('5.7.1 User management + role-based authorization'),
  bullet('Three roles: **admin**, **analyst**, **viewer**'),
  bullet('Admin-only Users page (/Users) — list, create, edit role, reset password, delete'),
  bullet('Self-service registration blocked via Razor Pages convention; bootstrap-aware policy lets the very first registration succeed when no users exist'),
  bullet('Safety guards: cannot demote yourself if sole admin, cannot lock yourself out, cannot delete the last admin'),
  h3('5.7.2 Per-user data isolation'),
  bullet('Added `OwnerUserId` (string FK to AspNetUsers.Id) to ParsedClaim and ProviderConfig'),
  bullet('**ICurrentUserContext** + **OwnedQueryExtensions.OwnedBy(ctx)** — every read query gets a one-line filter'),
  bullet('Migration backfilled all existing rows to the seeded admin'),
  bullet('**View-as switcher** in navbar (admin/viewer only) — scope the session to one user; persistent breadcrumb makes it obvious'),
  bullet('Owner column on the Claims list, visible only when admin/viewer is in unscoped (all-users) mode'),
  h3('5.7.3 CMS Medicare PFS integration'),
  bullet('**CmsRateService** — port of Python `cms_engine.py`'),
  bullet('Self-healing UUID resolution from pfs.data.cms.gov/data.json'),
  bullet('Two-dataset model: "Indicators for YYYY" (RVUs + CF) + "Localities for YYYY" (GPCIs)'),
  bullet('Formula: `payment = ((rvu_work × gpci_w) + (pe_rvu × gpci_pe) + (rvu_mp × gpci_mp)) × CF`'),
  bullet('Optional PC/TC split (-26 professional, -TC technical)'),
  bullet('24-hour DB cache + graceful stale-cache fallback when CMS unreachable'),
  bullet('Wired into the Rate Calculator (rate-source dropdown) and the 837P/835P claim Detail page (auto comparison panel)'),
  bullet('Admin-only Settings card to invalidate the cache after CMS quarterly publications'),
  h3('5.7.4 Multi-line Rate Calculator'),
  bullet('JS-driven add-row / remove-row UI — no fixed maximum'),
  bullet('Rate-source dropdown: APG (NYS Institutional) / CMS Medicare (Professional) / Both side-by-side'),
  bullet('Searchable CMS locality dropdown grouped by MAC region'),
  bullet('Facility vs non-facility toggle, PC/TC split toggle'),
  h3('5.7.5 Analytics dashboard'),
  bullet('Filter bar: date range (default last 12 months), payer, file type, NPI, group-by, trend period'),
  bullet('Six panels: KPI tiles, Trends line chart, Denials by CARC bar chart, Top underpaid procedures, Compression breakdown, Payer scorecard'),
  bullet('Drill-through links from procedure / payer / claim ID rows back to filtered Claims list'),
  bullet('Excel export — 9-sheet workbook honoring all current filters')
);

// ============================================================================
// 6. SECURITY & ACCESS CONTROL
// ============================================================================
children.push(h1('6. Security and Access Control'));

children.push(
  h2('6.1 Authentication'),
  p(
    'Authentication is handled by ASP.NET Core Identity with default cookie-based sessions. ' +
      'Passwords are hashed with PBKDF2 + per-user salt (Identity defaults). The cookie is HTTP-only, ' +
      'secure, and SameSite=Lax. There is no refresh-token flow because there is no separate API.'
  ),
  h2('6.2 Authorization — role matrix'),
  makeTable(
    ['Capability', 'Admin', 'Analyst', 'Viewer'],
    [
      ['View Dashboard, Claims, Analytics, Calculator', '✓', '✓', '✓'],
      ['Upload claim files', '✓', '✓', '—'],
      ['Delete claims (single + bulk)', '✓', '✓', '—'],
      ['Export to Excel / PDF / Full Data', '✓', '✓', '✓'],
      ['Edit Provider Configuration', '✓', '✓', '—'],
      ['Reference-data Settings (upload workbooks, master reset, refresh CMS)', '✓', '—', '—'],
      ['User management (create, edit role, reset password, delete)', '✓', '—', '—'],
      ['View-as switcher (scope session to one user\'s data)', '✓', '—', '✓'],
    ],
    [4500, 1500, 1500, 1860]
  ),
  spacer(),
  h2('6.3 Role enforcement'),
  p('Roles are enforced at controller-action granularity using `[Authorize(Roles = "...")]`:'),
  ...code(
    '// Admin only\n' +
    '[Authorize(Roles = "admin")]\n' +
    'public class UsersController : Controller { ... }\n' +
    '\n' +
    '// Editor roles (admin OR analyst)\n' +
    '[Authorize(Roles = RoleSeeder.EditorRoles)]\n' +
    'public class UploadController : Controller { ... }\n' +
    '\n' +
    '// Any authenticated user\n' +
    '[Authorize]\n' +
    'public class ClaimsController : Controller { ... }',
    { lang: 'C#' }
  ),
  h2('6.4 Per-user data isolation'),
  p(
    'Even though analysts and viewers share a database, their *visible* data is scoped to themselves ' +
      'unless they have an admin/viewer role and explicitly switch context. The mechanism:'
  ),
  numbered(
    '**Stamping on writes** — `ClaimUploadService` and `ProviderConfigController.Save` set `OwnerUserId = currentUser.SignedInUserId` on every newly-created row. The signed-in user is used here, *not* the view-as target — admins can\'t accidentally upload into another user\'s bucket while scoped.'
  ),
  numbered(
    '**Filtering on reads** — every controller that reads ParsedClaim or ProviderConfig pipes through `db.ParsedClaims.OwnedBy(currentUser)`. The extension method applies `WHERE OwnerUserId = ...` based on the effective owner filter.'
  ),
  numbered(
    '**Effective owner** — `ICurrentUserContext.EffectiveOwnerFilter` returns NULL (no filter, see everything) for admins/viewers in unscoped mode, otherwise the signed-in or view-as target user ID.'
  ),
  numbered(
    '**Sibling/linker scoping** — `ClaimLinkerService.LinkAndEnrichAsync` takes an `ownerUserId` parameter, so two analysts who both upload claims with the same CLM01 won\'t cross-link.'
  ),
  ...code(
    '// Marker interface; implemented by ParsedClaim and ProviderConfig\n' +
    'public interface IOwnedByUser\n' +
    '{\n' +
    '    string? OwnerUserId { get; set; }\n' +
    '}\n' +
    '\n' +
    '// Extension applied at every read site\n' +
    'public static IQueryable<T> OwnedBy<T>(this IQueryable<T> query,\n' +
    '                                        ICurrentUserContext ctx)\n' +
    '    where T : class, IOwnedByUser\n' +
    '{\n' +
    '    var owner = ctx.EffectiveOwnerFilter;\n' +
    '    if (owner is null) return query; // unscoped admin/viewer — see everything\n' +
    '    return query.Where(e => e.OwnerUserId == owner);\n' +
    '}',
    { lang: 'C# — OwnedQueryExtensions.cs' }
  ),
  h2('6.5 The bootstrap-admin problem'),
  p(
    'A fresh deployment has zero users and zero admins. With self-service registration blocked, ' +
      'no one can create the first admin — chicken-and-egg. The application solves this with an ' +
      '**AdminOrBootstrapRequirement** authorization handler:'
  ),
  bullet('If the current user is in the admin role → succeed (the normal path post-bootstrap)'),
  bullet('Else if there are zero users in the entire database → succeed (bootstrap mode for the first deploy)'),
  bullet('Otherwise → fail (the default for any non-admin trying to register)'),
  p(
    'After the first user registers, the app must be restarted (Azure Portal → App Service → Restart) ' +
      'so that `RoleSeeder.SeedAsync` runs again and promotes the first user to admin. From that point ' +
      'forward the bootstrap branch is unreachable and registration is admin-only.'
  )
);

// ============================================================================
// 7. CMS MEDICARE INTEGRATION
// ============================================================================
children.push(h1('7. CMS Medicare PFS Integration'));

children.push(
  p(
    'The CMS Medicare integration computes the equivalent professional-services payment under ' +
      'the CMS Physician Fee Schedule (PFS). It is used in three places: the Rate Calculator (manual ' +
      'CPT entry), the 837P/835P claim Detail page (automatic comparison panel), and the planned ' +
      'analytics module that benchmarks Medicaid against Medicare.'
  ),
  h2('7.1 Data source'),
  bullet('**Endpoint**: `https://pfs.data.cms.gov/data.json` (catalog) and `/api/1/datastore/query/{uuid}/0` (DKAN POST queries)'),
  bullet('**No authentication** — public, anonymous CMS data'),
  bullet('**Two datasets per year**: "Indicators for YYYY" (RVUs + conversion factor) and "Localities for YYYY" (GPCIs)'),
  bullet('**Self-healing UUID resolution** — UUIDs are *never* hardcoded; the engine fetches data.json, regex-matches "Indicators for {year}{suffix}" and "Localities for {year}{suffix}", and picks the newest suffix (preference: no suffix > B > A)'),
  h2('7.2 Formula'),
  ...code(
    'non_facility_rate = ((rvu_work × gpci_work)\n' +
    '                  +  (pe_rvu_nonfac × gpci_pe)\n' +
    '                  +  (rvu_mp × gpci_mp)) × conversion_factor\n' +
    '\n' +
    'facility_rate     = ((rvu_work × gpci_work)\n' +
    '                  +  (pe_rvu_facility × gpci_pe)\n' +
    '                  +  (rvu_mp × gpci_mp)) × conversion_factor',
    { lang: 'CMS PFS formula' }
  ),
  bullet('**Non-facility rate** — applies in independent clinics / offices'),
  bullet('**Facility rate** — lower; applies in hospital outpatient settings (CMS pays the facility separately)'),
  bullet('**PC/TC split** (optional) — fetches the -26 (professional component) and -TC (technical component) modifier rows in parallel; not every HCPCS has these'),
  h2('7.3 Caching strategy'),
  p(
    'Three layers, in order of speed:'
  ),
  numbered(
    '**In-process catalog cache** (24-hour TTL, per process) — the fetched data.json plus computed UUIDs for each requested year. First request hits CMS; subsequent requests are O(1).'
  ),
  numbered(
    '**In-process locality-list cache** (24-hour TTL) — populated when the locality dropdown is rendered. ~110 rows per year.'
  ),
  numbered(
    '**Persistent DB cache** — `cms_rate_cache` table, keyed by (HCPCS, modifier, locality, year). 24-hour `CachedUntil` timestamp. Stale rows are still returned if the live API is unreachable (graceful degradation).'
  ),
  h2('7.4 Cache refresh'),
  p(
    'Admins can invalidate the cache via Settings → "Refresh CMS fee schedule cache". The button:'
  ),
  bullet('Runs a single SQL `UPDATE cms_rate_cache SET CachedUntil = utcnow` (instant, no API calls)'),
  bullet('Clears the in-process catalog and locality caches'),
  bullet('Subsequent rate lookups will re-fetch fresh data from CMS'),
  bullet('Designed for the quarterly CMS PFS publication cadence (Jan/Apr/Jul/Oct, plus occasional mid-year corrections)'),
  h2('7.5 Outbound network requirement')
);
children.push(
  ...callout(
    'IT note',
    [
      'Production deployment requires **outbound HTTPS to pfs.data.cms.gov** through any corporate ' +
        'firewall or NSG. The beta deployment confirmed Azure App Service\'s default outbound ruleset ' +
        'allows this. If your enterprise environment restricts outbound traffic, add this domain to ' +
        'the allow-list before deployment, or expect "CMS catalog unreachable" banners in the UI.',
    ],
    HEADER_GREY
  )
);
children.push(
  h2('7.6 Threading caveat'),
  p(
    'EF Core\'s DbContext is **not thread-safe** — only one query may be in flight per instance at a time. ' +
      'Early in development, the calculator code launched the base, -26, and -TC cache lookups via ' +
      '`Task.WhenAll` to parallelize. On Azure SQL Free Tier (which has higher latency than LocalDB), ' +
      'the second concurrent query would start before the first completed, triggering:'
  ),
  ...code(
    '"A second operation was started on this context instance before a previous\n' +
    ' operation completed. This is usually caused by different threads\n' +
    ' concurrently using the same instance of DbContext."',
    { lang: 'EF Core' }
  ),
  p(
    'The fix was to make the cache reads sequential. The CMS HTTP fetches inside `CmsRateService` ' +
      'still parallelize (using `Task.WhenAll` over independent `HttpClient` calls), so the user-facing ' +
      'latency is unchanged. The serialized work is just the per-row DB cache lookup, which is a B-tree ' +
      'seek on a unique index — sub-millisecond.'
  )
);

// ============================================================================
// 8. AZURE TRIAL DEPLOYMENT
// ============================================================================
children.push(h1('8. Azure Trial Deployment'));

children.push(
  p(
    'This section is the playbook used to stand up the beta environment on Azure free-tier resources. ' +
      'Total monthly cost: $0 (with caveats — see Section 8.10). Total deployment time: ~60 minutes ' +
      'including Azure account creation, with another ~30 minutes for first-time troubleshooting of ' +
      'common cold-start issues.'
  ),
  h2('8.1 Prerequisites'),
  bullet('A working .NET 10 build of the application (verified by `dotnet build` exiting clean)'),
  bullet('Visual Studio 2022 17.x or later with the "Azure development" workload installed'),
  bullet('A Microsoft account (work or personal) — used for Azure sign-in'),
  bullet('A credit card (used for Microsoft\'s identity verification only; the free SKUs we use never bill it)'),
  bullet('Outbound HTTPS allowed from your laptop to portal.azure.com and pfs.data.cms.gov'),
  bullet('LocalDB running and the app verified working in dev mode (so we know the build is good before we deploy it)'),
  h2('8.2 Code-level preparation'),
  p('Before publishing, three small code-level changes ensure the deployed app behaves correctly:'),
  numbered('**Move the dev connection string out of `appsettings.json`** into `appsettings.Development.json`. The production-loaded `appsettings.json` should have NO connection string — it will be supplied by Azure App Service Configuration.'),
  numbered('**Add `appsettings.Production.json`** with sensible production logging defaults (Warning level for noisy namespaces, Information for `APGAnalyzer`).'),
  numbered('**Confirm `Program.cs` reads the connection string via `builder.Configuration.GetConnectionString("DefaultConnection")`** — this naturally picks up Azure\'s injected value.'),
  ...code(
    '// appsettings.json (production-loaded)\n' +
    '{\n' +
    '  // ConnectionStrings:DefaultConnection is intentionally NOT set here.\n' +
    '  //   Dev:  loaded from appsettings.Development.json (LocalDB).\n' +
    '  //   Prod: loaded from Azure App Service > Configuration\n' +
    '  //         > Connection strings (the Publish wizard provisions this).\n' +
    '  "Logging": { ... },\n' +
    '  "AllowedHosts": "*"\n' +
    '}',
    { lang: 'JSON' }
  ),
  h2('8.3 Step C1 — Create the free Azure SQL Database'),
  p(
    'The free Azure SQL Database tier (announced 2024) provides 32 GB storage, 100,000 vCore-seconds/month, ' +
      'and a 1-hour auto-pause delay. It is intentionally provisioned **before** the App Service so we can ' +
      'wire up the connection string in one step during the App Service creation.'
  ),
  numbered('Sign into the Azure Portal at portal.azure.com.'),
  numbered('Search for "Azure SQL Database" in the top search bar; click the result with type "SQL server" (the Microsoft.Sql/servers/databases resource).'),
  numbered('On the SQL databases listing page, click **+ Create** → **SQL database (Free offer)**. The free-offer flow pre-configures everything for you.'),
  numbered('On the **Basics** tab, fill in:'),
  bullet('  · Subscription: Azure subscription 1', 1),
  bullet('  · Resource group: create new, name it `apg-analyzer-beta`', 1),
  bullet('  · Database name: `APGAnalyzer`', 1),
  bullet('  · Server: create new (see next step)', 1),
  bullet('  · Workload environment: Development', 1),
  numbered('In the "Create new server" panel:'),
  bullet('  · Server name: e.g., `apg-analyzer-sql-pmtac` (must be globally unique; add suffix if taken)', 1),
  bullet('  · Location: try Central US first; if free-tier capacity is exhausted, try Canada Central, then East US 2 / West US 2 / South Central US in turn', 1),
  bullet('  · Authentication method: SQL authentication', 1),
  bullet('  · Server admin login: `sqladmin`', 1),
  bullet('  · Password: generate a strong password and **save it in a password manager immediately** — Microsoft does not show it again', 1),
  numbered('Confirm "Free offer applied" banner shows on the form (100k vCore-sec, 32 GB data, 32 GB backup) and Estimated total = Free.'),
  numbered('Click **Review + create** → **Create**. Provisioning takes 2-5 minutes.'),
  numbered('On the deployment-complete page, click **Go to resource**. Confirm Status = Online.')
);
children.push(
  ...callout(
    'Free-tier capacity quirk',
    [
      'Microsoft caps how many free databases each region hosts. New subscriptions sometimes cannot ' +
        'create a free database in popular regions (East US, West US). Cycle through Central US → ' +
        'Canada Central → East US 2 → West US 2 → South Central US until one accepts. Canada Central ' +
        'is fully HIPAA-compatible under Microsoft\'s Canada BAA if US regions are unavailable.',
    ],
    HEADER_GREY
  )
);
children.push(
  h2('8.4 Step C2 — Create the App Service'),
  p(
    'The Visual Studio Publish wizard does most of this in one flow. Two gotchas: VS sometimes hides ' +
      'the F1 free tier from the Hosting Plan size dropdown; if that happens, create the App Service ' +
      'manually in the Portal first (Portal always shows F1) and pick "existing" in the wizard.'
  ),
  numbered('In Visual Studio Solution Explorer, right-click **APGAnalyzer** → **Publish**.'),
  numbered('On the first wizard screen pick **Azure** → **Next**.'),
  numbered('On the second screen pick **Azure App Service (Windows)** → **Next**.'),
  numbered('Sign into Azure with the same Microsoft account.'),
  numbered('Click **+ Create new** to create a new App Service. Fill the form:'),
  bullet('  · Name: `apg-analyzer-beta-pmtac` (becomes part of the URL)', 1),
  bullet('  · Resource group: pick `apg-analyzer-beta` (the one created with the SQL DB)', 1),
  bullet('  · Hosting Plan: New → name `apg-analyzer-plan`, location **Central US** (must match SQL DB), Size **F1 Free**', 1),
  numbered('If F1 doesn\'t appear in the Size dropdown: cancel out, go to Azure Portal → App Services → +Create → Web App, pick F1 there directly. The Portal\'s tier picker is more complete.'),
  numbered('Click **Create** in the wizard, wait ~60-90 seconds for provisioning.'),
  numbered('Back on the Publish target list, select the new app, leave "Deploy as ZIP package" checked, leave "Turn on Basic Authentication" unchecked. Click **Finish**.'),
  numbered('On the resulting Publish summary page, verify Configuration = Release, Target Framework = net10.0, App Service = apg-analyzer-beta-pmtac.'),
  numbered('Click **Publish** at the top right. Build + ZIP + upload + restart takes 3-5 minutes. Browser opens automatically when done.'),
  h2('8.5 Step C2b — Wire up the connection string'),
  p('The deployed app needs to know how to reach the SQL database. We add the connection string to App Service Configuration as an environment variable.'),
  numbered('In Azure Portal, navigate to your **APGAnalyzer** SQL database → **Connection strings** in the left sidebar.'),
  numbered('Copy the **ADO.NET (SQL authentication)** string (the bottom box). It looks like: `Server=tcp:apg-analyzer-sql-pmtac.database.windows.net,1433;Initial Catalog=APGAnalyzer;User ID=sqladmin;Password={your_password};...`'),
  numbered('Open Notepad, paste the string, replace `{your_password}` with the real password (no curly braces).'),
  numbered('Navigate to your **apg-analyzer-beta-pmtac** App Service → **Settings** → **Environment variables** → **Connection strings** tab.'),
  numbered('Click **+ Add**. Fill in:'),
  bullet('  · Name: `DefaultConnection` (case-sensitive; must match exactly what Program.cs reads)', 1),
  bullet('  · Value: paste the full string from Notepad', 1),
  bullet('  · Type: **SQLAzure**', 1),
  numbered('Click **Apply** in the popup, then **Apply** at the top of the page (Azure has a two-step save). Confirm the app restart.'),
  h2('8.6 Step C2c — SQL firewall rules'),
  p('The SQL Server has a firewall that defaults to deny-all. Two rules need to exist:'),
  bullet('**Allow Azure services and resources** — let the App Service connect (this is usually enabled automatically by the Free offer creation flow)'),
  bullet('**Your laptop\'s public IP** — required to apply migrations from your local PowerShell using `dotnet ef database update`'),
  numbered('Navigate to your SQL **server** (not the database) → **Security** → **Networking**.'),
  numbered('Confirm "Allow Azure services and resources to access this server" = **Yes**.'),
  numbered('Click **+ Add your client IPv4 address (xxx.xxx.xxx.xxx)** — Azure auto-detects your IP.'),
  numbered('Click **Save** at the top of the page.'),
  h2('8.7 Step C3 — Apply database migrations'),
  p(
    'EF Core migrations create all the schema (Identity tables + reference tables + operational tables + ' +
      'cms_rate_cache). They run from your laptop against the Azure SQL database, not from the App Service.'
  ),
  numbered('Open a non-admin Windows PowerShell window (LocalDB instances are scoped per Windows user, so the admin shell sees a different LocalDB universe than your normal session — wrong shell will produce confusing errors).'),
  numbered('Navigate to the project: `cd "H:\\Working\\eProjects\\APG Calculator DotNet\\APGAnalyzer"`'),
  numbered('Copy the connection string (with the real password) from your Notepad to clipboard.'),
  numbered('Capture the clipboard into a variable: `$conn = Get-Clipboard`'),
  numbered('Sanity check: `$conn.Length` (should be ~250-280 characters)'),
  numbered('Apply migrations: `dotnet ef database update --connection $conn`'),
  numbered('Wait 1-3 minutes. EF Core builds the project, connects, runs all migrations in order. Final line: `Done.`')
);
children.push(
  ...callout(
    'Common error 40613 — Database not currently available',
    [
      'Azure SQL Free Tier auto-pauses after 1 hour idle. The first connection of the day fails because ' +
        'the DB is still waking. Fix: open the SQL database overview in the Portal (which triggers a wake-up), ' +
        'wait until Status = Online, then immediately retry the migration. The DB takes ~30-60 seconds to ' +
        'come back. This same error pattern reappears throughout the trial deployment whenever the DB ' +
        'has been idle for 1+ hour — see Section 9.2 for ongoing operational handling.',
    ],
    ACCENT_RED
  )
);
children.push(
  h2('8.8 Step D — Bootstrap the first admin'),
  p(
    'The very first user who registers on a fresh deployment gets auto-promoted to admin via the ' +
      '`AdminOrBootstrapRequirement` policy + `RoleSeeder.SeedAsync` startup code.'
  ),
  numbered('Navigate to your live URL: `https://apg-analyzer-beta-pmtac-{random}.centralus-01.azurewebsites.net`'),
  numbered('Wait 30-90 seconds for first-request cold start.'),
  numbered('Click **Login** → "Register as a new user" link'),
  numbered('Register with your real email and a strong password. After successful registration you\'ll be auto-signed-in but **without** any role yet.'),
  numbered('Restart the App Service from the Portal: navigate to the App Service → **Restart** at the top toolbar → confirm. This forces `RoleSeeder.SeedAsync` to run again. It detects "1 user, 0 admins" and promotes you.'),
  numbered('Wait ~60 seconds for the App Service to come back up.'),
  numbered('Sign back in. Your navbar should now show admin-only items: Provider, Users, Settings, plus the "Viewing: All users" view-as dropdown in the top-right.'),
  h2('8.9 Step D2 — Load reference data and create beta accounts'),
  p('From the admin dashboard:'),
  numbered('Click **Settings** in the navbar (admin-only).'),
  numbered('Upload the four reference workbooks **in order**:'),
  bullet('  · Card 1 — eMedNY APG Crosswalk (.xlsx) → ~96k rows, ~30 sec', 1),
  bullet('  · Card 2 — NYS DOH Weights + Fees (.xls) → ~29k rows, ~3-5 sec', 1),
  bullet('  · Card 3 — PMTAC Fee Calculator (.xlsm) → ~250 rows, ~5 sec', 1),
  bullet('  · Card 4 — DTC Base Rates (.xls, optional) → 182 rows', 1),
  numbered('Verify on the Dashboard: Total reference rows ≈ 125,249.'),
  numbered('Set up your own Provider Config (Provider in navbar) with peer group, county, region, CMS locality (e.g., 1320201 for Manhattan).'),
  numbered('Create beta tester accounts via Users → + Add user. For each tester pick the appropriate role (analyst for daily users, viewer for oversight roles).'),
  numbered('Send each tester their URL, login email, and temp password through your normal corporate channel. Recommend they change their password on first login.'),
  h2('8.10 Step D3 — Smoke tests'),
  p('Three quick verifications to run before letting beta testers loose:'),
  numbered('**APG path** — Rate Calculator with source=APG, principal ICD = E11.9, CPT = 99213, locality = whatever matches your peer group + county. Expected result: ~$132.09 (matches the canonical reference).'),
  numbered('**CMS path** — Rate Calculator with source=CMS Medicare, locality = 1320201, CPT = 99213. Expected: ~$107.63 (Manhattan 2026, varies by year as CMS publishes updates). This verifies outbound HTTPS to pfs.data.cms.gov from Azure App Service.'),
  numbered('**Upload pipeline** — upload one of the synthetic samples (`samples/paired_835i.edi` or `paired_837i.edi`). Verify the claim appears on the Claims list, the calculation completes, and the Detail page renders.')
);

// ============================================================================
// 9. OPERATIONAL NOTES
// ============================================================================
children.push(h1('9. Operational Notes'));

children.push(
  h2('9.1 Free-tier characteristics and trade-offs'),
  makeTable(
    ['Tier', 'Cost / month', 'Auto-sleep', 'CPU cap', 'RAM', 'Best for'],
    [
      ['F1 (App Service Free)', '$0', '20 min idle', '60 min/day', '1 GB shared', 'Demo, light testing, single-user'],
      ['B1 (App Service Basic)', '~$13', 'never', 'no cap', '1.75 GB dedicated', 'Active beta with real upload workloads'],
      ['SQL Free Tier', '$0', '1 hour idle', '100k vCore-sec/month', '32 GB storage', 'Beta with infrequent use'],
      ['SQL Basic ($5/mo)', '~$5', 'never', '~5 DTU', '2 GB storage', 'Always-warm beta or low-traffic prod'],
      ['SQL S0 / S1', '$15-30', 'never', 'higher DTU', '250 GB storage', 'Light production'],
    ],
    [1900, 1300, 1300, 1500, 1500, 1860]
  ),
  spacer(),
  h2('9.2 Cold-start playbook'),
  p(
    'On the all-free configuration (F1 + free SQL), both layers can be asleep at once. The first ' +
      'request after >1 hour idle hits this sequence:'
  ),
  numbered('Browser → App Service → cold-start the worker process (~30-60 sec)'),
  numbered('App Service → SQL DB → DB resume from auto-pause (~30-60 sec)'),
  numbered('App startup → `RoleSeeder.SeedAsync` → first DB query (~5 sec)'),
  p(
    'Combined first-request time: **60-120 seconds**. Subsequent requests (and any request within ' +
      '20 min of the previous one) are fast.'
  ),
  p('**If a request times out (HTTP 500.30) after a long idle period**:'),
  numbered('Open the SQL database in Azure Portal — viewing the Overview page triggers a wake-up.'),
  numbered('Wait until Status = Online (refresh the page every 15 sec).'),
  numbered('Open the App Service in Azure Portal → click **Restart** at the top.'),
  numbered('Wait ~90 seconds.'),
  numbered('Refresh your browser tab (Ctrl+F5).'),
  p(
    '**To eliminate cold starts permanently**: scale up to App Service B1 (~$13/mo) and SQL Basic ' +
      '(~$5/mo). Both stay warm 24/7. Beta testers will see snappy first-load, the experience matches ' +
      'production. Total ~$18/mo for the entire beta period.'
  ),
  h2('9.3 Quarterly CMS refresh'),
  p('CMS publishes new PFS data ~quarterly (Jan, Apr, Jul, Oct, plus mid-year corrections). When the new dataset is up:'),
  numbered('Sign in as admin.'),
  numbered('Settings → Card 5: **Refresh CMS fee schedule cache**.'),
  numbered('Confirm the dialog.'),
  p('The button: clears in-process caches (catalog, locality lists), runs `UPDATE cms_rate_cache SET CachedUntil = utcnow` (instant). Subsequent rate lookups will pull fresh data from CMS. The next user to use the calculator pays the cold-fetch cost (~5 sec); everyone after that hits the new warm cache.'),
  h2('9.4 Reference data refresh'),
  p(
    'When NYS DOH publishes new APG reference data (typically annually or after methodology updates):'
  ),
  numbered('Download the four PMTAC / NYS DOH reference workbooks for the new period.'),
  numbered('Sign in as admin.'),
  numbered('Settings → upload the four files via Cards 1-4. Each upload **replaces** the previous data (not append).'),
  numbered('Verify Dashboard totals match expected counts.'),
  numbered('No claims data is touched; the engine\'s next calculation will use the new reference data.'),
  h2('9.5 Backups'),
  bullet('Azure SQL Database has **automatic point-in-time-restore** included on every tier (7 days for Free/Basic, 35 days for S0+).'),
  bullet('Long-term backups can be configured at SQL Server → Backups → Retention policies.'),
  bullet('App Service code is recoverable from the local Visual Studio publish profile and git — no separate App Service backup is required.'),
  bullet('Reference data workbooks should be archived in PMTAC\'s document store, not relied on as "live" inside the application.')
);

// ============================================================================
// 10. ROADMAP / DEFERRED ITEMS
// ============================================================================
children.push(h1('10. Roadmap and Deferred Items'));

children.push(
  h2('10.1 Cosmetic / quick wins'),
  bullet('**Footer text** says "SQL Server (LocalDB)" and "Phase 1 — Skeleton" — both are stale strings from early dev. ~10 min cleanup.'),
  bullet('**Application Insights** — free tier is sufficient for beta-scale usage. Adds error tracking, slow-request analysis, and dependency telemetry. ~5 min wire-up via Azure Portal → App Service → Application Insights.'),
  bullet('**Custom domain** — instead of `apg-analyzer-beta-pmtac-xxx.azurewebsites.net`, point a friendly subdomain like `apg-beta.pmtac.com` at the App Service. Requires DNS access plus an SSL certificate (free via App Service Managed Cert).'),
  h2('10.2 Feature work deferred from V1'),
  bullet('**ZIP → CMS locality auto-resolution** — the Python build had a `zip_locality` table with the CMS ZIP5 file imported. The C# port relies on the locality dropdown instead. Quick to add when needed; the file is ~50k rows.'),
  bullet('**Saved analytics filter presets per user** — would need a small new table (`analytics_preset` with OwnerUserId + name + filter JSON) plus CRUD UI. Defer until usage tells us which filter combos people save vs. retype.'),
  bullet('**Bulk re-pricing of historical claims against CMS** — currently claims are priced APG-only at upload time. A "compute CMS variance for all claims" job would let the analytics dashboard show APG-vs-CMS comparisons retroactively.'),
  h2('10.3 V3 — separate analytics application'),
  p(
    'The current analytics page (V1 Tier 1+2+3) is intentionally simple: server-rendered Razor + Chart.js, ' +
      'no SPA framework. For deeper analytics (Days in A/R, Collection Rates, E&M Bell Curve, drill-' +
      'through, saved dashboards), the proposal is a **separate ASP.NET Core MVC project** (working name ' +
      '`APGAnalyzer.V3`) in the same Git repo and same Azure SQL database, but with its own deployment ' +
      'and Chart.js + DataTables stack. This separation:'
  ),
  bullet('Keeps the V1 surface stable while V3 evolves'),
  bullet('Lets V3 use a richer JS framework (React or Vue) without rewriting V1'),
  bullet('Enables deploying V3 to a different App Service tier (e.g., heavier compute) than V1'),
  bullet('Reuses the same Identity / per-user isolation / claims data'),
  h2('10.4 Production deployment (handed to IT)'),
  p('When IT does the production Azure deployment, they should:'),
  bullet('Use the same migration set (no schema differences between beta and prod)'),
  bullet('Pick **B1 or higher** App Service tier and **S0 or higher** SQL DB tier — production-like behavior, no cold-start issues, larger backup retention'),
  bullet('Confirm outbound HTTPS to **pfs.data.cms.gov** is allowed by NSG / firewall rules (verified working in beta on standard Azure outbound)'),
  bullet('Configure the connection string via App Service Configuration (NEVER in `appsettings.json`)'),
  bullet('Wire up Application Insights for production monitoring'),
  bullet('Consider Azure Front Door / Web Application Firewall in front of the App Service if exposing the application beyond the corporate network'),
  bullet('Coordinate the bootstrap-admin step (first registered user becomes admin) with the IT-provisioned account'),
  bullet('Decide on a reference-data refresh policy and document it for ongoing operations')
);

// ============================================================================
// 11. APPENDICES
// ============================================================================
children.push(h1('11. Appendices'));

children.push(
  h2('11.1 Glossary'),
  makeTable(
    ['Term', 'Definition'],
    [
      ['APG / EAPG', 'Ambulatory Patient Group / Enhanced APG — a payment classification system for outpatient services'],
      ['Article 28', 'New York State Public Health Law section that licenses outpatient clinics, including diagnostic & treatment centers (DTCs)'],
      ['CARC', 'Claim Adjustment Reason Code — explains why a payer reduced or denied a payment (e.g., 96 = "Non-covered charge")'],
      ['CLP / CLM', 'X12 EDI segment — claim payment information (835) / claim information (837)'],
      ['CPT / HCPCS', 'Current Procedural Terminology / Healthcare Common Procedure Coding System — the standard procedure-code vocabulary'],
      ['CMS', 'Centers for Medicare & Medicaid Services — federal agency that publishes Medicare reimbursement rates'],
      ['DKAN', 'Open-source datastore platform CMS uses to publish public data; underpins pfs.data.cms.gov'],
      ['DTC', 'Diagnostic & Treatment Center — Article 28-licensed outpatient clinic in New York'],
      ['EDI / X12', 'Electronic Data Interchange — the file format for medical claim transmission, governed by ASC X12'],
      ['GPCI', 'Geographic Practice Cost Index — multipliers in the CMS PFS formula reflecting regional cost differences'],
      ['ICD-10', 'International Classification of Diseases v10 — diagnosis code vocabulary'],
      ['MAC', 'Medicare Administrative Contractor — regional CMS contractor processing Medicare claims'],
      ['MPFS / PFS', 'Medicare Physician Fee Schedule — CMS\'s rate table for professional services'],
      ['NPI', 'National Provider Identifier — 10-digit ID for healthcare providers'],
      ['NYS DOH', 'New York State Department of Health — publishes the APG methodology and rates'],
      ['PCT / TC', 'Professional Component (-26 modifier) and Technical Component (-TC modifier) of a procedure (e.g., radiology)'],
      ['RVU', 'Relative Value Unit — components in the CMS PFS formula: work, practice expense (PE), malpractice (MP)'],
      ['U6 modifier', 'NYS-specific modifier indicating an after-hours service; multiplies the APG payment by 1.5×'],
      ['UB-04 / CMS-1500', 'Standard claim form layouts: UB-04 for institutional, CMS-1500 for professional'],
      ['VS Publish wizard', 'Visual Studio\'s built-in deploy-to-Azure tool — generates a publish profile (.pubxml) and pushes a ZIP package'],
    ],
    [2200, 7160]
  ),
  spacer(),
  h2('11.2 Key file locations'),
  makeTable(
    ['Path', 'What\'s there'],
    [
      ['APGAnalyzer/Program.cs', 'Composition root; service registrations; HTTP pipeline; RoleSeeder boot'],
      ['APGAnalyzer/Controllers/', 'MVC controllers — one per feature area'],
      ['APGAnalyzer/Views/', 'Razor views — folder per controller, plus Shared/'],
      ['APGAnalyzer/Models/', 'View models, filter models, domain DTOs'],
      ['APGAnalyzer/Models/Domain/', 'EF Core entities — one class per table'],
      ['APGAnalyzer/Models/Engine/', 'Engine input/output DTOs (ParsedClaimDto, APGResult, APGLineResult)'],
      ['APGAnalyzer/Services/', 'Business services — engine, parsers, exporters, loaders'],
      ['APGAnalyzer/Services/Edi/', 'EDI parsers + EdiFileTypeDetector'],
      ['APGAnalyzer/Services/Cms/', 'CmsRateService and supporting types'],
      ['APGAnalyzer/Data/Migrations/', 'EF Core migrations (chronological)'],
      ['APGAnalyzer/wwwroot/css/pmtac-theme.css', 'PMTAC custom theme overrides'],
      ['APGAnalyzer/wwwroot/images/pmtac-logo.png', 'Brand logo (38px tall, transparent PNG)'],
      ['APGAnalyzer/Properties/PublishProfiles/', 'Saved Publish wizard profiles (per environment)'],
      ['samples/paired_835i.edi, paired_837i.edi', 'Synthetic test fixtures for the linker pathway'],
      ['docs/AZURE_SQL_SCHEMA.sql', 'Standalone T-SQL DDL of the entire production schema (for IT review)'],
      ['docs/APGAnalyzer_Technical_Documentation.docx', 'This document'],
    ],
    [4000, 5360]
  ),
  spacer(),
  h2('11.3 Standard error references'),
  makeTable(
    ['Symptom', 'Most likely cause', 'Fix'],
    [
      ['HTTP 500.30 ASP.NET Core app failed to start', 'DB connection failing during RoleSeeder', 'Wake DB in Portal, then restart App Service'],
      ['Migration error 40613 "database not currently available"', 'SQL Free tier auto-paused', 'Open DB overview to wake; retry migration'],
      ['Migration error 40615 "client IP not allowed"', 'Your laptop IP not in SQL firewall', 'Add IP via SQL Server → Networking → Firewall rules'],
      ['EF "second operation on this context" exception', 'Parallel queries on shared DbContext', 'Make queries sequential or inject DbContextFactory'],
      ['HTTP 500.30 immediately after upload', 'F1 OOM during parse + re-pricing', 'Restart App Service; consider scaling to B1'],
      ['Calculator "CMS catalog unreachable" banner', 'Outbound HTTPS to pfs.data.cms.gov blocked', 'Verify NSG / firewall rules; cached values still serve'],
      ['F1 quota exceeded after heavy use', '60 min/day CPU cap hit', 'Wait until midnight UTC, or scale to B1'],
      ['App responsive but stuck on old data', 'In-process cache (CMS, locality)', 'Settings → Refresh CMS fee schedule cache; or restart App Service'],
    ],
    [3200, 3200, 2960]
  ),
  spacer(),
  h2('11.4 Useful Azure CLI commands'),
  ...code(
    '# Wake the SQL database manually (forces resume from auto-pause)\n' +
    'az sql db show \\\n' +
    '  --resource-group apg-analyzer-beta \\\n' +
    '  --server apg-analyzer-sql-pmtac \\\n' +
    '  --name APGAnalyzer\n' +
    '\n' +
    '# Restart the App Service\n' +
    'az webapp restart \\\n' +
    '  --resource-group apg-analyzer-beta \\\n' +
    '  --name apg-analyzer-beta-pmtac\n' +
    '\n' +
    '# Tail live application logs\n' +
    'az webapp log tail \\\n' +
    '  --resource-group apg-analyzer-beta \\\n' +
    '  --name apg-analyzer-beta-pmtac\n' +
    '\n' +
    '# Scale up to B1 (production-grade)\n' +
    'az appservice plan update \\\n' +
    '  --resource-group apg-analyzer-beta \\\n' +
    '  --name apg-analyzer-plan \\\n' +
    '  --sku B1\n' +
    '\n' +
    '# Apply EF migrations from local machine to Azure SQL\n' +
    '$conn = Get-Clipboard\n' +
    'dotnet ef database update --connection $conn',
    { lang: 'PowerShell / Azure CLI' }
  ),
  h2('11.5 Document revision history'),
  makeTable(
    ['Version', 'Date', 'Author', 'Notes'],
    [
      ['1.0', 'May 2026', 'Project documentation', 'Initial release covering V1 + Azure beta deployment'],
    ],
    [1500, 1500, 2500, 3860]
  )
);

// Final spacer
children.push(new Paragraph({ children: [new TextRun(' ')] }));

// ============================================================================
// Build the document
// ============================================================================

const doc = new Document({
  creator: 'PMTAC Engineering',
  title: 'APG Rate Analyzer — Technical Documentation',
  description: 'Full architecture, build, and Azure deployment guide for the APG Rate Analyzer (.NET 10 MVC).',
  styles: {
    default: {
      document: { run: { font: 'Calibri', size: 22 } }, // 11pt body
    },
    paragraphStyles: [
      {
        id: 'Heading1',
        name: 'Heading 1',
        basedOn: 'Normal',
        next: 'Normal',
        quickFormat: true,
        run: { size: 36, bold: true, font: 'Calibri', color: PMTAC_PURPLE },
        paragraph: {
          spacing: { before: 480, after: 240 },
          outlineLevel: 0,
          border: {
            bottom: { color: PMTAC_PURPLE, style: BorderStyle.SINGLE, size: 12, space: 4 },
          },
        },
      },
      {
        id: 'Heading2',
        name: 'Heading 2',
        basedOn: 'Normal',
        next: 'Normal',
        quickFormat: true,
        run: { size: 28, bold: true, font: 'Calibri', color: '333333' },
        paragraph: {
          spacing: { before: 320, after: 160 },
          outlineLevel: 1,
        },
      },
      {
        id: 'Heading3',
        name: 'Heading 3',
        basedOn: 'Normal',
        next: 'Normal',
        quickFormat: true,
        run: { size: 24, bold: true, font: 'Calibri', color: '555555' },
        paragraph: {
          spacing: { before: 240, after: 120 },
          outlineLevel: 2,
        },
      },
    ],
  },
  numbering: {
    config: [
      {
        reference: 'bullets',
        levels: [
          {
            level: 0,
            format: LevelFormat.BULLET,
            text: '•',
            alignment: AlignmentType.LEFT,
            style: { paragraph: { indent: { left: 720, hanging: 360 } } },
          },
          {
            level: 1,
            format: LevelFormat.BULLET,
            text: '◦',
            alignment: AlignmentType.LEFT,
            style: { paragraph: { indent: { left: 1440, hanging: 360 } } },
          },
        ],
      },
      {
        reference: 'numbers',
        levels: [
          {
            level: 0,
            format: LevelFormat.DECIMAL,
            text: '%1.',
            alignment: AlignmentType.LEFT,
            style: { paragraph: { indent: { left: 720, hanging: 360 } } },
          },
        ],
      },
    ],
  },
  sections: [
    {
      properties: {
        page: {
          size: { width: 12240, height: 15840 }, // US Letter
          margin: { top: 1440, right: 1440, bottom: 1440, left: 1440 }, // 1" all around
        },
      },
      headers: {
        default: new Header({
          children: [
            new Paragraph({
              alignment: AlignmentType.RIGHT,
              children: [
                new TextRun({
                  text: 'APG Rate Analyzer — Technical Documentation',
                  size: 18,
                  color: '888888',
                  italics: true,
                }),
              ],
              border: {
                bottom: { color: 'CCCCCC', style: BorderStyle.SINGLE, size: 4, space: 4 },
              },
            }),
          ],
        }),
      },
      footers: {
        default: new Footer({
          children: [
            new Paragraph({
              tabStops: [
                { type: TabStopType.RIGHT, position: 9360 },
              ],
              children: [
                new TextRun({ text: '© 2026 PMTAC', size: 18, color: '888888' }),
                new TextRun({
                  text: '\tPage ',
                  size: 18,
                  color: '888888',
                }),
                new TextRun({
                  children: [PageNumber.CURRENT],
                  size: 18,
                  color: '888888',
                }),
              ],
              border: {
                top: { color: 'CCCCCC', style: BorderStyle.SINGLE, size: 4, space: 4 },
              },
            }),
          ],
        }),
      },
      children,
    },
  ],
});

// Output
const outputPath = path.join(__dirname, 'APGAnalyzer_Technical_Documentation.docx');
Packer.toBuffer(doc).then((buffer) => {
  fs.writeFileSync(outputPath, buffer);
  const stat = fs.statSync(outputPath);
  console.log(`✓ Document generated: ${outputPath}`);
  console.log(`  Size: ${(stat.size / 1024).toFixed(1)} KB`);
});
