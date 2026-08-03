using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TM_PE.Model
{
    // Represents a picture/file the leader of the assigned field technicians uploaded
    // as proof of work for a job ticket. Populated by the (future) Field Technician
    // portal; the Manager interface only views these.
    [Table("tbl_jobticketsubmission")]
    public class JobTicketSubmission
    {
        [Key]
        public int JobTicketSubmissionID { get; set; }

        public int JobTicketID { get; set; }

        [ForeignKey(nameof(JobTicketID))]
        public JobTicket? JobTicket { get; set; }

        // The leader who uploaded the file.
        public int EmployeeID { get; set; }

        [ForeignKey(nameof(EmployeeID))]
        public Employee? Employee { get; set; }

        public string FileName { get; set; } = string.Empty;

        public string FilePath { get; set; } = string.Empty;

        [StringLength(300)]
        public string? Caption { get; set; }

        public DateTime DateSubmitted { get; set; } = DateTime.Now;
    }
}
