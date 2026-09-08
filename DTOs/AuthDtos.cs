using System.ComponentModel.DataAnnotations;

namespace PharmacyV2.DTOs
{
    // Body for POST /api/auth/register
    public class RegisterRequestDto
    {
        [Required, MaxLength(100)]
        public string Username { get; set; } = string.Empty;

        [Required, MaxLength(200)]
        public string FullName { get; set; } = string.Empty;

        [MaxLength(100), EmailAddress]
        public string? Email { get; set; }

        [MaxLength(20)]
        public string? Phone { get; set; }

        [Required, MinLength(6)]
        public string Password { get; set; } = string.Empty;

        // Optional: assign a role at registration time (e.g. "Admin", "Cashier").
        // If omitted the user is created with no role and can be assigned one later.
        public int? RoleId { get; set; }
    }

    // Body for POST /api/auth/login
    public class LoginRequestDto
    {
        [Required]
        public string Username { get; set; } = string.Empty;

        [Required]
        public string Password { get; set; } = string.Empty;
    }

    // Returned by login — the JWT the client must send back as
    // "Authorization: Bearer {token}" on every subsequent request.
    public class AuthResponseDto
    {
        public int UserId { get; set; }
        public string Username { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string? RoleName { get; set; }
        public string Token { get; set; } = string.Empty;
        public DateTime ExpiresAtUtc { get; set; }
    }

    // Returned by register — no token. Client must call /api/auth/login
    // afterwards to obtain one.
    public class RegisterResponseDto
    {
        public int UserId { get; set; }
        public string Username { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string? RoleName { get; set; }
        public string Message { get; set; } = "Registration successful. Please log in to get an access token.";
    }

    // Returned by GET /api/auth/me
    public class UserProfileDto
    {
        public int Id { get; set; }
        public string Username { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string? Email { get; set; }
        public string? Phone { get; set; }
        public bool IsActive { get; set; }
        public string? RoleName { get; set; }
    }
}