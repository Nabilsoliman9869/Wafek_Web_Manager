using Microsoft.EntityFrameworkCore;
using Wafek_Web_Manager.Models;

namespace Wafek_Web_Manager.Data
{
    public class WafekDbContext : DbContext
    {
        public WafekDbContext(DbContextOptions<WafekDbContext> options)
            : base(options)
        {
        }

        public DbSet<WorkflowDefinition> WorkflowDefinitions { get; set; }
        public DbSet<WorkflowStep> WorkflowSteps { get; set; }
        public DbSet<WorkflowLog> WorkflowLogs { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // ربط الكيانات بأسماء الجداول الفعلية في قاعدة البيانات (WF_Definitions, WF_Steps, WF_Logs)
            modelBuilder.Entity<WorkflowDefinition>().ToTable("WF_Definitions");
            modelBuilder.Entity<WorkflowStep>().ToTable("WF_Steps");
            modelBuilder.Entity<WorkflowLog>().ToTable("WF_Logs");
        }
    }
}