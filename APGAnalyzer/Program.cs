using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using APGAnalyzer.Data;
using APGAnalyzer.Services;

var builder = WebApplication.CreateBuilder(args);

// Application Insights — wires up the "security camera" so we can see errors,
// slow pages, and usage patterns in the Azure portal dashboard.
// The connection string is read from appsettings.json in dev and from the
// APPLICATIONINSIGHTS_CONNECTION_STRING environment variable in Azure (set
// automatically when App Insights is enabled via the App Service portal blade).
builder.Services.AddApplicationInsightsTelemetry();

// Add services to the container.
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
// Transient retry policy: Azure SQL serverless auto-pauses after idle and
// takes 30-60s to resume. The first connection attempt during wake-up hits
// error 40613 ("database not currently available") and similar transient
// failures. EnableRetryOnFailure retries with exponential backoff so the
// caller doesn't see the wake-up window — just a slower-than-usual first
// page load. Without this, every cold start trips RoleSeeder and the app
// fails to start with HTTP 500.30. Microsoft's recommended default for
// any Azure SQL workload, especially serverless.
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString, sql =>
        sql.EnableRetryOnFailure(
            maxRetryCount: 6,
            maxRetryDelay: TimeSpan.FromSeconds(30),
            errorNumbersToAdd: null)));
builder.Services.AddDatabaseDeveloperPageExceptionFilter();

// Identity defaults — RequireConfirmedAccount=false during early development
// so the very first registration can sign in immediately. Flip to true once
// you wire up an email sender (SendGrid / Microsoft Graph / SMTP) before
// anything goes near production.
builder.Services.AddDefaultIdentity<IdentityUser>(options =>
    {
        options.SignIn.RequireConfirmedAccount = false;
        options.Password.RequireNonAlphanumeric = false;   // simpler dev passwords
    })
    .AddRoles<IdentityRole>()                              // role-based authorization
    .AddEntityFrameworkStores<ApplicationDbContext>();
builder.Services.AddControllersWithViews();

// Account provisioning is admin-only — block the default Register page so
// strangers (and non-admins) can't create accounts. Real account creation
// happens through /Users/Create.
//
// Exception: on a fresh deploy with no users yet, the AdminOrBootstrap policy
// lets the very first registration through so we can bootstrap the first
// admin account. RoleSeeder auto-promotes that user on the next app start.
builder.Services.AddRazorPages(options =>
{
    options.Conventions.AuthorizeAreaPage("Identity", "/Account/Register", "AdminOnly");
    options.Conventions.AuthorizeAreaPage("Identity", "/Account/RegisterConfirmation", "AdminOnly");
});
builder.Services.AddScoped<Microsoft.AspNetCore.Authorization.IAuthorizationHandler,
                           AdminOrBootstrapHandler>();
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", p => p.AddRequirements(new AdminOrBootstrapRequirement()));
});

// Reference-data loaders (Phase 2). Scoped lifetime — each upload runs in
// its own request and gets a fresh DbContext.
builder.Services.AddScoped<ICrosswalkLoader, CrosswalkLoader>();
builder.Services.AddScoped<IWeightsHistoryLoader, WeightsHistoryLoader>();
builder.Services.AddScoped<ProviderCountyLoader>();
builder.Services.AddScoped<IPmtacFeeCalculatorLoader, PmtacFeeCalculatorLoader>();
builder.Services.AddScoped<IDtcBaseRatesLoader, DtcBaseRatesLoader>();
builder.Services.AddScoped<IMasterResetService, MasterResetService>();

// APG calculation engine (Phase 3).
builder.Services.AddScoped<IApgEngine, ApgEngine>();

// EDI upload pipeline (Phase 4).
builder.Services.AddScoped<IClaimUploadService, ClaimUploadService>();
builder.Services.AddScoped<IClaimLinkerService, ClaimLinkerService>();

// CMS Medicare PFS rate engine. Requires outbound HTTPS to pfs.data.cms.gov.
// IHttpClientFactory provides connection pooling + handler lifecycle, so
// we get DNS refresh, transient failure tolerance, and minimal socket
// exhaustion — all the things a hand-rolled HttpClient gets wrong.
builder.Services.AddHttpClient<APGAnalyzer.Services.Cms.ICmsRateService,
                               APGAnalyzer.Services.Cms.CmsRateService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
    client.DefaultRequestHeaders.Accept.Add(
        new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
    client.DefaultRequestHeaders.UserAgent.ParseAdd("APGAnalyzer/1.0 (PMTAC)");
});

// Per-user data isolation: every read query filters by OwnerUserId,
// every write stamps the current user. Admins can override the read
// scope through a session-cookie "View as user" dropdown.
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserContext, CurrentUserContext>();
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(o =>
{
    o.IdleTimeout = TimeSpan.FromHours(8);
    o.Cookie.HttpOnly = true;
    o.Cookie.IsEssential = true;
});

// Analytics + Excel/PDF exports (Phase 5).
builder.Services.AddSingleton<ExportService>();
builder.Services.AddScoped<IAnalyticsService, AnalyticsService>();

// ECW Practice Audit module.
builder.Services.AddScoped<APGAnalyzer.Services.EcwAudit.IEcwAuditUploadService,
                           APGAnalyzer.Services.EcwAudit.EcwAuditUploadService>();
builder.Services.AddScoped<APGAnalyzer.Services.EcwAudit.IEcwAuditEngine,
                           APGAnalyzer.Services.EcwAudit.EcwAuditEngine>();
builder.Services.AddScoped<APGAnalyzer.Services.EcwAudit.IEcwAuditExportService,
                           APGAnalyzer.Services.EcwAudit.EcwAuditExportService>();

// QuestPDF community-license declaration. Must be set before any
// document is generated. Free for internal-use scenarios like ours.
QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

// Allow uploads up to 50 MB so the largest reference workbook
// (eMedNY APG Crosswalk ≈ 5 MB; PMTAC Fee Calculator ≈ 5 MB) goes through
// comfortably with headroom.
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(o =>
{
    o.MultipartBodyLengthLimit = 50 * 1024 * 1024;
});

var app = builder.Build();

// Seed Identity roles + auto-promote the first registered user to admin
// so the existing admin@test.com keeps Settings access without manual
// intervention. Runs once on startup.
//
// Wrapped in try/catch so a transient DB failure at startup (Azure SQL
// Serverless auto-pause that hasn't finished resuming yet) doesn't crash
// the app with HTTP 500.30. The seeder runs again on the next app start
// (e.g., after Azure recycles the worker), and any HTTP request that hits
// a sleeping DB will trigger wake-up gracefully thanks to
// EnableRetryOnFailure on the DbContext.
try
{
    await RoleSeeder.SeedAsync(app.Services);
}
catch (Exception ex)
{
    var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");
    logger.LogWarning(ex,
        "RoleSeeder.SeedAsync failed at startup — likely Azure SQL is paused/resuming. "
      + "App will start anyway; the seeder will retry on the next worker recycle, and "
      + "the first authenticated request will wake the database. Re-deploys / restarts "
      + "should resolve any lingering 'no admin role' state automatically.");
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();

app.UseSession();
app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.MapRazorPages()
   .WithStaticAssets();

app.Run();
