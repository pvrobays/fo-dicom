// Copyright (c) 2012-2025 fo-dicom contributors.
// Licensed under the Microsoft Public License (MS-PL).

using Microsoft.EntityFrameworkCore;

namespace FellowOakDicom.SimplePacs.Data
{
    public class SimplePacsDbContext : DbContext
    {
        public SimplePacsDbContext(DbContextOptions<SimplePacsDbContext> options)
            : base(options) { }

        public DbSet<StudyRecord> Studies => Set<StudyRecord>();
        public DbSet<SeriesRecord> Series => Set<SeriesRecord>();
        public DbSet<InstanceRecord> Instances => Set<InstanceRecord>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // Unique index on StudyInstanceUID
            modelBuilder.Entity<StudyRecord>()
                .HasIndex(s => s.StudyInstanceUid)
                .IsUnique();

            // Unique index on SeriesInstanceUID
            modelBuilder.Entity<SeriesRecord>()
                .HasIndex(s => s.SeriesInstanceUid)
                .IsUnique();

            // Unique index on SopInstanceUID
            modelBuilder.Entity<InstanceRecord>()
                .HasIndex(i => i.SopInstanceUid)
                .IsUnique();

            // Study → Series (one-to-many, cascade delete)
            modelBuilder.Entity<StudyRecord>()
                .HasMany(s => s.Series)
                .WithOne(sr => sr.Study)
                .HasForeignKey(sr => sr.StudyId)
                .OnDelete(DeleteBehavior.Cascade);

            // Series → Instance (one-to-many, cascade delete)
            modelBuilder.Entity<SeriesRecord>()
                .HasMany(s => s.Instances)
                .WithOne(i => i.Series)
                .HasForeignKey(i => i.SeriesId)
                .OnDelete(DeleteBehavior.Cascade);

            // Non-unique index on PatientId and PatientName for QIDO filtering
            modelBuilder.Entity<StudyRecord>()
                .HasIndex(s => s.PatientId);
            modelBuilder.Entity<StudyRecord>()
                .HasIndex(s => s.PatientName);
            modelBuilder.Entity<StudyRecord>()
                .HasIndex(s => s.StudyDate);

            // Non-unique index on Modality for QIDO filtering
            modelBuilder.Entity<SeriesRecord>()
                .HasIndex(s => s.Modality);
        }
    }
}
