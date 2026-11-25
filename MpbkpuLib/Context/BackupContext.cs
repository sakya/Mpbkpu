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
        public DbSet<DataModel.Destination> Destinations => Set<DataModel.Destination>();

        public BackupContext()
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

            modelBuilder.Entity<DataModel.Backup>(entity =>
            {
                entity.HasIndex(e => e.Guid)
                    .IsUnique();
            });

            modelBuilder.Entity<DataModel.BackupItem>(entity =>
            {
                entity.HasIndex(e => new { e.BackupId, e.OriginalPath })
                    .IsUnique(false);
                entity.HasIndex(e => new { e.BackupId, e.OriginalRelativePath })
                    .IsUnique(false);
            });

            modelBuilder.Entity<DataModel.BackupVolume>(entity =>
            {
                entity.HasIndex(e => new { e.BackupId, e.Name })
                    .IsUnique();
            });

            modelBuilder.Entity<DataModel.Destination>(entity =>
            {
                entity.HasIndex(e => e.Key)
                    .IsUnique();
            });

            modelBuilder.Entity<DataModel.File>(entity =>
            {
                entity.HasIndex(e => e.FullPath)
                    .IsUnique(false);

                entity.HasIndex(e => e.RelativePath)
                    .IsUnique(false);

                entity.HasIndex(e => e.FileAction)
                    .IsUnique(false);
            });
        }
    }
}