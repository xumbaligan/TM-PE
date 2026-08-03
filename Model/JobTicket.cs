using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TM_PE.Model
{
    // Fiber subscription plans a job ticket can be created for (monthly price, in PHP).
    public static class FiberPlans
    {
        public static readonly int[] Allowed = { 999, 1199, 1499, 1999, 2499, 2999 };
    }

    [Table("tbl_jobticket")]
    public class JobTicket
    {
        [Key]
        public int JobTicketID { get; set; }

        [Required]
        [StringLength(20)]
        public string TicketNumber { get; set; } = string.Empty;

        public DateTime DateCreated { get; set; } = DateTime.Now;

        [Required(ErrorMessage = "Job name is required.")]
        [StringLength(150)]
        public string JobName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Client full name is required.")]
        [StringLength(100)]
        public string ClientFullName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Primary number is required.")]
        [StringLength(20)]
        [RegularExpression(@"^[0-9+\-\s()]{7,20}$", ErrorMessage = "Enter a valid contact number.")]
        public string PrimaryNumber { get; set; } = string.Empty;

        [StringLength(20)]
        [RegularExpression(@"^[0-9+\-\s()]{7,20}$", ErrorMessage = "Enter a valid contact number.")]
        public string? SecondaryNumber { get; set; }

        // One of FiberPlans.Allowed (999 / 1199 / 1499 / 1999 / 2499 / 2999)
        [Required(ErrorMessage = "Please select a fiber plan.")]
        public int FiberPlan { get; set; }

        [Required(ErrorMessage = "Date of installation is required.")]
        public DateTime InstallationDate { get; set; } = DateTime.Now;

        // Auto-filled from the pinned map location (reverse-geocoded address)
        [Required(ErrorMessage = "Please pin the job location on the map.")]
        [StringLength(300)]
        public string LocationAddress { get; set; } = string.Empty;

        [Column(TypeName = "decimal(9,6)")]
        public decimal Latitude { get; set; }

        [Column(TypeName = "decimal(9,6)")]
        public decimal Longitude { get; set; }

        // Always starts as "Pending" on creation; never exposed on the Create form.
        // Valid values: Pending, In Progress, Completed, Cancelled
        public string Status { get; set; } = "Pending";

        // Navigation
        public ICollection<JobTicketAssignment> Assignments { get; set; }
            = new List<JobTicketAssignment>();

        public ICollection<JobTicketSubmission> Submissions { get; set; }
            = new List<JobTicketSubmission>();
    }
}
