using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace BuildingBlocks.Web;

public static class JwtAuthConstants
{
    public const string Issuer = "ETicaret.Identity";
    public const string Audience = "ETicaret.Clients";
    public const string DefaultSigningKey = "ETicaret-Super-Secret-Jwt-Signing-Key-2026-Min32Bytes!";
    public const string AdminPolicy = "AdminOnly";
}

public static class JwtAuthExtensions
{
    public static IServiceCollection AddStandardJwtAuth(this IServiceCollection services, IConfiguration configuration)
    {
        var secret = configuration["Jwt:SigningKey"] ?? JwtAuthConstants.DefaultSigningKey;
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = JwtAuthConstants.Issuer,
                    ValidateAudience = true,
                    ValidAudience = JwtAuthConstants.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = key,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromMinutes(1),
                    NameClaimType = "name",
                    RoleClaimType = "role"
                };
            });

        services.AddAuthorizationBuilder()
            .AddPolicy(JwtAuthConstants.AdminPolicy, policy => policy.RequireClaim("role", "Admin"));

        return services;
    }

    public static string? GetUserId(this ClaimsPrincipal user)
        => user.FindFirstValue("sub") ?? user.FindFirstValue(ClaimTypes.NameIdentifier);
}

