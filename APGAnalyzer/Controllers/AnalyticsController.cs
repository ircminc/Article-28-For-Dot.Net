using APGAnalyzer.Data;
using APGAnalyzer.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace APGAnalyzer.Controllers;

[Authorize]
public class AnalyticsController(ApplicationDbContext db) : Controller
{
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var vm = new AnalyticsViewModel();

        // 1. Top-line summary — issued as separate aggregations to keep
        //    the SQL each one emits simple. EF Core composes them
        //    server-side so this is still 4 round-trips, not 4 full table
        //    scans.
        vm.TotalClaims          = await db.ParsedClaims.CountAsync(ct);
        vm.ClaimsWithApgResult  = await db.ApgResults.CountAsync(ct);
        vm.TotalBilled          = await db.ParsedClaims.SumAsync(c => (decimal?)c.BilledAmount, ct) ?? 0m;
        vm.TotalPaid            = await db.ParsedClaims.SumAsync(c => (decimal?)c.PaidAmount, ct)   ?? 0m;
        vm.TotalCorrectApg      = await db.ApgResults.SumAsync(a => (decimal?)a.CorrectApgPayment, ct) ?? 0m;
        vm.TotalVariance        = await db.ApgResults.SumAsync(a => (decimal?)a.Variance, ct) ?? 0m;

        // 2. Status counts. Single round trip with conditional sums.
        var counts = await db.ApgResults
            .GroupBy(a => 1)   // single group → one row
            .Select(g => new
            {
                Underpaid = g.Sum(a => a.Underpaid ? 1 : 0),
                Overpaid  = g.Sum(a => a.Overpaid ? 1 : 0),
                Match     = g.Sum(a => !a.Underpaid && !a.Overpaid ? 1 : 0),
            })
            .FirstOrDefaultAsync(ct);
        vm.Underpaid = counts?.Underpaid ?? 0;
        vm.Overpaid  = counts?.Overpaid ?? 0;
        vm.Match     = counts?.Match ?? 0;
        vm.Unpriced  = vm.TotalClaims - vm.ClaimsWithApgResult;

        // 3. File-type breakdown.
        var byType = await db.ParsedClaims
            .GroupBy(c => c.FileType)
            .Select(g => new FileTypeStat
            {
                FileType        = g.Key,
                Count           = g.Count(),
                TotalBilled     = g.Sum(c => c.BilledAmount),
                TotalPaid       = g.Sum(c => c.PaidAmount),
                TotalCorrectApg = g.Sum(c => c.ApgResult == null ? 0 : c.ApgResult.CorrectApgPayment),
                TotalVariance   = g.Sum(c => c.ApgResult == null ? 0 : c.ApgResult.Variance),
            })
            .OrderBy(s => s.FileType)
            .ToListAsync(ct);
        vm.ByFileType = byType;

        // 4. Top-10 lists.
        vm.TopUnderpaid = await db.ParsedClaims
            .Include(c => c.ApgResult)
            .Where(c => c.ApgResult != null && c.ApgResult.Underpaid)
            .OrderByDescending(c => c.ApgResult!.Variance)
            .Take(10)
            .Select(c => new TopVarianceRow
            {
                Id            = c.Id,
                ClaimId       = c.ClaimId,
                FileType      = c.FileType,
                PatientName   = c.PatientName,
                DateOfService = c.DateOfService,
                CorrectApg    = c.ApgResult!.CorrectApgPayment,
                Paid          = c.ApgResult.ActualPaid,
                Variance      = c.ApgResult.Variance,
            })
            .ToListAsync(ct);

        vm.TopOverpaid = await db.ParsedClaims
            .Include(c => c.ApgResult)
            .Where(c => c.ApgResult != null && c.ApgResult.Overpaid)
            .OrderBy(c => c.ApgResult!.Variance)   // most negative first
            .Take(10)
            .Select(c => new TopVarianceRow
            {
                Id            = c.Id,
                ClaimId       = c.ClaimId,
                FileType      = c.FileType,
                PatientName   = c.PatientName,
                DateOfService = c.DateOfService,
                CorrectApg    = c.ApgResult!.CorrectApgPayment,
                Paid          = c.ApgResult.ActualPaid,
                Variance      = c.ApgResult.Variance,
            })
            .ToListAsync(ct);

        return View(vm);
    }
}
