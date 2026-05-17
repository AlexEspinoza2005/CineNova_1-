using System.ComponentModel.DataAnnotations;

namespace MovieApi.DTOs
{
    public class MovieDto
    {
        public Guid Id { get; set; }
        public Guid UserId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Genre { get; set; } = string.Empty;
        public string Director { get; set; } = string.Empty;
        public int Year { get; set; }
        public string Synopsis { get; set; } = string.Empty;
        public int? DurationMin { get; set; }
        public string? CoverUrl { get; set; }
        public string? VectorId { get; set; }
        public int? InsertionLatencyMs { get; set; }
        public string InsertionStatus { get; set; } = "success";
        public DateTime CreatedAt { get; set; }
    }

    public class CreateMovieDto
    {
        [Required]
        public string Title { get; set; } = string.Empty;
        [Required]
        public string Genre { get; set; } = string.Empty;
        [Required]
        public string Director { get; set; } = string.Empty;
        [Required]
        [Range(1888, 2100)]
        public int Year { get; set; }
        [Required]
        public string Synopsis { get; set; } = string.Empty;
        public int? DurationMin { get; set; }
        public string? CoverUrl { get; set; }
    }

    public class UpdateMovieDto
    {
        public string? Title { get; set; }
        public string? Genre { get; set; }
        public string? Director { get; set; }
        public int? Year { get; set; }
        public string? Synopsis { get; set; }
        public int? DurationMin { get; set; }
        public string? CoverUrl { get; set; }
    }
}
