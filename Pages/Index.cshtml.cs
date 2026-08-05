using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using TM_PE.Data;
using TM_PE.Model;

namespace TM_PE.Pages;

public class IndexModel : PageModel
{
    private readonly AppDbContext _db;
    public IndexModel(AppDbContext db) => _db = db;

    public int DepartmentCount { get; set; }
    public int EmployeeCount { get; set; }
    public int CriteriaCount { get; set; }

    // ---- Workload monitoring (Office Task module) ----
    public int ActiveOfficeTaskCount { get; set; }
    public int OverdueOfficeTaskCount { get; set; }
    public int CompletedOfficeTaskCount { get; set; }
    public List<WorkloadItem> Workload { get; set; } = new();

    public async Task OnGetAsync()
    {
        DepartmentCount = _db.Departments.Count();
        EmployeeCount = _db.Employees.Count();
        CriteriaCount = _db.Criteria.Count(c => c.IsActive);

        await BuildWorkloadAsync();
    }

    // Mirrors the overdue check used on the Office Task Index page so the
    // dashboard reflects the same live status, even if no one has opened
    // Office Tasks yet today.
    private async Task RefreshOverdueStatusesAsync(List<Model.OfficeTask> tasks)
    {
        var today = DateTime.Now.Date;
        bool changed = false;

        foreach (var task in tasks)
        {
            if (task.Status != "Completed" && task.DueDate.Date < today)
            {
                if (task.Status != "Overdue")
                {
                    task.Status = "Overdue";
                    changed = true;
                }
            }
            else if (task.Status == "Overdue" && task.DueDate.Date >= today)
            {
                task.Status = "Pending";
                changed = true;
            }
        }

        if (changed)
        {
            await _db.SaveChangesAsync();
        }
    }

    // Builds a per-Office-Staff workload snapshot from the same signals the
    // Office Task module already tracks: task assignments, per-activity
    // assignment, task status/overdue, and the computed task Score.
    private async Task BuildWorkloadAsync()
    {
        var tasks = await _db.OfficeTasks
            .Include(t => t.Assignments).ThenInclude(a => a.Employee)
            .Include(t => t.Activities).ThenInclude(a => a.AssignedEmployee)
            .ToListAsync();

        await RefreshOverdueStatusesAsync(tasks);

        ActiveOfficeTaskCount = tasks.Count(t => t.Status is "Pending" or "In Progress" or "Overdue");
        OverdueOfficeTaskCount = tasks.Count(t => t.Status == "Overdue");
        CompletedOfficeTaskCount = tasks.Count(t => t.Status == "Completed");

        var officeStaff = await _db.Employees
            .Where(e => e.IsActive && e.RoleType == RoleType.OfficeStaff)
            .Include(e => e.Department)
            .OrderBy(e => e.FullName)
            .ToListAsync();

        Workload = officeStaff.Select(emp =>
        {
            var assignedTasks = tasks.Where(t => t.Assignments.Any(a => a.EmployeeID == emp.EmployeeId)).ToList();
            var activeTasks = assignedTasks.Count(t => t.Status is "Pending" or "In Progress" or "Overdue");
            var overdueTasks = assignedTasks.Count(t => t.Status == "Overdue");
            var completedTasks = assignedTasks.Count(t => t.Status == "Completed");

            var assignedActivities = tasks
                .SelectMany(t => t.Activities)
                .Where(a => a.AssignedEmployeeID == emp.EmployeeId)
                .ToList();
            var pendingActivities = assignedActivities.Count(a => a.Status != "Approved");

            var avgScore = assignedTasks.Any() ? Math.Round(assignedTasks.Average(t => t.Score), 1) : 0;

            // Simple, transparent weighting: an active task counts more than a
            // pending activity since it carries more responsibility.
            var points = (activeTasks * 2) + pendingActivities + (overdueTasks * 2);
            var level = points switch
            {
                <= 2 => "Light",
                <= 6 => "Moderate",
                _ => "Heavy"
            };

            return new WorkloadItem
            {
                EmployeeId = emp.EmployeeId,
                FullName = emp.FullName,
                DepartmentName = emp.Department?.DepartmentName ?? "—",
                ActiveTasks = activeTasks,
                OverdueTasks = overdueTasks,
                CompletedTasks = completedTasks,
                TotalTasks = assignedTasks.Count,
                PendingActivities = pendingActivities,
                AvgScore = avgScore,
                WorkloadPoints = points,
                WorkloadLevel = level
            };
        })
        .OrderByDescending(w => w.WorkloadPoints)
        .ToList();
    }

    public class WorkloadItem
    {
        public int EmployeeId { get; set; }
        public string FullName { get; set; } = string.Empty;
        public string DepartmentName { get; set; } = string.Empty;
        public int ActiveTasks { get; set; }
        public int OverdueTasks { get; set; }
        public int CompletedTasks { get; set; }
        public int TotalTasks { get; set; }
        public int PendingActivities { get; set; }
        public decimal AvgScore { get; set; }
        public int WorkloadPoints { get; set; }
        public string WorkloadLevel { get; set; } = "Light";
    }
}
