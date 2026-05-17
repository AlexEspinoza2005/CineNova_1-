using System.ComponentModel.DataAnnotations;

namespace MovieApi.Models.ViewModels
{
    public class ProfileViewModel
    {
        public Guid Id { get; set; }
        public string Email { get; set; } = string.Empty;

        [Display(Name = "Nombre de Usuario")]
        public string? Username { get; set; }

        [Required(ErrorMessage = "El nombre completo es obligatorio")]
        [Display(Name = "Nombre Completo")]
        public string? FullName { get; set; }

        [Display(Name = "Biografía")]
        public string? Bio { get; set; }

        [Display(Name = "URL de Avatar")]
        public string? AvatarUrl { get; set; }

        [Display(Name = "País")]
        public string? Country { get; set; }

        [Display(Name = "Ciudad")]
        public string? City { get; set; }

        [Display(Name = "Géneros Favoritos")]
        public string[]? FavoriteGenres { get; set; }

        public DateTime CreatedAt { get; set; }

        // Data for Tabs
        public List<Movie> MyMovies { get; set; } = new();
        public List<FavoriteMovie> Favorites { get; set; } = new();
        public List<MovieList> MyLists { get; set; } = new();
    }

    public class EditProfileViewModel
    {
        [Display(Name = "Nombre de Usuario")]
        public string? Username { get; set; }

        [Required(ErrorMessage = "El nombre completo es obligatorio")]
        [Display(Name = "Nombre Completo")]
        public string? FullName { get; set; }

        [Display(Name = "Biografía")]
        public string? Bio { get; set; }

        [Display(Name = "URL de Avatar")]
        public string? AvatarUrl { get; set; }

        [Display(Name = "País")]
        public string? Country { get; set; }

        [Display(Name = "Ciudad")]
        public string? City { get; set; }

        [Display(Name = "Géneros Favoritos (separados por coma)")]
        public string? FavoriteGenresString { get; set; }

        [Display(Name = "Subir Avatar")]
        public Microsoft.AspNetCore.Http.IFormFile? ImageFile { get; set; }
    }
}
