using System.Text;
using System.Text.Json;
using APGAnalyzer.Data;
using APGAnalyzer.Models.Domain;
using APGAnalyzer.Services.Edi;
using Microsoft.EntityFrameworkCore;
using EngineDto = APGAnalyzer.Models.Engine;
using ParserDto = APGAnalyzer.Services.Edi;

namespace APGAnalyzer.Services;

public interface IClaimUploadService
{
    Task<ClaimUploadResult> ParseAndStoreAsync(
        byte[] fileBytes,
        string fileName,
        string fileType,         // '835I' for now; '835P' / '837' coming in Session B
        CancellationToken ct = default);
}

public class ClaimUploadResult
{
    public string FileName { get; set; } = "";
    public string FileType { get; set; } = "";
    public string FileId { get; set; } = "";
    public int ClaimsParsed { get; set; }
    public int ClaimsSaved { get; set; }
    public int ApgResultsComputed { get; set; }
    public List<string> Warnings { get; set; } = new();
    public TimeSpan Elapsed { get; set; }
}

/// <summary>
/// Glue between the EDI parser, the database, and the APG engine.
/// One call: bytes in, parsed+saved+priced claims out.
/// </summary>
public class ClaimUploadService(
    ApplicationDbContext db,
    IApgEngine engine,
    ILogger<ClaimUploadService> log) : IClaimUploadService
{
    public async Task<ClaimUploadResult> ParseAndStoreAsync(
        byte[] fileBytes,
        string fileName,
        string fileType,
        CancellationToken ct = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = new ClaimUploadResult
        {
            FileName = fileName,
            FileType = fileType,
            FileId = $"UP-{DateTime.UtcNow:yyyyMMddHHmmssfff}",
        };

        // 1. Parse the EDI text
        var text = Encoding.UTF8.GetString(fileBytes);
        var parsed = fileType.Equals("835I", StringComparison.OrdinalIgnoreCase)
            ? new Edi835IParser(text).Parse()
            : throw new NotSupportedException($"File type '{fileType}' isn't supported in this build (Session A only handles 835I).");

        result.ClaimsParsed = parsed.Claims.Count;
        log.LogInformation("Parsed {Count} claims from {File}", parsed.Claims.Count, fileName);

        // 2. Resolve the active provider once (engine needs it for every claim)
        var provider = await db.ProviderConfigs
            .Where(x => x.IsActive)
            .OrderByDescending(x => x.UpdatedAt)
            .FirstOrDefaultAsync(ct);
        if (provider is null)
        {
            result.Warnings.Add(
                "No active provider configured — claims saved without APG calculation. "
              + "Set up Provider Config and re-process from the Claims list.");
        }

        // 3. Insert each parsed claim, then run the engine, then store result
        foreach (var pc in parsed.Claims)
        {
            var entity = ToEntity(pc, result.FileId, parsed);
            db.ParsedClaims.Add(entity);
            await db.SaveChangesAsync(ct);   // need entity.Id for FK on lines/adjustments + apg_result
            result.ClaimsSaved++;

            // Run engine if we have a provider; cache the result on the claim row
            if (provider is not null && entity.DateOfService.HasValue)
            {
                try
                {
                    var dto = ToEngineDto(entity);
                    var apgResult = await engine.CalculateAsync(dto, provider, ct);
                    db.ApgResults.Add(new ApgResultRecord
                    {
                        ClaimIdFk = entity.Id,
                        CorrectApgPayment = apgResult.CorrectApgPayment,
                        ActualPaid = apgResult.ActualPaid,
                        Variance = apgResult.Variance,
                        CompressionPct = apgResult.CompressionPct,
                        Underpaid = apgResult.Underpaid,
                        Overpaid = apgResult.Overpaid,
                        BaseRateApplied = apgResult.BaseRateApplied,
                        PeerGroup = apgResult.PeerGroup,
                        Region = apgResult.Region,
                        DiscountingApplied = apgResult.DiscountingApplied,
                        U6Applied = apgResult.U6Applied,
                        CapitalApplied = apgResult.CapitalApplied,
                        LineDetailsJson = JsonSerializer.Serialize(apgResult.LineDetails),
                        CalculatedAt = DateTime.UtcNow,
                    });
                    await db.SaveChangesAsync(ct);
                    result.ApgResultsComputed++;
                }
                catch (Exception ex)
                {
                    log.LogWarning(ex, "APG calc failed for claim {ClaimId}", entity.ClaimId);
                    result.Warnings.Add($"Claim {entity.ClaimId}: APG calc failed — {ex.Message}");
                }
            }

            // Don't let the change tracker accumulate every claim's graph
            db.ChangeTracker.Clear();
        }

        result.Elapsed = sw.Elapsed;
        log.LogInformation(
            "Upload {File}: {Saved}/{Parsed} claims saved, {Apg} APG calcs, {Elapsed:F1}s",
            fileName, result.ClaimsSaved, result.ClaimsParsed,
            result.ApgResultsComputed, result.Elapsed.TotalSeconds);
        return result;
    }

    /// <summary>Map the parser's claim DTO → DB entity (with children).</summary>
    private static ParsedClaim ToEntity(ParserDto.ParsedClaimDto src, string fileId, Parsed835IResult envelope)
    {
        var claim = new ParsedClaim
        {
            FileId = fileId,
            FileType = src.FileType,
            PayerName = src.PayerName ?? envelope.PayerName,
            PayerId = src.PayerId ?? envelope.PayerId,
            ProviderNpi = src.ProviderNpi,
            ProviderName = src.ProviderName,
            ClaimId = src.ClaimId,
            PatientName = src.PatientName,
            PatientId = src.PatientId,
            DateOfService = src.DateOfService,
            ClaimStatus = src.ClaimStatus,
            BilledAmount = src.BilledAmount,
            AllowedAmount = src.AllowedAmount,
            PaidAmount = src.PaidAmount,
            PatientResponsibility = src.PatientResponsibility,
            ClaimFilingIndicator = src.ClaimFilingIndicator,
            PrincipalDiagnosis = DxCodeNormalizer.Normalize(src.PrincipalDiagnosis),
            OtherDiagnosesJson = src.OtherDiagnoses.Count == 0
                ? null
                : JsonSerializer.Serialize(src.OtherDiagnoses),
        };
        foreach (var sl in src.ServiceLines)
        {
            claim.ServiceLines.Add(new ParsedServiceLine
            {
                LineSeq = sl.LineSeq,
                ProcedureCode = sl.ProcedureCode,
                ModifiersJson = sl.Modifiers.Count == 0 ? null : JsonSerializer.Serialize(sl.Modifiers),
                RevenueCode = sl.RevenueCode,
                BilledAmount = sl.BilledAmount,
                AllowedAmount = sl.AllowedAmount,
                PaidAmount = sl.PaidAmount,
                Units = sl.Units,
                DateOfService = sl.DateOfService ?? src.DateOfService,
            });
        }
        foreach (var adj in src.Adjustments)
        {
            claim.Adjustments.Add(new ClaimAdjustment
            {
                LineSeq = adj.LineSeq,
                GroupCode = adj.GroupCode,
                ReasonCode = adj.ReasonCode,
                Amount = adj.Amount,
                Quantity = adj.Quantity,
            });
        }
        foreach (var sl in src.ServiceLines)
        {
            foreach (var adj in sl.Adjustments)
            {
                claim.Adjustments.Add(new ClaimAdjustment
                {
                    LineSeq = adj.LineSeq ?? sl.LineSeq,
                    GroupCode = adj.GroupCode,
                    ReasonCode = adj.ReasonCode,
                    Amount = adj.Amount,
                    Quantity = adj.Quantity,
                });
            }
        }
        return claim;
    }

    /// <summary>Build the engine-input DTO from a saved entity.</summary>
    public static EngineDto.ParsedClaimDto ToEngineDto(ParsedClaim entity)
    {
        return new EngineDto.ParsedClaimDto
        {
            ClaimId = entity.ClaimId,
            DateOfService = entity.DateOfService,
            BilledAmount = entity.BilledAmount,
            PaidAmount = entity.PaidAmount,
            AllowedAmount = entity.AllowedAmount,
            PrincipalDiagnosis = entity.PrincipalDiagnosis,
            OtherDiagnoses = string.IsNullOrEmpty(entity.OtherDiagnosesJson)
                ? new List<string>()
                : JsonSerializer.Deserialize<List<string>>(entity.OtherDiagnosesJson) ?? new(),
            ServiceLines = entity.ServiceLines.Select(sl => new EngineDto.ServiceLineDto
            {
                LineSeq = sl.LineSeq,
                ProcedureCode = sl.ProcedureCode,
                Modifiers = string.IsNullOrEmpty(sl.ModifiersJson)
                    ? new List<string>()
                    : JsonSerializer.Deserialize<List<string>>(sl.ModifiersJson) ?? new(),
                RevenueCode = sl.RevenueCode,
                BilledAmount = sl.BilledAmount,
                AllowedAmount = sl.AllowedAmount,
                PaidAmount = sl.PaidAmount,
                Units = sl.Units,
                DateOfService = sl.DateOfService,
            }).ToList(),
        };
    }
}
