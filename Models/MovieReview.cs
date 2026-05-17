using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MovieApi.Models
{
    [Table("movie_reviews", Schema = "public")]
    public class MovieReview
    {
        [Key]
        [Column("id")]
        public Guid Id { get; set; }

        [Column("movie_id")]
        public Guid MovieId { get; set; }

        [Column("user_id")]
        public Guid UserId { get; set; }

        [Required]
        [Column("rating")]
        public decimal Rating { get; set; }

        [Column("review")]
        public string? Review { get; set; }

        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [Column("updated_at")]
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        [ForeignKey("MovieId")]
        public Movie? Movie { get; set; }

        [ForeignKey("UserId")]
        public Profile? Profile { get; set; }
    }
}
