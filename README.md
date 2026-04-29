# APG 835/837 Rate Analyzer — .NET 10 build

NYS DOH Article 28 / APG (Ambulatory Patient Group) compliance + 835/837
EDI rate-analysis platform. **.NET 10 / ASP.NET Core MVC + Razor +
Bootstrap 5 + Entity Framework Core 10 + SQL Server (LocalDB in dev,
Azure SQL in production).**

This repository is the .NET 10 sister of the Python service at
[`ircminc/Article-28`](https://github.com/ircminc/Article-28). They
implement the same domain logic; this one is built to deploy to an
Azure App Service.

## Quick start

### Prerequisites

| Tool | Version | Notes |
|---|---|---|
| **.NET SDK** | 10.x | Download from <https://dotnet.microsoft.com/download/dotnet/10.0> |
| **Visual Studio 2022/2026** | latest | "ASP.NET and web development" workload |
| **SQL Server LocalDB** | any modern | Bundled with SQL Server Express; or install the standalone "SQL Server Express LocalDB" |

A single `dotnet --version` should print `10.0.x`. If it doesn't, your
PATH probably needs `C:\Program Files\dotnet` — fix once with:

```powershell
[Environment]::SetEnvironmentVariable(
    'Path',
    [Environment]::GetEnvironmentVariable('Path', 'User') + ';C:\Program Files\dotnet',
    'User'
)
```

### First-run setup

```bash
git clone https://github.com/ircminc/Article-28-For-Dot.Net.git
cd Article-28-For-Dot.Net

# Apply the database schema to LocalDB
dotnet ef database update --project APGAnalyzer

# Run the app
dotnet run --project APGAnalyzer
```

Browse to <https://localhost:5001> (or whatever port the console reports).
Click **Register** in the top-right, create a user, log in, and you should
see the home page report:

> **Database connected.** ASP.NET Core Identity + Entity Framework Core
> are both wired up and reachable.

…with seven row-count tiles all reading **0**. That's expected — the
reference uploaders come in later phases.

### Open in Visual Studio

1. Double-click `APGAnalyzer.slnx`
2. Press **F5** (or click the green ▶ button)
3. Browser opens at the dev URL automatically

VS will run `dotnet ef database update` for you the first time if you
have the EF Core tooling installed and the migration is pending.

## Project layout (monolithic — single MVC project per IT directive)

```
APGAnalyzer.slnx                 ← solution
APGAnalyzer/
├── APGAnalyzer.csproj
├── Program.cs                   ← service registrations + middleware pipeline
├── appsettings.json             ← prod connection string (overridden by Azure)
├── appsettings.Development.json ← dev overrides
├── Controllers/
│   └── HomeController.cs        ← DB row-count smoke test
├── Models/
│   ├── Domain/                  ← EF entities (one per reference table)
│   ├── HomeIndexViewModel.cs
│   └── ErrorViewModel.cs
├── Data/
│   ├── ApplicationDbContext.cs  ← IdentityDbContext + 7 reference DbSets
│   └── Migrations/
├── Views/
│   ├── Shared/_Layout.cshtml    ← Bootstrap 5 chrome
│   └── Home/Index.cshtml        ← row-count dashboard
├── Areas/Identity/              ← Register / Login / Account scaffolds
└── wwwroot/                     ← static assets (Bootstrap, jQuery, CSS, JS)
```

## Stack at a glance

| Layer | Choice | Why |
|---|---|---|
| Runtime | **.NET 10 (LTS)** | Supported through Nov 2028 |
| Web framework | **ASP.NET Core MVC** | Server-rendered Razor views per IT spec |
| Data | **Entity Framework Core 10** + SQL Server provider | Async, code-first migrations |
| Database (dev) | **SQL Server LocalDB** | Per-user, Trusted_Connection=True, no login hassle |
| Database (prod) | **Azure SQL Database** | Same engine, managed |
| Auth | **ASP.NET Core Identity** (cookie auth) | Username + password per requirements |
| UI | **Bootstrap 5** + **jQuery 3** | Standard MVC chrome |
| Tests | **xUnit** (added in a later phase) | |

See `docs/DATABASE_SCHEMA.txt` for the full table-by-table reference.

## Phase status

| Phase | Status |
|---|---|
| **1 — Skeleton** (you are here): solution + DbContext + Identity + home page | ✅ done |
| 2 — Reference data loaders (Crosswalk, Weights+Fees, DTC rates, PMTAC v2) | ⏳ next |
| 3 — APG Engine (priority ladder, visit-purpose override, packaging, discounting) | |
| 4 — EDI parsers (837/835I/835P) + Rate Calculator | |
| 5 — Analytics + Excel/PDF exports | |
| 6 — Azure App Service deployment scripts + parity verification with Python | |
