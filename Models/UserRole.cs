using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MovieApi.Models
{
    [Table("user_roles", Schema = "public")]
    public class UserRole
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Column("user_id")]
        public Guid UserId { get; set; }

        [Column("role_id")]
        public int RoleId { get; set; }

        [Column("assigned_at")]
        public DateTime AssignedAt { get; set; } = DateTime.UtcNow;

        [ForeignKey("UserId")]
        public Profile? Profile { get; set; }

        [ForeignKey("RoleId")]
        public Role? Role { get; set; }
    }
}
