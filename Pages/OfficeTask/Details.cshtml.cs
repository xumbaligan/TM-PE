using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using TM_PE.Data;
using TM_PE.Model;

namespace TM_PE.Pages.OfficeTask
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

        // Employees not yet assigned to this task, for the "Select an employee to add" dropdown
        public List<Employee> AvailableEmployees { get; set; } = new();

        // Latest submission per ActivityID (for the Submission column)
        public Dictionary<int, ActivitySubmission> LatestSubmissions { get; set; } = new();

        [TempData]
        public string? ErrorMessage { get; set; }

        // ---------------------------------------------------------------
        // LOAD
        // ---------------------------------------------------------------
        public async Task<IActionResult> OnGetAsync(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var task = await _context.OfficeTasks
                .Include(t => t.Assignments).ThenInclude(a => a.Employee)
                .Include(t => t.Activities)
                .FirstOrDefaultAsync(t => t.OfficeTaskID == id);

            if (task == null)
            {
                return NotFound();
            }

            // Keep activities in a stable, predictable order
            task.Activities = task.Activities.OrderBy(a => a.ActivityID).ToList();

            OfficeTask = task;

            var assignedIds = task.Assignments.Select(a => a.EmployeeID).ToList();
            AvailableEmployees = await _context.Employees
                .Where(e => e.IsActive && !assignedIds.Contains(e.EmployeeId))
                .OrderBy(e => e.FullName)
                .ToListAsync();

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
        // ASSIGNED EMPLOYEES
        // ---------------------------------------------------------------
        public async Task<IActionResult> OnPostAddEmployeeAsync(int officeTaskId, int employeeId)
        {
            if (employeeId > 0)
            {
                bool alreadyAssigned = await _context.TaskAssignments
                    .AnyAsync(a => a.OfficeTaskID == officeTaskId && a.EmployeeID == employeeId);

                if (!alreadyAssigned)
                {
                    _context.TaskAssignments.Add(new TaskAssignment
                    {
                        OfficeTaskID = officeTaskId,
                        EmployeeID = employeeId,
                        AssignedDate = DateTime.Now
                    });
                    await _context.SaveChangesAsync();
                }
            }

            return RedirectToPage(new { id = officeTaskId });
        }

        public async Task<IActionResult> OnPostRemoveEmployeeAsync(int taskAssignmentId, int officeTaskId)
        {
            var assignment = await _context.TaskAssignments.FindAsync(taskAssignmentId);
            if (assignment != null)
            {
                _context.TaskAssignments.Remove(assignment);
                await _context.SaveChangesAsync();
            }

            return RedirectToPage(new { id = officeTaskId });
        }

        // ---------------------------------------------------------------
        // ACTIVITIES
        // ---------------------------------------------------------------
        public async Task<IActionResult> OnPostAddActivityAsync(int officeTaskId, string activityName)
        {
            if (!string.IsNullOrWhiteSpace(activityName))
            {
                _context.TaskActivities.Add(new TaskActivity
                {
                    OfficeTaskID = officeTaskId,
                    ActivityName = activityName.Trim(),
                    Status = "Pending",
                    DateCreated = DateTime.Now
                });
                await _context.SaveChangesAsync();
                await RecalculateTaskAsync(officeTaskId);
            }

            return RedirectToPage(new { id = officeTaskId });
        }

        public async Task<IActionResult> OnPostRemoveActivityAsync(int activityId, int officeTaskId)
        {
            // Remove any submissions tied to this activity first to avoid FK conflicts
            var submissions = _context.ActivitySubmissions.Where(s => s.ActivityID == activityId);
            _context.ActivitySubmissions.RemoveRange(submissions);

            var activity = await _context.TaskActivities.FindAsync(activityId);
            if (activity != null)
            {
                _context.TaskActivities.Remove(activity);
            }

            await _context.SaveChangesAsync();
            await RecalculateTaskAsync(officeTaskId);

            return RedirectToPage(new { id = officeTaskId });
        }

        // Fired when the Status dropdown on an activity row changes (auto-submits)
        public async Task<IActionResult> OnPostUpdateActivityStatusAsync(int activityId, string status, int officeTaskId)
        {
            var activity = await _context.TaskActivities.FindAsync(activityId);
            if (activity != null && !string.IsNullOrWhiteSpace(status))
            {
                activity.Status = status;
                await _context.SaveChangesAsync();
                await RecalculateTaskAsync(officeTaskId);
            }

            return RedirectToPage(new { id = officeTaskId });
        }

        // Fired when a manager types/edits the Feedback text for an activity
        public async Task<IActionResult> OnPostUpdateActivityFeedbackAsync(int activityId, string feedback, int officeTaskId)
        {
            var activity = await _context.TaskActivities.FindAsync(activityId);
            if (activity != null)
            {
                activity.FeedBack = feedback?.Trim() ?? string.Empty;
                await _context.SaveChangesAsync();
            }

            return RedirectToPage(new { id = officeTaskId });
        }

        // ---------------------------------------------------------------
        // SUBMISSIONS (file upload / download)
        // ---------------------------------------------------------------
        public async Task<IActionResult> OnPostUploadSubmissionAsync(int activityId, int officeTaskId, IFormFile submissionFile)
        {
            if (submissionFile == null || submissionFile.Length == 0)
            {
                ErrorMessage = "Please choose a file to upload.";
                return RedirectToPage(new { id = officeTaskId });
            }

            // Attribute the upload to the first employee assigned to this task.
            var firstAssignment = await _context.TaskAssignments
                .Where(a => a.OfficeTaskID == officeTaskId)
                .OrderBy(a => a.AssignedDate)
                .FirstOrDefaultAsync();

            if (firstAssignment == null)
            {
                ErrorMessage = "Assign an employee to this task before uploading a submission.";
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
                existing.EmployeeID = firstAssignment.EmployeeID;
            }
            else
            {
                _context.ActivitySubmissions.Add(new ActivitySubmission
                {
                    ActivityID = activityId,
                    EmployeeID = firstAssignment.EmployeeID,
                    FileName = submissionFile.FileName,
                    FilePath = relativePath,
                    DateSubmitted = DateTime.Now,
                    Status = "Pending Review"
                });
            }

            // A fresh upload puts the activity back into review, unless a manager already approved it.
            var activity = await _context.TaskActivities.FindAsync(activityId);
            if (activity != null && activity.Status != "Approved")
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
        // SAVE / CANCEL
        // ---------------------------------------------------------------
        // All add/remove/status/upload actions already persist immediately, so Save
        // just does one final recalculation for safety and returns to the task list.
        public async Task<IActionResult> OnPostSaveAsync(int officeTaskId)
        {
            await RecalculateTaskAsync(officeTaskId);
            return RedirectToPage("./Index");
        }

        // ---------------------------------------------------------------
        // Status/Progress auto-calculation
        // ---------------------------------------------------------------
        // Rule: Progress = % of activities that are Approved.
        //       Status = Completed  -> every activity is Approved
        //                In Progress -> at least one activity is Approved or Submitted
        //                Pending    -> otherwise (or no activities yet)
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

                // Score: 100 points split evenly across all activities, earned per Approved activity.
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
