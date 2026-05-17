using System.ComponentModel.DataAnnotations;

namespace MovieApi.DTOs
{
    // Reviews
    public class MovieReviewDto
    {
        public Guid Id { get; set; }
        public Guid MovieId { get; set; }
        public Guid UserId { get; set; }
        public decimal Rating { get; set; }
        public string? Review { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class CreateReviewDto
    {
        [Range(0, 10)]
        public decimal Rating { get; set; }
        public string? Review { get; set; }
    }

    // Favorites
    public class FavoriteMovieDto
    {
        public int Id { get; set; }
        public Guid MovieId { get; set; }
        public string MovieTitle { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
    }

    // Movie Lists
    public class MovieListDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public bool IsPublic { get; set; }
        public List<MovieListItemDto> Items { get; set; } = new();
    }

    public class CreateMovieListDto
    {
        [Required]
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public bool IsPublic { get; set; } = true;
    }

    public class MovieListItemDto
    {
        public Guid MovieId { get; set; }
        public string MovieTitle { get; set; } = string.Empty;
        public int Position { get; set; }
    }
}
