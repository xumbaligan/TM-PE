using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using TM_PE.Data;

namespace TM_PE.Pages.Manager.JobTickets
{
    public class IndexModel : PageModel
    {
        private readonly AppDbContext _context;

        public IndexModel(AppDbContext context)
        {
            _context = context;
        }

        public IList<Model.JobTicket> JobTicket { get; set; } = default!;

        public async Task OnGetAsync()
        {
            JobTicket = await _context.JobTickets
                .Include(t => t.Assignments)
                    .ThenInclude(a => a.Employee)
                .Include(t => t.Submissions)
                .OrderByDescending(t => t.DateCreated)
                .ToListAsync();
        }
    }
}
