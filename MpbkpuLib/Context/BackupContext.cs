using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace MpbkpuLib.Context
{
    public class BackupContext : DbContext
    {
        public DbSet<DataModel.Backup>? Backups { get; set; }
        public DbSet<DataModel.BackupItem>? BackupItems { get; set; }
        public DbSet<DataModel.BackupWarning>? BackupWarnings { get; set; }
        public DbSet<DataModel.File>? Files { get; set; }

        public BackupContext(DbContextOptions options) :
            base(options)
        {

        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<DataModel.File>(e =>
            {
                e.HasIndex(e => new { e.RootPath, e.RelativePath })
                .IsUnique(false);

                e.HasIndex(e => e.Action)
                .IsUnique(false);
            });
        }
    }
}