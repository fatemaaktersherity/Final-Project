using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PharmacyV2.Data;
using PharmacyV2.DTOs;
using PharmacyV2.Models.Other;
using PharmacyV2.Services;
using System.Security.Claims;

namespace PharmacyV2.Controllers
{
    // Public authentication endpoints. Everything else in the solution is
    // [Authorize] by default (see Program.cs), so Register/Login are the two
    // doors into the API from Postman.
    [Route("api/[controller]")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        private readonly PharmacyDbContext _db;
        private readonly ITokenService _tokenService;
        private readonly IPasswordHasher<AppUser> _passwordHasher;

        public AuthController(PharmacyDbContext db, ITokenService tokenService, IPasswordHasher<AppUser> passwordHasher)
        {
            _db = db;
            _tokenService = tokenService;
            _passwordHasher = passwordHasher;
        }

        // POST api/auth/register
        // Creates a new AppUser with a securely hashed password (never store
        // plain text). Does NOT return a token — the client must call
        // POST /api/auth/login afterwards to obtain one.
        [Authorize(Roles = "Admin")]
        [HttpPost("register")]
        public async Task<ActionResult<RegisterResponseDto>> Register([FromBody] RegisterRequestDto dto)
        {
            if (await _db.AppUsers.AnyAsync(u => u.Username == dto.Username))
                return Conflict(new { message = $"Username '{dto.Username}' is already taken." });

            if (dto.RoleId is not null && !await _db.Roles.AnyAsync(r => r.Id == dto.RoleId))
                return BadRequest(new { message = $"Role {dto.RoleId} does not exist." });

            var user = new AppUser
            {
                Username = dto.Username,
                FullName = dto.FullName,
                Email = dto.Email,
                Phone = dto.Phone,
                RoleId = dto.RoleId,
                IsActive = true
            };
            // PasswordHasher salts + hashes internally (PBKDF2) — PasswordHash
            // never contains the raw password.
            user.PasswordHash = _passwordHasher.HashPassword(user, dto.Password);

            _db.AppUsers.Add(user);
            await _db.SaveChangesAsync();

            await _db.Entry(user).Reference(u => u.Role).LoadAsync();

            return StatusCode(201, new RegisterResponseDto
            {
                UserId = user.Id,
                Username = user.Username,
                FullName = user.FullName,
                RoleName = user.Role?.Name
            });
        }

        // POST api/auth/login
        // Verifies the username/password against the stored hash and, if
        // valid, issues a fresh JWT. This is the endpoint you call in Postman
        // to get the Bearer token for every other request.
        [AllowAnonymous]
        [HttpPost("login")]
        public async Task<ActionResult<AuthResponseDto>> Login([FromBody] LoginRequestDto dto)
        {
            var user = await _db.AppUsers
                .Include(u => u.Role)
                .FirstOrDefaultAsync(u => u.Username == dto.Username);

            // Same generic error for "no such user" and "wrong password" —
            // never reveal which one it was, that leaks valid usernames.
            if (user is null || !user.IsActive)
                return Unauthorized(new { message = "Invalid username or password." });

            var verifyResult = _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, dto.Password);
            if (verifyResult == PasswordVerificationResult.Failed)
                return Unauthorized(new { message = "Invalid username or password." });

            var (token, expires) = _tokenService.CreateToken(user);

            return Ok(new AuthResponseDto
            {
                UserId = user.Id,
                Username = user.Username,
                FullName = user.FullName,
                RoleName = user.Role?.Name,
                Token = token,
                ExpiresAtUtc = expires
            });
        }

        // GET api/auth/me
        // Requires a valid Bearer token. Reads the user id out of the JWT's
        // claims (set in TokenService) and returns their profile — handy in
        // Postman to sanity-check that a token actually works.
        [Authorize]
        [HttpGet("me")]
        public async Task<ActionResult<UserProfileDto>> Me()
        {
            var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdClaim, out var userId))
                return Unauthorized();

            var user = await _db.AppUsers
                .Include(u => u.Role)
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == userId);

            if (user is null)
                return NotFound();

            return Ok(new UserProfileDto
            {
                Id = user.Id,
                Username = user.Username,
                FullName = user.FullName,
                Email = user.Email,
                Phone = user.Phone,
                IsActive = user.IsActive,
                RoleName = user.Role?.Name
            });
        }
    }
}
