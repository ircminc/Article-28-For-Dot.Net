using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using APGAnalyzer.Data;
using APGAnalyzer.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString));
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

// Analytics + Excel/PDF exports (Phase 5).
builder.Services.AddSingleton<ExportService>();

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
await RoleSeeder.SeedAsync(app.Services);

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
