using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using TM_PE.Model;
using TM_PE.Data;

namespace TM_PE.Pages.OfficeTask
{
    public class IndexModel : PageModel
    {
        private readonly AppDbContext _context;

        public IndexModel(AppDbContext context)
        {
            _context = context;
        }

        public IList<Model.OfficeTask> OfficeTask { get; set; } = default!;

        public async Task OnGetAsync()
        {
            OfficeTask = await _context.OfficeTasks
                .Include(t => t.Assignments)
                    .ThenInclude(a => a.Employee)
                .Include(t => t.Activities)
                .OrderByDescending(t => t.DateCreated)
                .ToListAsync();
        }

        public async Task<IActionResult> OnPostDeleteAsync(int id)
        {
            var task = await _context.OfficeTasks.FindAsync(id);

            if (task != null)
            {
                _context.OfficeTasks.Remove(task);
                await _context.SaveChangesAsync();
            }

            return RedirectToPage();
        }
    }
}
