using System.ComponentModel.DataAnnotations;

namespace PharmacyV2.Models.Other
{
    public class Role
    {
        public int Id { get; set; }

        [Required, MaxLength(100)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(500)]
        public string? Description { get; set; }

        public bool IsActive { get; set; } = true;

        public ICollection<AppUser> Users { get; set; } = new List<AppUser>();
        public ICollection<RolePermission> Permissions { get; set; } = new List<RolePermission>();
    }
}
