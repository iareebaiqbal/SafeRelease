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
    }
}