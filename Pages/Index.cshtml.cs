//using Microsoft.AspNetCore.Mvc.RazorPages;
//using TM_PE.Data;

//namespace TM_PE.Pages;

//public class IndexModel : PageModel
//{
//    private readonly AppDbContext _db;
//    public IndexModel(AppDbContext db) => _db = db;

//    public int DepartmentCount { get; set; }
//    public int EmployeeCount { get; set; }
//    public int CriteriaCount { get; set; }

//    public void OnGet()
//    {
//        DepartmentCount = _db.Departments.Count();
//        EmployeeCount = _db.Employees.Count();
//        CriteriaCount = _db.Criteria.Count(c => c.IsActive);
//    }
//}
