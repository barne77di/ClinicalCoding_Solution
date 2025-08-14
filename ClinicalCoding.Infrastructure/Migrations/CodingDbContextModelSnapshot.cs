using System;
using ClinicalCoding.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

#nullable disable

namespace ClinicalCoding.Infrastructure.Migrations
{
    [DbContext(typeof(CodingDbContext))]
    partial class CodingDbContextModelSnapshot : ModelSnapshot
    {
        protected override void BuildModel(ModelBuilder modelBuilder)
        {
            modelBuilder
                .HasAnnotation("ProductVersion", "8.0.7");

            modelBuilder.Entity("ClinicalCoding.Domain.Models.AuditEntry", b =>
            {
                b.Property<Guid>("Id").ValueGeneratedOnAdd();
                b.Property<string>("Action");
                b.Property<string>("EntityId");
                b.Property<string>("EntityType");
                b.Property<string>("PerformedBy");
                b.Property<string>("PayloadJson");
                b.Property<DateTimeOffset>("Timestamp");
                b.HasKey("Id");
                b.HasIndex("Timestamp");
                b.ToTable("AuditEntries");
            });

            modelBuilder.Entity("ClinicalCoding.Domain.Models.DiagnosisEntity", b =>
            {
                b.Property<Guid>("Id").ValueGeneratedOnAdd();
                b.Property<string>("Code").HasMaxLength(10);
                b.Property<string>("Description").HasMaxLength(300);
                b.Property<Guid>("EpisodeId");
                b.Property<bool>("IsPrimary");
                b.HasKey("Id");
                b.HasIndex("EpisodeId");
                b.ToTable("Diagnoses");
            });

            modelBuilder.Entity("ClinicalCoding.Domain.Models.EpisodeEntity", b =>
            {
                b.Property<Guid>("Id").ValueGeneratedOnAdd();
                b.Property<DateTime>("AdmissionDate");
                b.Property<DateTime?>("DischargeDate");
                b.Property<string>("NHSNumber").HasMaxLength(20);
                b.Property<string>("PatientName").HasMaxLength(200);
                b.Property<string>("SourceText");
                b.Property<string>("Specialty").HasMaxLength(100);
                b.HasKey("Id");
                b.ToTable("Episodes");
            });

            modelBuilder.Entity("ClinicalCoding.Domain.Models.ProcedureEntity", b =>
            {
                b.Property<Guid>("Id").ValueGeneratedOnAdd();
                b.Property<string>("Code").HasMaxLength(10);
                b.Property<string>("Description").HasMaxLength(300);
                b.Property<Guid>("EpisodeId");
                b.Property<DateTime?>("PerformedOn");
                b.HasKey("Id");
                b.HasIndex("EpisodeId");
                b.ToTable("Procedures");
            });
        }
    }
}
