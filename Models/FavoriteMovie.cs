using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MovieApi.Models
{
    [Table("favorite_movies", Schema = "public")]
    public class FavoriteMovie
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Column("user_id")]
        public Guid UserId { get; set; }

        [Column("movie_id")]
        public Guid MovieId { get; set; }

        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [ForeignKey("UserId")]
        public Profile? Profile { get; set; }

        [ForeignKey("MovieId")]
        public Movie? Movie { get; set; }
    }
}
