using Microsoft.EntityFrameworkCore;
using ContentRiskScanner.Models;

namespace ContentRiskScanner.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
        {
        }

        public DbSet<ScanResult> Scans { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<ScanResult>(entity =>
            {
                entity.ToTable("scans");
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).HasColumnName("id").UseIdentityAlwaysColumn();
                entity.Property(e => e.Content).HasColumnName("content").IsRequired();
                entity.Property(e => e.RiskScore).HasColumnName("risk_score");
                entity.Property(e => e.Status).HasColumnName("status").IsRequired();
                entity.Property(e => e.Issues).HasColumnName("issues").IsRequired();
                entity.Property(e => e.CreatedAt).HasColumnName("created_at")
                    .HasDefaultValueSql("NOW()");
            });
        }
    }
}