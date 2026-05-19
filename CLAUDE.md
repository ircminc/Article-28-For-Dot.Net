# APG 835/837 Rate Analyzer — .NET Edition

## What This Project Is
.NET 10 / ASP.NET Core MVC sister of the Python APG Calculator (`ircminc/Article-28`). Implements the same NYS DOH Article 28 / APG compliance + 835/837 EDI rate-analysis domain logic, built to deploy to Azure App Service with SQL Server as the backing store.

**GitHub repo:** https://github.com/ircminc/Article-28-For-Dot.Net.git  
**Active branch:** `main`  
**Working directory:** `H:\Working\eProjects\APG Calculator DotNet\`  
**Solution file:** `APGAnalyzer.slnx`

---

## Stack
| Layer | Choice |
|---|---|
| Runtime | .NET 10 (LTS — supported through Nov 2028) |
| Web framework | ASP.NET Core MVC (server-rendered Razor views — per IT spec) |
| Data access | Entity Framework Core 10 + SQL Server provider, code-first migrations |
| Database (dev) | SQL Server LocalDB (`Trusted_Connection=True`) |
| Database (prod) | Azure SQL Database |
| Auth | ASP.NET Core Identity (cookie auth, username + password) |
| UI | Bootstrap 5 + jQuery 3 |
| Tests | xUnit (added in a later phase) |

---

## How to Run (Local)
```bash
# Apply migrations to LocalDB
dotnet ef database update --project APGAnalyzer

# Run
dotnet run --project APGAnalyzer
# → https://localhost:5001
```
Or open `APGAnalyzer.slnx` in Visual Studio and press **F5**.

---

## Project Structure
```
APGAnalyzer.slnx
APGAnalyzer/
├── Program.cs                   — service registrations + middleware pipeline
├── appsettings.json             — prod connection string (overridden by Azure Key Vault)
├── appsettings.Development.json — dev overrides (LocalDB)
├── appsettings.Production.json  — Azure overrides
├── Controllers/                 — MVC controllers
├── Models/
│   ├── Domain/                  — EF entities (one per reference table)
│   └── ViewModels/
├── Data/
│   ├── ApplicationDbContext.cs  — IdentityDbContext + reference DbSets
│   └── Migrations/
├── Services/                    — Business logic layer
├── Views/                       — Razor views (Bootstrap 5 chrome)
├── Areas/Identity/              — Register / Login scaffolds
└── wwwroot/                     — Static assets
docs/
├── DATABASE_SCHEMA.txt          — Full table-by-table schema reference
├── AZURE_SQL_SCHEMA.sql         — Azure SQL DDL
├── TECH_STACK_PYTHON_REFERENCE.md — Python sister project comparison
└── APGAnalyzer_Technical_Documentation.docx
samples/                         — Paired 835I + 837I EDI test fixtures
```

---

## Reference Data (NYS DOH Files)
Source files for seeding APG reference tables live at:
`H:\Working\eProjects\APG Calculator DotNet NYS Files\`

| File | Purpose |
|---|---|
| `Updated APG Fee Calculator 04292026.xlsx` | Primary NYS DOH APG workbook |
| `APGcrosswalk04272026 3.xlsx` | HCPCS-to-EAPG crosswalk |
| `dtc_base_rates_inv 3.xls` | DTC base rates |
| `history_and_fee_schedule 3.xls` | Fee schedule history |
| `v3_delta_plan.md` | Full v3 scope + gap analysis vs shipped phases |

---

## Azure Deployment
- **App Service URL:** `https://apg-analyzer-beta-pmtac-d0dmanfygtded5gx.centralus-01.azurewebsites.net`
- **SQL Server:** `apg-analyzer-sql-pmtac.database.windows.net` — DB: `APGAnalyzer`
- Connection string lives in `appsettings.Production.json` and Azure Key Vault — do not hardcode credentials in source.
- GitHub Actions workflow in `.github/workflows/` — wired for Azure App Service deploy.

---

## Phase Status
| Phase | Status |
|---|---|
| 1 — Skeleton: solution + DbContext + Identity + home page | ✅ Done |
| 2 — Reference data loaders (Crosswalk, Weights+Fees, DTC rates) | ✅ Done |
| 3 — APG Engine (priority ladder, visit-purpose override, packaging, discounting) | ✅ Done |
| 4 — EDI parsers (835I, 835P, 837I/P) + claim linking + Upload/Claims UI | ✅ Done |
| 5 — Analytics dashboard + Excel/PDF exports + CMS-1500/UB-04 PDF forms | ✅ Done |
| 6 — Azure App Service deployment + parity verification with Python | ⏳ Next |

---

## Relationship to Python Project
This is the **.NET 10 sister** of `H:\Working\eProjects\APG Calculator` (Python/FastAPI). Both implement the same APG domain logic. The Python build is ahead on analytics modules (see `v3_delta_plan.md` for full gap analysis). When parity features are needed, cross-reference the Python implementation in `ircminc/Article-28`.

---

## Git / Push Policy
**Never push to `ircminc/Article-28-For-Dot.Net` without explicit per-push confirmation from the user.** Ask before every push.
