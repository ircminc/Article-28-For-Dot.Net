using APGAnalyzer.Models;
using APGAnalyzer.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace APGAnalyzer.Controllers;

[Authorize]
public class AnalyticsController(
    IAnalyticsService analytics,
    ExportService exporter) : Controller
{
    public async Task<IActionResult> Index(AnalyticsFilters? filters, CancellationToken ct)
    {
        filters = ApplyDefaults(filters);
        var vm = await analytics.ComputeAsync(filters, ct);
        return View(vm);
    }

    /// <summary>
    /// GET /Analytics/ExportXlsx — multi-sheet workbook of every panel
    /// with the same filters as the on-screen view.
    /// </summary>
    public async Task<IActionResult> ExportXlsx(AnalyticsFilters? filters, CancellationToken ct)
    {
        filters = ApplyDefaults(filters);
        var vm = await analytics.ComputeAsync(filters, ct);
        var bytes = exporter.BuildAnalyticsXlsx(vm);
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmm");
        return File(bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"analytics-{stamp}.xlsx");
    }

    /// <summary>
    /// Default filter to last 12 months when nothing is supplied — keeps
    /// the page snappy on large databases and matches the most-common
    /// "what happened recently?" use case.
    /// </summary>
    private static AnalyticsFilters ApplyDefaults(AnalyticsFilters? filters)
    {
        filters ??= new AnalyticsFilters();
        if (filters.IsEmpty)
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            filters.DateFrom = today.AddYears(-1);
            filters.DateTo = today;
        }
        return filters;
    }
}
