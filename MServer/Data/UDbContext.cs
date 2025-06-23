using Microsoft.EntityFrameworkCore;
using MServer.Models;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using System.IO;

namespace MServer.Data
{

    public class ApplicationDbContextFactory :
        IDesignTimeDbContextFactory<UDbContext>
    {
        public UDbContext CreateDbContext(string[] args)
        {
            var configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appSettings.json")
                .Build();

            var optionsBuilder = new 
            DbContextOptionsBuilder<UDbContext>();
            var connectionString = configuration
            .GetConnectionString("DefaultConnection");
            optionsBuilder.UseNpgsql(connectionString);

            return new UDbContext
            (optionsBuilder.Options);
        }
    }

    public class UDbContext : DbContext
    {
        public UDbContext
        (DbContextOptions
        <UDbContext> options)
            : base(options)
        {
        }

        public DbSet<User> Users { get; set; }

        protected override void
        OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.HasDefaultSchema("mserver");
            modelBuilder.Entity<User>().ToTable("User");
        }

        public override int SaveChanges()
        {
            ConvertDatesToUtc();
            return base.SaveChanges();
        }

        public override Task<int> SaveChangesAsync
        (CancellationToken cancellationToken = default)
        {
            ConvertDatesToUtc();
            return base.SaveChangesAsync(cancellationToken);
        }

        private void ConvertDatesToUtc()
        {
            var entries = ChangeTracker.Entries()
                .Where(e => e.State == EntityState.Added ||
                e.State == EntityState.Modified);

            foreach (var entry in entries)
            {
                var properties = entry.Properties.Where
                    (p => p.Metadata.ClrType == typeof(DateTime) ||
                        p.Metadata.ClrType == typeof(DateTime?));

                foreach (var property in properties)
                {
                    if (property.CurrentValue is DateTime dateTime &&
                    dateTime.Kind == DateTimeKind.Unspecified)
                    {
                        property.CurrentValue = DateTime
                        .SpecifyKind(dateTime, DateTimeKind.Utc);
                    }
                }
            }
        }
    }
}