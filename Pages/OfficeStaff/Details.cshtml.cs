using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using TM_PE.Data;
using TM_PE.Model;

namespace TM_PE.Pages.OfficeStaff
{
    public class DetailsModel : PageModel
    {
        private readonly AppDbContext _context;
        private readonly IWebHostEnvironment _env;

        public DetailsModel(AppDbContext context, IWebHostEnvironment env)
        {
            _context = context;
            _env = env;
        }

        public TM_PE.Model.OfficeTask OfficeTask { get; set; } = default!;

        public Employee CurrentEmployee { get; set; } = default!;

        // Latest submission per ActivityID (for the Submission column)
        public Dictionary<int, ActivitySubmission> LatestSubmissions { get; set; } = new();

        [TempData]
        public string? ErrorMessage { get; set; }

        // ---------------------------------------------------------------
        // LOAD
        // ---------------------------------------------------------------
        public async Task<IActionResult> OnGetAsync(int? id)
        {
            var employeeId = HttpContext.Session.GetInt32("CurrentEmployeeId");
            if (employeeId == null)
            {
                return RedirectToPage("./Select");
            }

            var employee = await _context.Employees.FindAsync(employeeId.Value);
            if (employee == null)
            {
                HttpContext.Session.Remove("CurrentEmployeeId");
                return RedirectToPage("./Select");
            }

            CurrentEmployee = employee;

            if (id == null)
            {
                return NotFound();
            }

            var task = await _context.OfficeTasks
                .Include(t => t.Assignments).ThenInclude(a => a.Employee)
                .Include(t => t.Activities).ThenInclude(a => a.AssignedEmployee)
                .FirstOrDefaultAsync(t => t.OfficeTaskID == id);

            if (task == null)
            {
                return NotFound();
            }

            bool isAssignedToTask = task.Assignments.Any(a => a.EmployeeID == employeeId.Value);
            if (!isAssignedToTask)
            {
                ErrorMessage = "You are not assigned to that task.";
                return RedirectToPage("./Index");
            }

            task.Activities = task.Activities.OrderBy(a => a.ActivityID).ToList();
            OfficeTask = task;

            var activityIds = task.Activities.Select(a => a.ActivityID).ToList();
            LatestSubmissions = await _context.ActivitySubmissions
                .Where(s => activityIds.Contains(s.ActivityID))
                .OrderByDescending(s => s.DateSubmitted)
                .GroupBy(s => s.ActivityID)
                .Select(g => g.First())
                .ToDictionaryAsync(s => s.ActivityID, s => s);

            return Page();
        }

        // ---------------------------------------------------------------
        // SUBMISSIONS (file upload / download)
        // ---------------------------------------------------------------
        // Employees can only upload to activities assigned to them. Submitting
        // automatically flips the activity to "Submitted" (unless a manager has
        // already Approved it); only a manager can set Approved/Rejected.
        public async Task<IActionResult> OnPostUploadSubmissionAsync(int activityId, int officeTaskId, IFormFile submissionFile)
        {
            var employeeId = HttpContext.Session.GetInt32("CurrentEmployeeId");
            if (employeeId == null)
            {
                return RedirectToPage("./Select");
            }

            var activity = await _context.TaskActivities.FindAsync(activityId);
            if (activity == null || activity.OfficeTaskID != officeTaskId)
            {
                return NotFound();
            }

            if (activity.AssignedEmployeeID != employeeId.Value)
            {
                ErrorMessage = "You can only upload a file for activities assigned to you.";
                return RedirectToPage(new { id = officeTaskId });
            }

            if (submissionFile == null || submissionFile.Length == 0)
            {
                ErrorMessage = "Please choose a file to upload.";
                return RedirectToPage(new { id = officeTaskId });
            }

            var uploadsRoot = Path.Combine(_env.WebRootPath, "uploads", "activity-submissions");
            Directory.CreateDirectory(uploadsRoot);

            var safeFileName = $"{activityId}_{Guid.NewGuid():N}_{Path.GetFileName(submissionFile.FileName)}";
            var fullPath = Path.Combine(uploadsRoot, safeFileName);

            using (var stream = new FileStream(fullPath, FileMode.Create))
            {
                await submissionFile.CopyToAsync(stream);
            }

            var relativePath = Path.Combine("uploads", "activity-submissions", safeFileName).Replace("\\", "/");

            // One submission record per activity: replace the previous file if one exists
            var existing = await _context.ActivitySubmissions
                .FirstOrDefaultAsync(s => s.ActivityID == activityId);

            if (existing != null)
            {
                existing.FileName = submissionFile.FileName;
                existing.FilePath = relativePath;
                existing.DateSubmitted = DateTime.Now;
                existing.Status = "Pending Review";
                existing.EmployeeID = employeeId.Value;
            }
            else
            {
                _context.ActivitySubmissions.Add(new ActivitySubmission
                {
                    ActivityID = activityId,
                    EmployeeID = employeeId.Value,
                    FileName = submissionFile.FileName,
                    FilePath = relativePath,
                    DateSubmitted = DateTime.Now,
                    Status = "Pending Review"
                });
            }

            // A fresh upload puts the activity back into review, unless a manager already approved it.
            if (activity.Status != "Approved")
            {
                activity.Status = "Submitted";
            }

            await _context.SaveChangesAsync();
            await RecalculateTaskAsync(officeTaskId);

            return RedirectToPage(new { id = officeTaskId });
        }

        public async Task<IActionResult> OnGetDownloadAsync(int submissionId)
        {
            var submission = await _context.ActivitySubmissions.FindAsync(submissionId);
            if (submission == null || string.IsNullOrEmpty(submission.FilePath))
            {
                return NotFound();
            }

            var fullPath = Path.Combine(_env.WebRootPath, submission.FilePath.Replace("/", Path.DirectorySeparatorChar.ToString()));
            if (!System.IO.File.Exists(fullPath))
            {
                return NotFound();
            }

            var bytes = await System.IO.File.ReadAllBytesAsync(fullPath);
            return File(bytes, "application/octet-stream", submission.FileName);
        }

        // ---------------------------------------------------------------
        // Status/Progress auto-calculation (mirrors the manager-side rule)
        // ---------------------------------------------------------------
        private async Task RecalculateTaskAsync(int officeTaskId)
        {
            var task = await _context.OfficeTasks
                .Include(t => t.Activities)
                .FirstOrDefaultAsync(t => t.OfficeTaskID == officeTaskId);

            if (task == null)
            {
                return;
            }

            var activities = task.Activities.ToList();

            if (activities.Count == 0)
            {
                task.Status = "Pending";
                task.Progress = 0;
                task.Score = 0;
            }
            else
            {
                int approvedCount = activities.Count(a => a.Status == "Approved");
                task.Progress = Math.Round((decimal)approvedCount / activities.Count * 100, 0);

                decimal pointsPerActivity = 100m / activities.Count;
                task.Score = Math.Round(pointsPerActivity * approvedCount, 2);

                if (approvedCount == activities.Count)
                {
                    task.Status = "Completed";
                }
                else if (activities.Any(a => a.Status == "Approved" || a.Status == "Submitted"))
                {
                    task.Status = "In Progress";
                }
                else
                {
                    task.Status = "Pending";
                }
            }

            await _context.SaveChangesAsync();
        }
    }
}
