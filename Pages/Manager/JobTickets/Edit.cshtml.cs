using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using TM_PE.Data;
using TM_PE.Model;

namespace TM_PE.Pages.Manager.JobTickets
{
    public class EditModel : PageModel
    {
        private readonly AppDbContext _context;

        public EditModel(AppDbContext context)
        {
            _context = context;
        }

        [BindProperty]
        public Model.JobTicket JobTicket { get; set; } = default!;

        public int[] FiberPlanOptions { get; set; } = FiberPlans.Allowed;

        public string[] JobTypeOptions { get; set; } = JobTypes.Allowed;

        public async Task<IActionResult> OnGetAsync(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var jobTicket = await _context.JobTickets
                .Include(t => t.Assignments)
                    .ThenInclude(a => a.Employee)
                .FirstOrDefaultAsync(m => m.JobTicketID == id);

            if (jobTicket == null)
            {
                return NotFound();
            }

            // Completed (and Closed) tickets are locked — no further edits allowed.
            if (jobTicket.IsLockedFromEditing)
            {
                return RedirectToPage("Details", new { id });
            }

            JobTicket = jobTicket;

            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            // Auto-generated / assignment-related fields are not editable from this page
            ModelState.Remove("JobTicket.TicketNumber");
            ModelState.Remove("JobTicket.Assignments");
            ModelState.Remove("JobTicket.Submissions");
            // FiberPlan/Description are conditionally required depending on JobType,
            // so their built-in validation attributes are skipped in favor of manual checks below.
            ModelState.Remove("JobTicket.FiberPlan");
            ModelState.Remove("JobTicket.Description");
            // Client Full Name / Primary / Secondary Number are only collected for
            // non-Maintenance jobs, so their built-in [Required] attributes are skipped
            // in favor of the manual, JobType-aware checks below.
            ModelState.Remove("JobTicket.ClientFullName");
            ModelState.Remove("JobTicket.PrimaryNumber");
            ModelState.Remove("JobTicket.SecondaryNumber");

            var ticket = await _context.JobTickets.FirstOrDefaultAsync(t => t.JobTicketID == JobTicket.JobTicketID);

            if (ticket == null)
            {
                return NotFound();
            }

            // Completed (and Closed) tickets are locked — no further edits allowed,
            // even if this page was submitted directly.
            if (ticket.IsLockedFromEditing)
            {
                return RedirectToPage("Details", new { id = ticket.JobTicketID });
            }

            if (!JobTypes.Allowed.Contains(JobTicket.JobType))
                ModelState.AddModelError("JobTicket.JobType", "Please select a valid job type.");

            if (JobTicket.JobType == JobTypes.Installation)
            {
                if (JobTicket.FiberPlan == null || !FiberPlans.Allowed.Contains(JobTicket.FiberPlan.Value))
                    ModelState.AddModelError("JobTicket.FiberPlan", "Please select a valid fiber plan.");

                JobTicket.Description = null;
            }
            else if (JobTicket.JobType == JobTypes.Repair || JobTicket.JobType == JobTypes.Maintenance || JobTicket.JobType == JobTypes.Inspection)
            {
                if (string.IsNullOrWhiteSpace(JobTicket.Description))
                    ModelState.AddModelError("JobTicket.Description", "Please provide a description.");

                JobTicket.FiberPlan = null;
            }

            // Maintenance jobs don't collect client contact info — clear whatever was
            // submitted instead of requiring it. Every other job type still requires
            // Client Full Name and Primary Number.
            if (JobTicket.JobType == JobTypes.Maintenance)
            {
                JobTicket.ClientFullName = string.Empty;
                JobTicket.PrimaryNumber = string.Empty;
                JobTicket.SecondaryNumber = null;
            }
            else
            {
                if (string.IsNullOrWhiteSpace(JobTicket.ClientFullName))
                    ModelState.AddModelError("JobTicket.ClientFullName", "Client full name is required.");

                if (string.IsNullOrWhiteSpace(JobTicket.PrimaryNumber))
                    ModelState.AddModelError("JobTicket.PrimaryNumber", "Primary number is required.");
                else if (!System.Text.RegularExpressions.Regex.IsMatch(JobTicket.PrimaryNumber, @"^[0-9+\-\s()]{7,20}$"))
                    ModelState.AddModelError("JobTicket.PrimaryNumber", "Enter a valid contact number.");

                if (!string.IsNullOrWhiteSpace(JobTicket.SecondaryNumber) &&
                    !System.Text.RegularExpressions.Regex.IsMatch(JobTicket.SecondaryNumber, @"^[0-9+\-\s()]{7,20}$"))
                    ModelState.AddModelError("JobTicket.SecondaryNumber", "Enter a valid contact number.");
            }

            if (!ModelState.IsValid)
            {
                var reload = await _context.JobTickets
                    .Include(t => t.Assignments).ThenInclude(a => a.Employee)
                    .AsNoTracking()
                    .FirstOrDefaultAsync(t => t.JobTicketID == JobTicket.JobTicketID);

                if (reload != null)
                {
                    JobTicket.Assignments = reload.Assignments;
                    JobTicket.TicketNumber = reload.TicketNumber;
                    JobTicket.DateCreated = reload.DateCreated;
                }

                return Page();
            }

            // Assignees (and who leads them) are intentionally locked after creation —
            // only the ticket's own details and status can change here.
            ticket.JobType = JobTicket.JobType;
            ticket.ClientFullName = JobTicket.ClientFullName;
            ticket.PrimaryNumber = JobTicket.PrimaryNumber;
            ticket.SecondaryNumber = JobTicket.SecondaryNumber;
            ticket.FiberPlan = JobTicket.FiberPlan;
            ticket.Description = JobTicket.Description;
            ticket.ServiceDate = JobTicket.ServiceDate;
            ticket.LocationAddress = JobTicket.LocationAddress;
            ticket.NearestLandmark = JobTicket.NearestLandmark;
            ticket.Status = JobTicket.Status;

            await _context.SaveChangesAsync();

            return RedirectToPage("Index");
        }
    }
}
