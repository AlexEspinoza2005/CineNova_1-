using System.ComponentModel.DataAnnotations;

namespace MovieApi.Models.ViewModels
{
    public class MovieViewModel
    {
        public Guid Id { get; set; }
        public Guid UserId { get; set; }
        
        [Required(ErrorMessage = "El título es obligatorio")]
        [Display(Name = "Título")]
        public string Title { get; set; } = string.Empty;

        [Required(ErrorMessage = "El género es obligatorio")]
        [Display(Name = "Género")]
        public string Genre { get; set; } = string.Empty;

        [Required(ErrorMessage = "El director es obligatorio")]
        [Display(Name = "Director")]
        public string Director { get; set; } = string.Empty;

        [Required(ErrorMessage = "El año es obligatorio")]
        [Range(1888, 2100, ErrorMessage = "Año fuera de rango")]
        [Display(Name = "Año")]
        public int? Year { get; set; }

        [Required(ErrorMessage = "La sinopsis es obligatoria")]
        [Display(Name = "Sinopsis")]
        public string Synopsis { get; set; } = string.Empty;

        [Display(Name = "Duración (min)")]
        public int? DurationMin { get; set; }

        [Display(Name = "URL de Portada")]
        public string? CoverUrl { get; set; }

        [Display(Name = "Subir Imagen")]
        public Microsoft.AspNetCore.Http.IFormFile? ImageFile { get; set; }

        public int? InsertionLatencyMs { get; set; }
        public string InsertionStatus { get; set; } = "success";
        public DateTime CreatedAt { get; set; }

        // Details specific
        public List<MovieReview>? Reviews { get; set; }
        public decimal AverageRating { get; set; }
    }

    public class MovieListViewModel
    {
        public List<Movie> Movies { get; set; } = new();
        public int CurrentPage { get; set; }
        public int TotalPages { get; set; }
    }
}
