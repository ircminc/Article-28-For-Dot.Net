using APGAnalyzer.Data;
using APGAnalyzer.Models.Domain;
using Microsoft.EntityFrameworkCore;

namespace APGAnalyzer.Services.EcwAudit;

public interface IEcwAuditUploadService
{
    Task<EcwAuditBatch> UploadAsync(EcwUploadRequest request, string ownerUserId, CancellationToken ct = default);
    Task<List<EcwAuditBatch>> GetBatchesAsync(string ownerUserId, CancellationToken ct = default);
    Task<EcwAuditBatch?> GetBatchAsync(int id, CancellationToken ct = default);
    Task DeleteBatchAsync(int id, CancellationToken ct = default);
}

public class EcwUploadRequest
{
    public string PracticeName { get; set; } = "";
    public DateOnly AuditDate { get; set; }
    public string? Notes { get; set; }

    // Optional file streams — only the files the user uploads are parsed
    public Stream? File361 { get; set; }   // 361.05 Financial at Claim Level
    public Stream? File371 { get; set; }   // 371.05 Financial at CPT Level
    public Stream? File123 { get; set; }   // 123.06 Claim Submission
    public Stream? File1310 { get; set; }  // 13.10  Billing Lag
    public Stream? File3108 { get; set; }  // 31.08  Patient Aging
    public Stream? File3109Primary { get; set; }   // 31.09 Primary
    public Stream? File3109Secondary { get; set; } // 31.09 Secondary
}

public class EcwAuditUploadService(ApplicationDbContext db, ILogger<EcwAuditUploadService> logger)
    : IEcwAuditUploadService
{
    public async Task<EcwAuditBatch> UploadAsync(EcwUploadRequest req, string ownerUserId, CancellationToken ct = default)
    {
        var batch = new EcwAuditBatch
        {
            PracticeName = req.PracticeName,
            AuditDate    = req.AuditDate,
            OwnerUserId  = ownerUserId,
            Notes        = req.Notes,
            UploadedAt   = DateTime.UtcNow,
        };
        db.EcwAuditBatches.Add(batch);
        await db.SaveChangesAsync(ct); // get Id before bulk inserts

        await ParseAndSave(req, batch.Id, ct);
        return batch;
    }

    private async Task ParseAndSave(EcwUploadRequest req, int batchId, CancellationToken ct)
    {
        if (req.File361 is not null)
        {
            logger.LogInformation("Parsing 361.05 for batch {Id}", batchId);
            var rows = EcwParser361.Parse(req.File361, batchId);
            db.EcwClaimFinancials.AddRange(rows);
            await db.SaveChangesAsync(ct);
        }

        if (req.File371 is not null)
        {
            logger.LogInformation("Parsing 371.05 for batch {Id}", batchId);
            var rows = EcwParser371.Parse(req.File371, batchId);
            db.EcwCptLines.AddRange(rows);
            await db.SaveChangesAsync(ct);
        }

        if (req.File123 is not null)
        {
            logger.LogInformation("Parsing 123.06 for batch {Id}", batchId);
            var rows = EcwParser123.Parse(req.File123, batchId);
            db.EcwSubmissions.AddRange(rows);
            await db.SaveChangesAsync(ct);
        }

        if (req.File1310 is not null)
        {
            logger.LogInformation("Parsing 13.10 for batch {Id}", batchId);
            var rows = EcwParser1310.Parse(req.File1310, batchId);
            db.EcwBillingLags.AddRange(rows);
            await db.SaveChangesAsync(ct);
        }

        if (req.File3108 is not null)
        {
            logger.LogInformation("Parsing 31.08 for batch {Id}", batchId);
            var rows = EcwParser3108.Parse(req.File3108, batchId);
            db.EcwPatientAgings.AddRange(rows);
            await db.SaveChangesAsync(ct);
        }

        if (req.File3109Primary is not null)
        {
            logger.LogInformation("Parsing 31.09 Primary for batch {Id}", batchId);
            var rows = EcwParser3109.Parse(req.File3109Primary, batchId, isPrimary: true);
            db.EcwPayerAgings.AddRange(rows);
            await db.SaveChangesAsync(ct);
        }

        if (req.File3109Secondary is not null)
        {
            logger.LogInformation("Parsing 31.09 Secondary for batch {Id}", batchId);
            var rows = EcwParser3109.Parse(req.File3109Secondary, batchId, isPrimary: false);
            db.EcwPayerAgings.AddRange(rows);
            await db.SaveChangesAsync(ct);
        }
    }

    public Task<List<EcwAuditBatch>> GetBatchesAsync(string ownerUserId, CancellationToken ct = default)
        => db.EcwAuditBatches
             .Where(b => b.OwnerUserId == ownerUserId)
             .OrderByDescending(b => b.UploadedAt)
             .ToListAsync(ct);

    public Task<EcwAuditBatch?> GetBatchAsync(int id, CancellationToken ct = default)
        => db.EcwAuditBatches.FindAsync([id], ct).AsTask();

    public async Task DeleteBatchAsync(int id, CancellationToken ct = default)
    {
        await db.EcwClaimFinancials.Where(x => x.BatchId == id).ExecuteDeleteAsync(ct);
        await db.EcwCptLines.Where(x => x.BatchId == id).ExecuteDeleteAsync(ct);
        await db.EcwSubmissions.Where(x => x.BatchId == id).ExecuteDeleteAsync(ct);
        await db.EcwBillingLags.Where(x => x.BatchId == id).ExecuteDeleteAsync(ct);
        await db.EcwPatientAgings.Where(x => x.BatchId == id).ExecuteDeleteAsync(ct);
        await db.EcwPayerAgings.Where(x => x.BatchId == id).ExecuteDeleteAsync(ct);
        await db.EcwAuditBatches.Where(b => b.Id == id).ExecuteDeleteAsync(ct);
    }
}
