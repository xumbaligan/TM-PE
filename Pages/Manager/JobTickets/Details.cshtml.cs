using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using TM_PE.Data;
using TM_PE.Model;

namespace TM_PE.Pages.Manager.JobTickets
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

        public Model.JobTicket JobTicket { get; set; } = default!;

        public static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp"
        };

        public async Task<IActionResult> OnGetAsync(int? id)
        {
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

            ticket.Submissions = ticket.Submissions.OrderByDescending(s => s.DateSubmitted).ToList();

            JobTicket = ticket;

            return Page();
        }

        // Manager can close out a job ticket once work is finished.
        public async Task<IActionResult> OnPostCloseAsync(int id)
        {
            var ticket = await _context.JobTickets.FindAsync(id);
            if (ticket != null && ticket.Status != JobTicketStatuses.Completed)
            {
                ticket.Status = JobTicketStatuses.Completed;
                await _context.SaveChangesAsync();
            }

            return RedirectToPage(new { id });
        }

        // Manager approves a completed job ticket. Once approved, the ticket is
        // locked from further edits.
        public async Task<IActionResult> OnPostApproveAsync(int id)
        {
            var ticket = await _context.JobTickets.FindAsync(id);
            if (ticket != null && ticket.Status == JobTicketStatuses.Completed)
            {
                ticket.Status = JobTicketStatuses.Approved;
                await _context.SaveChangesAsync();
            }

            return RedirectToPage(new { id });
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
