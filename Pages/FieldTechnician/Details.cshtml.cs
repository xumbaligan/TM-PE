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

        public static readonly string[] StatusOptions = { "Pending", "In Progress", "Completed", "Cancelled" };

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

            ticket.Submissions = ticket.Submissions.OrderByDescending(s => s.DateSubmitted).ToList();
            JobTicket = ticket;

            return Page();
        }

        // ---------------------------------------------------------------
        // FILE SUBMISSION (leader only)
        // ---------------------------------------------------------------
        public async Task<IActionResult> OnPostUploadSubmissionAsync(int jobTicketId, IFormFile submissionFile, string? caption)
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
                ErrorMessage = "Only the team leader can upload files for this job order.";
                return RedirectToPage(new { id = jobTicketId });
            }

            if (ticket.Status == JobTicketStatuses.Approved)
            {
                ErrorMessage = "This job order has been approved and can no longer be updated.";
                return RedirectToPage(new { id = jobTicketId });
            }

            if (submissionFile == null || submissionFile.Length == 0)
            {
                ErrorMessage = "Please choose a file to upload.";
                return RedirectToPage(new { id = jobTicketId });
            }

            var uploadsRoot = Path.Combine(_env.WebRootPath, "uploads", "jobticket-submissions");
            Directory.CreateDirectory(uploadsRoot);

            var safeFileName = $"{jobTicketId}_{Guid.NewGuid():N}_{Path.GetFileName(submissionFile.FileName)}";
            var fullPath = Path.Combine(uploadsRoot, safeFileName);

            using (var stream = new FileStream(fullPath, FileMode.Create))
            {
                await submissionFile.CopyToAsync(stream);
            }

            var relativePath = Path.Combine("uploads", "jobticket-submissions", safeFileName).Replace("\\", "/");

            _context.JobTicketSubmissions.Add(new JobTicketSubmission
            {
                JobTicketID = jobTicketId,
                EmployeeID = employeeId.Value,
                FileName = submissionFile.FileName,
                FilePath = relativePath,
                Caption = string.IsNullOrWhiteSpace(caption) ? null : caption.Trim(),
                DateSubmitted = DateTime.Now
            });

            await _context.SaveChangesAsync();

            return RedirectToPage(new { id = jobTicketId });
        }

        // ---------------------------------------------------------------
        // STATUS CHANGE (leader only)
        // ---------------------------------------------------------------
        public async Task<IActionResult> OnPostUpdateStatusAsync(int jobTicketId, string status)
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
                ErrorMessage = "Only the team leader can change the status of this job order.";
                return RedirectToPage(new { id = jobTicketId });
            }

            if (ticket.Status == JobTicketStatuses.Approved)
            {
                ErrorMessage = "This job order has been approved and can no longer be updated.";
                return RedirectToPage(new { id = jobTicketId });
            }

            if (!StatusOptions.Contains(status))
            {
                ErrorMessage = "Invalid status.";
                return RedirectToPage(new { id = jobTicketId });
            }

            ticket.Status = status;
            await _context.SaveChangesAsync();

            return RedirectToPage(new { id = jobTicketId });
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
