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
            // Run all the row-count queries in parallel to keep the page snappy.
            // EF Core's CountAsync issues a single SELECT COUNT(*) per call.
            var tHcpcs  = db.HcpcsToEapg.CountAsync();
            var tIcd10  = db.Icd10ToEapg.CountAsync();
            var tApgW   = db.ApgWeights.CountAsync();
            var tBase   = db.ApgBaseRates.CountAsync();
            var tCounty = db.ProviderCounties.CountAsync();
            var tPxW    = db.PxBasedWeights.CountAsync();
            var tFee    = db.FeeSchedule.CountAsync();
            var tUsers  = db.Users.CountAsync();

            await Task.WhenAll(tHcpcs, tIcd10, tApgW, tBase, tCounty, tPxW, tFee, tUsers);

            vm.DbConnected        = true;
            vm.HcpcsRows          = await tHcpcs;
            vm.Icd10Rows          = await tIcd10;
            vm.ApgWeightRows      = await tApgW;
            vm.ApgBaseRateRows    = await tBase;
            vm.ProviderCountyRows = await tCounty;
            vm.PxBasedWeightRows  = await tPxW;
            vm.FeeScheduleRows    = await tFee;
            vm.IdentityUserRows   = await tUsers;
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
