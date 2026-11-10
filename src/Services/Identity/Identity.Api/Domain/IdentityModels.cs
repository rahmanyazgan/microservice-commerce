using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using BuildingBlocks.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace Identity.Api.Domain;

public sealed class AppUser
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? PasswordHash { get; set; }
    public string Role { get; set; } = "Customer"; // "Admin" | "Customer"
    public string Provider { get; set; } = "Local"; // "Local" | "Google"
    public string? GoogleSubject { get; set; }
    public string? AvatarUrl { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastLoginAt { get; set; }
}

public sealed class IdentityDbContext(DbContextOptions<IdentityDbContext> options) : DbContext(options)
{
    public DbSet<AppUser> Users => Set<AppUser>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AppUser>(b =>
        {
            b.ToTable("Users");
            b.HasKey(x => x.Id);
            b.Property(x => x.Email).HasMaxLength(160).IsRequired();
            b.Property(x => x.FullName).HasMaxLength(120).IsRequired();
            b.Property(x => x.PasswordHash).HasMaxLength(300);
            b.Property(x => x.Role).HasMaxLength(32).IsRequired();
            b.Property(x => x.Provider).HasMaxLength(32).IsRequired();
            b.Property(x => x.GoogleSubject).HasMaxLength(128);
            b.Property(x => x.AvatarUrl).HasMaxLength(400);

            b.HasIndex(x => x.Email).IsUnique().HasDatabaseName("UX_Users_Email");
            b.HasIndex(x => x.GoogleSubject)
             .HasFilter("[GoogleSubject] IS NOT NULL")
             .HasDatabaseName("IX_Users_GoogleSubject");
        });
    }
}

public static class PasswordHasher
{
    private const int SaltSize = 16;
    private const int KeySize = 32;
    private const int Iterations = 100_000;

    public static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, KeySize);
        return $"{Convert.ToBase64String(salt)}.{Convert.ToBase64String(key)}";
    }

    public static bool Verify(string password, string? hash)
    {
        if (string.IsNullOrWhiteSpace(hash)) return false;
        var parts = hash.Split('.');
        if (parts.Length != 2) return false;

        var salt = Convert.FromBase64String(parts[0]);
        var expected = Convert.FromBase64String(parts[1]);
        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, KeySize);
        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }
}

public sealed class JwtTokenGenerator(IConfiguration configuration)
{
    public (string Token, DateTime ExpiresAt) Generate(AppUser user)
    {
        var secret = configuration["Jwt:SigningKey"] ?? JwtAuthConstants.DefaultSigningKey;
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var expires = DateTime.UtcNow.AddHours(12);

        var claims = new List<Claim>
        {
            new("sub", user.Id.ToString("N")),
            new("email", user.Email),
            new("name", user.FullName),
            new("role", user.Role),
            new("provider", user.Provider)
        };

        var token = new JwtSecurityToken(
            issuer: JwtAuthConstants.Issuer,
            audience: JwtAuthConstants.Audience,
            claims: claims,
            expires: expires,
            signingCredentials: creds);

        return (new JwtSecurityTokenHandler().WriteToken(token), expires);
    }
}

