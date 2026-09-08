using PharmacyV2.Models.Other;

namespace PharmacyV2.Services
{
    public interface ITokenService
    {
        // Builds a signed JWT for the given user (with their role, if any,
        // baked in as a claim) and returns both the token and its expiry.
        (string Token, DateTime ExpiresAtUtc) CreateToken(AppUser user);
    }
}
