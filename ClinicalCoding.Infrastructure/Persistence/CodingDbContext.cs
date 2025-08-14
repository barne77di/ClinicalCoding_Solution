using ClinicalCoding.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace ClinicalCoding.Infrastructure.Persistence;

public class CodingDbContext(DbContextOptions<CodingDbContext> options) : DbContext(options)
{
    public DbSet<EpisodeEntity> Episodes => Set<EpisodeEntity>();
    public DbSet<DiagnosisEntity> Diagnoses => Set<DiagnosisEntity>();
    public DbSet<ProcedureEntity> Procedures => Set<ProcedureEntity>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();
    public DbSet<ClinicianQueryEntity> ClinicianQueries => Set<ClinicianQueryEntity>();
    public DbSet<DeadLetterEntity> DeadLetters => Set<DeadLetterEntity>();
    public DbSet<RevertRequest> RevertRequests => Set<RevertRequest>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<EpisodeEntity>()
            .HasMany(e => e.Diagnoses)
            .WithOne(d => d.Episode)
            .HasForeignKey(d => d.EpisodeId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<EpisodeEntity>()
            .HasMany(e => e.Procedures)
            .WithOne(p => p.Episode)
            .HasForeignKey(p => p.EpisodeId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<AuditEntry>().HasIndex(a => a.Timestamp);
        modelBuilder.Entity<ClinicianQueryEntity>().HasIndex(q => q.CreatedOn);
        modelBuilder.Entity<DeadLetterEntity>().HasIndex(d => d.CreatedOn);
        modelBuilder.Entity<RevertRequest>().HasIndex(r => new { r.EpisodeId, r.Status });
    }
}
