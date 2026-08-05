using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TM_PE.Model
{
    // Fiber subscription plans a job ticket can be created for (monthly price, in PHP).
    public static class FiberPlans
    {
        public static readonly int[] Allowed = { 999, 1199, 1499, 1999, 2499, 2999 };
    }

    // The kind of job a ticket covers. Drives which fields appear on the form.
    public static class JobTypes
    {
        public const string Installation = "Installation";
        public const string Repair = "Repair";
        public const string Maintenance = "Maintenance";

        public static readonly string[] Allowed = { Installation, Repair, Maintenance };
    }

    // Job ticket lifecycle. "Approved" is a manager-only, terminal state — once a
    // ticket is Approved it is locked from further edits.
    public static class JobTicketStatuses
    {
        public const string Pending = "Pending";
        public const string InProgress = "In Progress";
        public const string Completed = "Completed";
        public const string Cancelled = "Cancelled";
        public const string Approved = "Approved";

        public static readonly string[] Allowed = { Pending, InProgress, Completed, Cancelled, Approved };
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

        // One of JobTypes.Allowed (Installation / Repair / Maintenance)
        [Required(ErrorMessage = "Job type is required.")]
        [StringLength(20)]
        public string JobType { get; set; } = string.Empty;

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

        // Only used when JobType == Installation. One of FiberPlans.Allowed.
        public int? FiberPlan { get; set; }

        // Only used when JobType == Repair or Maintenance.
        [StringLength(500)]
        public string? Description { get; set; }

        // Meaning depends on JobType: install date, repair date, or maintenance date.
        [Required(ErrorMessage = "Date is required.")]
        public DateTime ServiceDate { get; set; } = DateTime.Now;

        // Auto-filled from the pinned map location (reverse-geocoded address)
        [Required(ErrorMessage = "Please pin the job location on the map.")]
        [StringLength(300)]
        public string LocationAddress { get; set; } = string.Empty;

        [Column(TypeName = "decimal(9,6)")]
        public decimal Latitude { get; set; }

        [Column(TypeName = "decimal(9,6)")]
        public decimal Longitude { get; set; }

        // Always starts as "Pending" on creation; never exposed on the Create form.
        // Valid values: Pending, In Progress, Completed, Cancelled, Approved
        public string Status { get; set; } = JobTicketStatuses.Pending;

        [NotMapped]
        public bool IsApproved => Status == JobTicketStatuses.Approved;

        // Label shown above the ServiceDate field/value, based on JobType.
        [NotMapped]
        public string ServiceDateLabel => JobType switch
        {
            JobTypes.Repair => "Date of Repair",
            JobTypes.Maintenance => "Date of Maintenance",
            _ => "Date of Installation"
        };

        // Navigation
        public ICollection<JobTicketAssignment> Assignments { get; set; }
            = new List<JobTicketAssignment>();

        public ICollection<JobTicketSubmission> Submissions { get; set; }
            = new List<JobTicketSubmission>();
    }
}
