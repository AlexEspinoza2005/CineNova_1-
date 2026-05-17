using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MovieApi.Models
{
    [Table("movies", Schema = "public")]
    public class Movie
    {
        [Key]
        [Column("id")]
        public Guid Id { get; set; }

        [Column("user_id")]
        public Guid UserId { get; set; }

        [Required]
        [Column("title")]
        public string Title { get; set; } = string.Empty;

        [Required]
        [Column("genre")]
        public string Genre { get; set; } = string.Empty;

        [Required]
        [Column("director")]
        public string Director { get; set; } = string.Empty;

        [Column("year")]
        public int Year { get; set; }

        [Required]
        [Column("synopsis")]
        public string Synopsis { get; set; } = string.Empty;

        [Column("duration_min")]
        public int? DurationMin { get; set; }

        [Column("cover_url")]
        public string? CoverUrl { get; set; }

        [Column("vector_id")]
        public string? VectorId { get; set; }

        [Column("insertion_latency_ms")]
        public int? InsertionLatencyMs { get; set; }

        [Column("embedding_latency_ms")]
        public int? EmbeddingLatencyMs { get; set; }

        [Column("insertion_status")]
        public string InsertionStatus { get; set; } = "success";

        [Column("ingestion_date")]
        [DatabaseGenerated(DatabaseGeneratedOption.Computed)]
        public DateOnly IngestionDate { get; set; }

        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [Column("updated_at")]
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        [ForeignKey("UserId")]
        public Profile? Profile { get; set; }
    }
}
