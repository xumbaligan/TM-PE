using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using TM_PE.Data;
using TM_PE.Model;

namespace TM_PE.Pages.FieldTechnician
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

        public static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp"
        };

        // "Rescheduled" can now be picked by the technician leader themselves (in
        // addition to being set automatically when a manager edits the service date).
        public static readonly string[] StatusOptions = { "Pending", "In Progress", "Completed", "Cancelled", "Rescheduled" };

        public JobTicket JobTicket { get; set; } = default!;

        public Employee CurrentEmployee { get; set; } = default!;

        // Only the assignment marked IsLeader for this employee/ticket grants upload
        // and status-change rights; everyone else assigned can view only.
        public bool IsLeader { get; set; }

        [TempData]
        public string? ErrorMessage { get; set; }

        // ---------------------------------------------------------------
        // LOAD
        // ---------------------------------------------------------------
        public async Task<IActionResult> OnGetAsync(int? id)
        {
            var employeeId = HttpContext.Session.GetInt32("CurrentFieldTechnicianId");
            if (employeeId == null)
            {
                return RedirectToPage("./Select");
            }

            var employee = await _context.Employees.FindAsync(employeeId.Value);
            if (employee == null || employee.RoleType != RoleType.FieldTechnician)
            {
                HttpContext.Session.Remove("CurrentFieldTechnicianId");
                return RedirectToPage("./Select");
            }

            CurrentEmployee = employee;

            if (id == null)
            {
                return NotFound();
            }

            var ticket = await _context.JobTickets
                .Include(t => t.Assignments).ThenInclude(a => a.Employee)
                .Include(t => t.Submissions).ThenInclude(s => s.Employee)
                .Include(t => t.RescheduleHistory).ThenInclude(h => h.ArchivedSubmissions).ThenInclude(s => s.Employee)
                .FirstOrDefaultAsync(t => t.JobTicketID == id);

            if (ticket == null)
            {
                return NotFound();
            }

            var myAssignment = ticket.Assignments.FirstOrDefault(a => a.EmployeeID == employeeId.Value);
            if (myAssignment == null)
            {
                ErrorMessage = "You are not assigned to that job order.";
                return RedirectToPage("./Index");
            }

            IsLeader = myAssignment.IsLeader;

            // Only the current cycle's submissions belong in the active list; ones
            // archived under a reschedule history entry show in that section instead.
            ticket.Submissions = ticket.Submissions
                .Where(s => s.RescheduleHistoryID == null)
                .OrderByDescending(s => s.DateSubmitted)
                .ToList();

            ticket.RescheduleHistory = ticket.RescheduleHistory
                .OrderByDescending(h => h.DateChanged)
                .ToList();

            JobTicket = ticket;

            return Page();
        }

        // ---------------------------------------------------------------
        // FILE SUBMISSION (leader only)
        // ---------------------------------------------------------------
        // NOTE: this handler only adds a file — it never changes the ticket's
        // Status or Remarks. Status/remarks changes only ever happen through the
        // explicit "Save" button in OnPostSaveAsync below, so uploading a photo
        // can never silently commit a status change (e.g. auto-completing a
        // ticket) before the leader has actually pressed Save.
        public async Task<IActionResult> OnPostUploadSubmissionAsync(int jobTicketId, IFormFile submissionFile, string? remarks)
        {
            var employeeId = HttpContext.Session.GetInt32("CurrentFieldTechnicianId");

            if (employeeId == null)
            {
                return RedirectToPage("./Select");
            }

            var ticket = await _context.JobTickets
                .Include(t => t.Assignments)
                .FirstOrDefaultAsync(t => t.JobTicketID == jobTicketId);

            if (ticket == null)
            {
                return NotFound();
            }

            var myAssignment = ticket.Assignments
                .FirstOrDefault(a => a.EmployeeID == employeeId.Value);

            if (myAssignment == null)
            {
                ErrorMessage = "You are not assigned to that job order.";
                return RedirectToPage("./Index");
            }

            if (!myAssignment.IsLeader)
            {
                ErrorMessage =
                    "Only the team leader can upload files for this job order.";

                return RedirectToPage(new { id = jobTicketId });
            }

            if (ticket.IsLockedFromEditing)
            {
                ErrorMessage = ticket.Status == JobTicketStatuses.Closed
                    ? "This job order has been closed and can no longer be updated."
                    : "This job order has been marked Completed and can no longer be updated.";

                return RedirectToPage(new { id = jobTicketId });
            }

            if (submissionFile == null || submissionFile.Length == 0)
            {
                ErrorMessage = "Please choose a file to upload.";
                return RedirectToPage(new { id = jobTicketId });
            }

            // Persist whatever remarks the leader has already typed in the Update
            // Status box — the page submits the upload as a separate form, so
            // without this the in-progress remarks text would be lost on reload.
            // The hidden field is kept in sync with the remarks textbox by JS, so
            // its value (even if blank) reflects what the leader currently has typed.
            ticket.Remarks = string.IsNullOrWhiteSpace(remarks)
                ? null
                : remarks.Trim();

            var uploadsRoot = Path.Combine(
                _env.WebRootPath,
                "uploads",
                "jobticket-submissions");

            Directory.CreateDirectory(uploadsRoot);

            var safeFileName =
                $"{jobTicketId}_{Guid.NewGuid():N}_{Path.GetFileName(submissionFile.FileName)}";

            var fullPath = Path.Combine(uploadsRoot, safeFileName);

            using (var stream = new FileStream(fullPath, FileMode.Create))
            {
                await submissionFile.CopyToAsync(stream);
            }

            var relativePath = Path.Combine(
                "uploads",
                "jobticket-submissions",
                safeFileName)
                .Replace("\\", "/");

            _context.JobTicketSubmissions.Add(new JobTicketSubmission
            {
                JobTicketID = jobTicketId,
                EmployeeID = employeeId.Value,
                FileName = submissionFile.FileName,
                FilePath = relativePath,
                DateSubmitted = DateTime.Now
            });

            await _context.SaveChangesAsync();

            return RedirectToPage(new { id = jobTicketId });
        }

        // ---------------------------------------------------------------
        // REMOVE A SUBMITTED FILE (leader only)
        // ---------------------------------------------------------------
        public async Task<IActionResult> OnPostDeleteSubmissionAsync(int jobTicketId, int submissionId)
        {
            var employeeId = HttpContext.Session.GetInt32("CurrentFieldTechnicianId");
            if (employeeId == null)
            {
                return RedirectToPage("./Select");
            }

            var ticket = await _context.JobTickets
                .Include(t => t.Assignments)
                .FirstOrDefaultAsync(t => t.JobTicketID == jobTicketId);

            if (ticket == null)
            {
                return NotFound();
            }

            var myAssignment = ticket.Assignments.FirstOrDefault(a => a.EmployeeID == employeeId.Value);
            if (myAssignment == null)
            {
                ErrorMessage = "You are not assigned to that job order.";
                return RedirectToPage("./Index");
            }

            if (!myAssignment.IsLeader)
            {
                ErrorMessage = "Only the team leader can remove files for this job order.";
                return RedirectToPage(new { id = jobTicketId });
            }

            if (ticket.IsLockedFromEditing)
            {
                ErrorMessage = ticket.Status == JobTicketStatuses.Closed
                    ? "This job order has been closed and can no longer be updated."
                    : "This job order has been marked Completed and can no longer be updated.";
                return RedirectToPage(new { id = jobTicketId });
            }

            var submission = await _context.JobTicketSubmissions
                .FirstOrDefaultAsync(s => s.JobTicketSubmissionID == submissionId
                    && s.JobTicketID == jobTicketId
                    && s.RescheduleHistoryID == null);

            if (submission != null)
            {
                var fullPath = Path.Combine(_env.WebRootPath, submission.FilePath.Replace("/", Path.DirectorySeparatorChar.ToString()));
                if (System.IO.File.Exists(fullPath))
                {
                    System.IO.File.Delete(fullPath);
                }

                _context.JobTicketSubmissions.Remove(submission);
                await _context.SaveChangesAsync();
            }

            return RedirectToPage(new { id = jobTicketId });
        }

        // ---------------------------------------------------------------
        // STATUS + REMARKS (leader only) ? consolidated Save; nothing is
        // persisted until the leader presses Save.
        // ---------------------------------------------------------------
        public async Task<IActionResult> OnPostSaveAsync(int jobTicketId, string status, string? remarks)
        {
            var employeeId = HttpContext.Session.GetInt32("CurrentFieldTechnicianId");

            if (employeeId == null)
            {
                return RedirectToPage("./Select");
            }

            var ticket = await _context.JobTickets
                .Include(t => t.Assignments)
                .Include(t => t.Submissions)
                .FirstOrDefaultAsync(t => t.JobTicketID == jobTicketId);

            if (ticket == null)
            {
                return NotFound();
            }

            var myAssignment = ticket.Assignments
                .FirstOrDefault(a => a.EmployeeID == employeeId.Value);

            if (myAssignment == null)
            {
                ErrorMessage = "You are not assigned to that job order.";
                return RedirectToPage("./Index");
            }

            // Only the leader can change the status
            if (!myAssignment.IsLeader)
            {
                ErrorMessage =
                    "Only the team leader can change the status of this job order.";

                return RedirectToPage(new { id = jobTicketId });
            }

            // Completed (and Closed) tickets cannot be changed further
            if (ticket.IsLockedFromEditing)
            {
                ErrorMessage = ticket.Status == JobTicketStatuses.Closed
                    ? "This job order has been closed and can no longer be updated."
                    : "This job order has been marked Completed and can no longer be updated.";

                return RedirectToPage(new { id = jobTicketId });
            }
            // Validate status
            if (!StatusOptions.Contains(status))
            {
                ErrorMessage = "Invalid status.";
                return RedirectToPage(new { id = jobTicketId });
            }

            // Only count the CURRENT cycle's files — ones archived under a
            // reschedule history entry don't count as proof for the new date.
            bool hasUploadedFile = ticket.Submissions != null &&
                                   ticket.Submissions.Any(s => s.RescheduleHistoryID == null);

            // ============================================================
            // RULE 1:
            // Once the ticket has moved past Pending, it can never go back.
            // ============================================================
            if (status == JobTicketStatuses.Pending && ticket.Status != JobTicketStatuses.Pending)
            {
                ErrorMessage =
                    "This job order cannot be changed back to Pending — work has already started.";

                return RedirectToPage(new { id = jobTicketId });
            }

            // ============================================================
            // RULE 2:
            // In Progress requires at least one uploaded file
            //
            // NOTE:
            // If you want the technician to be able to select
            // In Progress first and then upload the file, REMOVE this
            // validation for In Progress.
            // ============================================================

            if (status == JobTicketStatuses.Completed && !hasUploadedFile)
            {
                ErrorMessage =
                    "You must upload at least one photo or file before changing the job order to Completed.";

                return RedirectToPage(new { id = jobTicketId });
            }

            if (status == JobTicketStatuses.InProgress && !hasUploadedFile)
            {
                ErrorMessage =
                    "You must upload at least one photo or file for proof in your progress.";

                return RedirectToPage(new { id = jobTicketId });
            }
            // ============================================================
            // RULE 3:
            // Cancelled requires remarks
            // ============================================================
            if (status == JobTicketStatuses.Cancelled &&
                string.IsNullOrWhiteSpace(remarks))
            {
                ErrorMessage =
                    "Please provide a reason in the remarks before cancelling this job order.";

                return RedirectToPage(new { id = jobTicketId });
            }

            // ============================================================
            // RULE 4:
            // Rescheduled requires remarks explaining why, and — same as a
            // manager-triggered reschedule — archives whatever remarks/photos
            // this cycle already had, so the technician starts fresh.
            // ============================================================
            bool isNewReschedule = status == JobTicketStatuses.Rescheduled
                && ticket.Status != JobTicketStatuses.Rescheduled;

            if (isNewReschedule && string.IsNullOrWhiteSpace(remarks))
            {
                ErrorMessage =
                    "Please provide a reason in the remarks before marking this job order Rescheduled.";

                return RedirectToPage(new { id = jobTicketId });
            }

            // ============================================================
            // SAVE STATUS
            // ============================================================

            if (isNewReschedule)
            {
                var history = new JobTicketRescheduleHistory
                {
                    JobTicketID = ticket.JobTicketID,
                    OldServiceDate = ticket.ServiceDate,
                    NewServiceDate = ticket.ServiceDate,
                    Reason = remarks!.Trim(),
                    PreviousStatus = ticket.Status,
                    PreviousRemarks = ticket.Remarks,
                    DateChanged = DateTime.Now
                };

                var currentSubmissions = ticket.Submissions
                    .Where(s => s.RescheduleHistoryID == null)
                    .ToList();

                foreach (var sub in currentSubmissions)
                {
                    history.ArchivedSubmissions.Add(sub);
                }

                _context.JobTicketRescheduleHistories.Add(history);

                ticket.Status = JobTicketStatuses.Rescheduled;
                ticket.Remarks = null;
            }
            else
            {
                ticket.Status = status;

                ticket.Remarks = string.IsNullOrWhiteSpace(remarks)
                    ? null
                    : remarks.Trim();
            }

            await _context.SaveChangesAsync();

            return RedirectToPage("./Index");
        }

        public async Task<IActionResult> OnGetDownloadAsync(int submissionId)
        {
            var submission = await _context.JobTicketSubmissions.FindAsync(submissionId);
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
    }
}
