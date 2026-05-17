using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MovieApi.Models
{
    [Table("movie_list_items", Schema = "public")]
    public class MovieListItem
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Column("list_id")]
        public Guid ListId { get; set; }

        [Column("movie_id")]
        public Guid MovieId { get; set; }

        [Column("position")]
        public int Position { get; set; } = 0;

        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [ForeignKey("ListId")]
        public MovieList? MovieList { get; set; }

        [ForeignKey("MovieId")]
        public Movie? Movie { get; set; }
    }
}
