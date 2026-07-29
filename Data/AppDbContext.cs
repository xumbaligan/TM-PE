using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Reflection.Emit;
using TM_PE.Model;


namespace TM_PE.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        public DbSet<Department> Departments => Set<Department>();
        public DbSet<Employee> Employees => Set<Employee>();
        public DbSet<Criteria> Criteria => Set<Criteria>();
        // Backwards-compatible DbSet name used in pages (was previously named "Task")
        public DbSet<OfficeTask> OfficeTasks => Set<OfficeTask>();
        public DbSet<TaskActivity> TaskActivities => Set<TaskActivity>();
        public DbSet<TaskAssignment> TaskAssignments => Set<TaskAssignment>();
        public DbSet<ActivitySubmission> ActivitySubmissions => Set<ActivitySubmission>();

        protected override void OnModelCreating(ModelBuilder b)
        {
            base.OnModelCreating(b);

            b.Entity<Employee>()
                .HasIndex(e => e.Email)
                .IsUnique();

            b.Entity<Employee>()
                .Property(e => e.RoleType)
                .HasConversion<string>();

            b.Entity<Criteria>()
                .Property(e => e.RoleType)
                .HasConversion<string>();

            // Let the database set CreatedAt automatically
            b.Entity<Department>()
                .Property(d => d.CreatedAt)
                .HasDefaultValueSql("GETUTCDATE()");
        }
    }
}
