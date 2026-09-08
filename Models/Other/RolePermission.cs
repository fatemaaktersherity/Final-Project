using System.ComponentModel.DataAnnotations;

namespace PharmacyV2.Models.Other
{
    public class RolePermission
    {
        public int Id { get; set; }

        public int RoleId { get; set; }
        public Role Role { get; set; } = null!;

        [Required, MaxLength(100)]
        public string Module { get; set; } = string.Empty;   

        public bool CanView { get; set; }
        public bool CanCreate { get; set; }
        public bool CanEdit { get; set; }
        public bool CanDelete { get; set; }
    }
}