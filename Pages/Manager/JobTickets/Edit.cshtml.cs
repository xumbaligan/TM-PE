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

            JobTicket = jobTicket;

            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            // Auto-generated / assignment-related fields are not editable from this page
            ModelState.Remove("JobTicket.TicketNumber");
            ModelState.Remove("JobTicket.Assignments");
            ModelState.Remove("JobTicket.Submissions");

            if (!FiberPlans.Allowed.Contains(JobTicket.FiberPlan))
                ModelState.AddModelError("JobTicket.FiberPlan", "Please select a valid fiber plan.");

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

            var ticket = await _context.JobTickets.FirstOrDefaultAsync(t => t.JobTicketID == JobTicket.JobTicketID);

            if (ticket == null)
            {
                return NotFound();
            }

            // Assignees (and who leads them) are intentionally locked after creation —
            // only the ticket's own details and status can change here.
            ticket.JobName = JobTicket.JobName;
            ticket.ClientFullName = JobTicket.ClientFullName;
            ticket.PrimaryNumber = JobTicket.PrimaryNumber;
            ticket.SecondaryNumber = JobTicket.SecondaryNumber;
            ticket.FiberPlan = JobTicket.FiberPlan;
            ticket.InstallationDate = JobTicket.InstallationDate;
            ticket.LocationAddress = JobTicket.LocationAddress;
            ticket.Latitude = JobTicket.Latitude;
            ticket.Longitude = JobTicket.Longitude;
            ticket.Status = JobTicket.Status;

            await _context.SaveChangesAsync();

            return RedirectToPage("Index");
        }
    }
}
