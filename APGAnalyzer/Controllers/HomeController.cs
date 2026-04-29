using System.Diagnostics;
using APGAnalyzer.Data;
using APGAnalyzer.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace APGAnalyzer.Controllers;

public class HomeController(ApplicationDbContext db, ILogger<HomeController> log) : Controller
{
    public async Task<IActionResult> Index()
    {
        var vm = new HomeIndexViewModel();
        try
        {
            // EF Core DbContext is NOT thread-safe — only one query in flight
            // per instance at a time. Each CountAsync below issues a single
            // SELECT COUNT(*); these are sub-millisecond each so doing them
            // sequentially is plenty fast (the alternative — Task.WhenAll —
            // throws "A second operation was started on this context instance").
            vm.HcpcsRows          = await db.HcpcsToEapg.CountAsync();
            vm.Icd10Rows          = await db.Icd10ToEapg.CountAsync();
            vm.ApgWeightRows      = await db.ApgWeights.CountAsync();
            vm.ApgBaseRateRows    = await db.ApgBaseRates.CountAsync();
            vm.ProviderCountyRows = await db.ProviderCounties.CountAsync();
            vm.PxBasedWeightRows  = await db.PxBasedWeights.CountAsync();
            vm.FeeScheduleRows    = await db.FeeSchedule.CountAsync();
            vm.IdentityUserRows   = await db.Users.CountAsync();
            vm.DbConnected        = true;
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Home page failed to query reference tables");
            vm.DbConnected = false;
            vm.DbError = ex.Message;
        }
        return View(vm);
    }

    public IActionResult Privacy() => View();

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
        => View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
}
