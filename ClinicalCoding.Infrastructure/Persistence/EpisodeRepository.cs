using ClinicalCoding.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace ClinicalCoding.Infrastructure.Persistence;

public class EpisodeRepository(CodingDbContext db)
{
    public async Task<EpisodeEntity> AddAsync(EpisodeEntity entity, CancellationToken ct = default)
    {
        db.Episodes.Add(entity);
        await db.SaveChangesAsync(ct);
        return entity;
    }

    public Task<EpisodeEntity?> GetAsync(Guid id, CancellationToken ct = default) =>
        db.Episodes
          .Include(e => e.Diagnoses)
          .Include(e => e.Procedures)
          .FirstOrDefaultAsync(e => e.Id == id, ct);

    public async Task<List<EpisodeEntity>> ListAsync(int take = 50, CancellationToken ct = default) =>
        await db.Episodes
            .OrderByDescending(e => e.AdmissionDate)
            .Take(take)
            .AsNoTracking()
            .ToListAsync(ct);

    public async Task<(List<EpisodeEntity> items, int total)> QueryAsync(
        int? status, DateTime? from, DateTime? to, int page, int pageSize, CancellationToken ct = default)
    {
        var q = db.Episodes.AsQueryable();
        if (status.HasValue)
            q = q.Where(e => (int)e.Status == status.Value);
        if (from.HasValue)
            q = q.Where(e => e.AdmissionDate >= from.Value);
        if (to.HasValue)
            q = q.Where(e => e.AdmissionDate <= to.Value);

        var total = await q.CountAsync(ct);
        var items = await q.OrderByDescending(e => e.AdmissionDate)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .AsNoTracking()
            .ToListAsync(ct);
        return (items, total);
    }

    public async Task AddAuditAsync(AuditEntry entry, CancellationToken ct = default)
    {
        db.AuditEntries.Add(entry);
        await db.SaveChangesAsync(ct);
    }

    public async Task<List<AuditEntry>> ListAuditAsync(int take = 200, CancellationToken ct = default) =>
        await db.AuditEntries
            .OrderByDescending(a => a.Timestamp)
            .Take(take)
            .AsNoTracking()
            .ToListAsync(ct);

    public async Task SubmitAsync(Guid id, string user, CancellationToken ct = default)
    {
        var e = await db.Episodes.FirstAsync(x => x.Id == id, ct);
        e.Status = EpisodeStatus.Submitted;
        e.SubmittedBy = user;
        e.SubmittedOn = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public async Task ApproveAsync(Guid id, string user, string? notes, CancellationToken ct = default)
    {
        var e = await db.Episodes.FirstAsync(x => x.Id == id, ct);
        e.Status = EpisodeStatus.Approved;
        e.ReviewedBy = user;
        e.ReviewedOn = DateTimeOffset.UtcNow;
        e.ReviewNotes = notes;
        await db.SaveChangesAsync(ct);
    }

    public async Task RejectAsync(Guid id, string user, string? notes, CancellationToken ct = default)
    {
        var e = await db.Episodes.FirstAsync(x => x.Id == id, ct);
        e.Status = EpisodeStatus.Rejected;
        e.ReviewedBy = user;
        e.ReviewedOn = DateTimeOffset.UtcNow;
        e.ReviewNotes = notes;
        await db.SaveChangesAsync(ct);
    }


public async Task<ClinicianQueryEntity?> GetQueryAsync(Guid id, CancellationToken ct = default) =>
    await db.ClinicianQueries.FirstOrDefaultAsync(x => x.Id == id, ct);

public async Task<ClinicianQueryEntity> CreateQueryAsync(ClinicianQueryEntity q, CancellationToken ct = default)
{
    db.ClinicianQueries.Add(q);
    await db.SaveChangesAsync(ct);
    return q;
}

public async Task<EpisodeEntity?> GetEpisodeWithDetailsAsync(Guid id, CancellationToken ct = default) =>
    await db.Episodes
        .Include(e => e.Diagnoses)
        .Include(e => e.Procedures)
        .FirstOrDefaultAsync(e => e.Id == id, ct);

public async Task ReplaceEpisodeCodesAsync(Guid id, IEnumerable<DiagnosisEntity> dx, IEnumerable<ProcedureEntity> px, CancellationToken ct = default)
{
    var e = await db.Episodes
        .Include(x => x.Diagnoses)
        .Include(x => x.Procedures)
        .FirstAsync(x => x.Id == id, ct);

    db.Diagnoses.RemoveRange(e.Diagnoses);
    db.Procedures.RemoveRange(e.Procedures);
    await db.SaveChangesAsync(ct);

    foreach (var d in dx) { d.EpisodeId = id; db.Diagnoses.Add(d); }
    foreach (var p in px) { p.EpisodeId = id; db.Procedures.Add(p); }
    await db.SaveChangesAsync(ct);
}

    public async Task<bool> UpdateQueryResponseAsync(Guid id, string? respondedBy, string responseText, CancellationToken ct = default)
    {
        var q = await db.ClinicianQueries.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (q is null) return false;
        q.RespondedBy = respondedBy;
        q.ResponseText = responseText;
        q.RespondedOn = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return true;
    }



    public async Task<AuditEntry?> GetLastResuggestAuditForEpisodeAsync(Guid episodeId, CancellationToken ct = default)
    {
        return await db.AuditEntries
            .Where(a => a.EntityType == nameof(EpisodeEntity) && a.EntityId == episodeId.ToString() && a.Action == "ReSuggestionApplied")
            .OrderByDescending(a => a.Timestamp)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<AuditEntry?> GetAuditByIdAsync(Guid id, CancellationToken ct = default)
        => await db.AuditEntries.FirstOrDefaultAsync(a => a.Id == id, ct);

    public async Task AddDeadLetterAsync(string kind, string payloadJson, string error, CancellationToken ct = default)
    {
        db.DeadLetters.Add(new DeadLetterEntity
        {
            Kind = kind,
            PayloadJson = payloadJson,
            Error = error,
            Attempts = 0,
            CreatedOn = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync(ct);
    }

    public async Task<DeadLetterEntity?> GetDeadLetterAsync(Guid id, CancellationToken ct = default)
        => await db.DeadLetters.FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task UpdateDeadLetterAsync(DeadLetterEntity e, CancellationToken ct = default)
    {
        e.Attempts += 1;
        e.LastTriedOn = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }



    public async Task<RevertRequest> CreateRevertRequestAsync(Guid episodeId, Guid auditId, string requestedBy, CancellationToken ct = default)
    {
        var r = new RevertRequest { EpisodeId = episodeId, AuditId = auditId, RequestedBy = requestedBy };
        db.RevertRequests.Add(r);
        await db.SaveChangesAsync(ct);
        return r;
    }

    public Task<RevertRequest?> GetRevertRequestAsync(Guid id, CancellationToken ct = default)
        => db.RevertRequests.FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task ApproveRevertAsync(Guid id, string approver, CancellationToken ct = default)
    {
        var r = await db.RevertRequests.FirstAsync(x => x.Id == id, ct);
        r.Status = RevertStatus.Approved;
        r.ApprovedBy = approver;
        r.ApprovedOn = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public async Task RejectRevertAsync(Guid id, string approver, CancellationToken ct = default)
    {
        var r = await db.RevertRequests.FirstAsync(x => x.Id == id, ct);
        r.Status = RevertStatus.Rejected;
        r.RejectedBy = approver;
        r.RejectedOn = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }
}
