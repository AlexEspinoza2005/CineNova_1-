using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MovieApi.Models
{
    [Table("operation_logs", Schema = "public")]
    public class OperationLog
    {
        [Key]
        [Column("id")]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Column("user_id")]
        public Guid? UserId { get; set; }

        [Required]
        [Column("action")]
        public string Action { get; set; } = string.Empty;

        [Required]
        [Column("status")]
        public string Status { get; set; } = "success";

        [Column("error_message")]
        public string? ErrorMessage { get; set; }

        [Column("error_code")]
        public string? ErrorCode { get; set; }

        [Column("latency_ms")]
        public int? LatencyMs { get; set; }

        [Column("records_affected")]
        public int RecordsAffected { get; set; } = 0;

        [Column("metadata", TypeName = "jsonb")]
        public string? Metadata { get; set; }

        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [ForeignKey("UserId")]
        public Profile? Profile { get; set; }
    }
}
