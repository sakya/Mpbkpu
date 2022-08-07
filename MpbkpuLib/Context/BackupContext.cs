using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace MpbkpuLib.Context
{
    public class BackupContext : DbContext
    {

        public DbSet<DataModel.Backup> Backups => Set<DataModel.Backup>();
        public DbSet<DataModel.BackupItem> BackupItems => Set<DataModel.BackupItem>();
        public DbSet<DataModel.BackupVolume> BackupVolumes => Set<DataModel.BackupVolume>();
        public DbSet<DataModel.BackupWarning> BackupWarnings => Set<DataModel.BackupWarning>();
        public DbSet<DataModel.File> Files => Set<DataModel.File>();

        public BackupContext() :
            base()
        {
        }

        public BackupContext(DbContextOptions options) :
            base(options)
        {
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            if (!optionsBuilder.IsConfigured) {
                optionsBuilder.UseSqlite("Data Source=test.db;Pooling=false");
            }
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<DataModel.Backup>(e =>
            {
                e.HasIndex(e => e.Guid)
                    .IsUnique(true);
            });

            modelBuilder.Entity<DataModel.BackupItem>(e =>
            {
                e.HasIndex(e => new { e.BackupId, e.OriginalPath })
                    .IsUnique(false);
            });

            modelBuilder.Entity<DataModel.BackupVolume>(e =>
            {
                e.HasIndex(e => new { e.BackupId, e.Name })
                    .IsUnique(true);
            });

            modelBuilder.Entity<DataModel.File>(e =>
            {
                e.HasIndex(e => e.FullPath)
                    .IsUnique(false);

                e.HasIndex(e => e.FileAction)
                    .IsUnique(false);
            });
        }
    }
}