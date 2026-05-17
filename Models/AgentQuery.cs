using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MovieApi.Models
{
    [Table("agent_queries", Schema = "public")]
    public class AgentQuery
    {
        [Key]
        [Column("id")]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Column("user_id")]
        public Guid? UserId { get; set; }

        [Required]
        [Column("question")]
        public string Question { get; set; } = string.Empty;

        [Column("answer")]
        public string? Answer { get; set; }

        [Column("response_latency_ms")]
        public int? ResponseLatencyMs { get; set; }

        [Column("similarity_score")]
        public decimal? SimilarityScore { get; set; }

        [Column("results_count")]
        public int ResultsCount { get; set; } = 0;

        [Column("has_results")]
        public bool HasResults { get; set; } = true;

        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [ForeignKey("UserId")]
        public Profile? Profile { get; set; }
    }
}
