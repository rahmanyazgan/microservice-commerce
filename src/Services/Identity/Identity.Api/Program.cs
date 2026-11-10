using System.Net.Http.Json;
using System.Security.Claims;
using BuildingBlocks.Web;
using Identity.Api.Domain;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.AddStandardServiceDefaults("Identity.Api");

var connStr = builder.Configuration.GetConnectionString("IdentityDb")
              ?? "Server=localhost;Database=ETicaret_IdentityDb;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True";

builder.Services.AddDbContext<IdentityDbContext>(o => o.UseSqlServer(connStr));
builder.Services.AddSingleton<JwtTokenGenerator>();
builder.Services.AddHttpClient("google-oauth", c => c.Timeout = TimeSpan.FromSeconds(5));

var app = builder.Build();
app.UseStandardServicePipeline();

await SeedUsersAsync(app.Services);

var group = app.MapGroup("/api/auth");

group.MapGet("/config", (IConfiguration cfg) => Results.Ok(new
{
    googleClientId = cfg["GoogleAuth:ClientId"] ?? "",
    databaseName = "ETicaret_IdentityDb (dbo.Users)",
    demoAccounts = new[]
    {
        new { email = "admin@minimagaza.com", password = "Admin123!", role = "Admin", label = "Sistem Yöneticisi (Admin)" },
        new { email = "musteri@minimagaza.com", password = "Musteri123!", role = "Customer", label = "Standart Müşteri" }
    }
}));

group.MapPost("/login", async (LoginRequest req, IdentityDbContext db, JwtTokenGenerator jwt, CancellationToken ct) =>
{
    var email = (req.Email ?? string.Empty).Trim().ToLowerInvariant();
    var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email, ct);
    if (user is null || !PasswordHasher.Verify(req.Password ?? string.Empty, user.PasswordHash))
        throw new DomainException("E-posta adresi veya şifre hatalı.", 400);

    if (!user.IsActive)
        throw new DomainException("Hesabınız pasife alınmış.", 403);

    user.LastLoginAt = DateTime.UtcNow;
    await db.SaveChangesAsync(ct);

    return Results.Ok(ToAuthResponse(user, jwt));
});

group.MapPost("/register", async (RegisterRequest req, IdentityDbContext db, JwtTokenGenerator jwt, CancellationToken ct) =>
{
    var email = (req.Email ?? string.Empty).Trim().ToLowerInvariant();
    if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
        throw new DomainException("Geçerli bir e-posta adresi giriniz.");
    if (string.IsNullOrWhiteSpace(req.FullName) || req.FullName.Trim().Length < 2)
        throw new DomainException("Ad Soyad en az 2 karakter olmalıdır.");
    if (string.IsNullOrWhiteSpace(req.Password) || req.Password.Length < 6)
        throw new DomainException("Şifre en az 6 karakter olmalıdır.");

    if (await db.Users.AnyAsync(u => u.Email == email, ct))
        throw new DomainException("Bu e-posta adresiyle kayıtlı bir üye zaten var.", 409);

    var user = new AppUser
    {
        Id = Guid.NewGuid(),
        Email = email,
        FullName = req.FullName.Trim(),
        PasswordHash = PasswordHasher.Hash(req.Password),
        Role = "Customer",
        Provider = "Local",
        IsActive = true,
        CreatedAt = DateTime.UtcNow,
        LastLoginAt = DateTime.UtcNow
    };

    db.Users.Add(user);
    await db.SaveChangesAsync(ct);

    return Results.Ok(ToAuthResponse(user, jwt));
});

group.MapPost("/google", async (
    GoogleLoginRequest req,
    IdentityDbContext db,
    JwtTokenGenerator jwt,
    IHttpClientFactory httpFactory,
    IConfiguration cfg,
    CancellationToken ct) =>
{
    string email;
    string fullName;
    string googleSub;
    string? picture = null;

    if (!string.IsNullOrWhiteSpace(req.Credential))
    {
        // Gerçek Google Identity Services (GIS) id_token doğrulaması
        var client = httpFactory.CreateClient("google-oauth");
        var resp = await client.GetAsync($"https://oauth2.googleapis.com/tokeninfo?id_token={Uri.EscapeDataString(req.Credential)}", ct);
        if (!resp.IsSuccessStatusCode)
            throw new DomainException("Google kimlik doğrulama jetonu (id_token) doğrulanamadı.", 401);

        var info = await resp.Content.ReadFromJsonAsync<GoogleTokenInfo>(cancellationToken: ct)
                   ?? throw new DomainException("Google yanıtı okunamadı.", 401);

        var configuredClientId = cfg["GoogleAuth:ClientId"];
        if (!string.IsNullOrWhiteSpace(configuredClientId) && info.Aud != configuredClientId)
            throw new DomainException("Google Client ID uyuşmazlığı.", 401);

        email = info.Email.Trim().ToLowerInvariant();
        fullName = string.IsNullOrWhiteSpace(info.Name) ? email.Split('@')[0] : info.Name;
        googleSub = info.Sub;
        picture = info.Picture;
    }
    else if (!string.IsNullOrWhiteSpace(req.Email))
    {
        // Lokal geliştirme ortamında Google OAuth penceresi simülasyonu
        email = req.Email.Trim().ToLowerInvariant();
        fullName = string.IsNullOrWhiteSpace(req.FullName) ? email.Split('@')[0] : req.FullName.Trim();
        googleSub = $"google-sim-{email}";
    }
    else
    {
        throw new DomainException("Google id_token veya e-posta bilgisi gereklidir.");
    }

    var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email, ct);
    if (user is null)
    {
        user = new AppUser
        {
            Id = Guid.NewGuid(),
            Email = email,
            FullName = fullName,
            Role = req.RequestedRole == "Admin" ? "Admin" : "Customer",
            Provider = "Google",
            GoogleSubject = googleSub,
            AvatarUrl = picture,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            LastLoginAt = DateTime.UtcNow
        };
        db.Users.Add(user);
    }
    else
    {
        if (!user.IsActive)
            throw new DomainException("Hesabınız pasife alınmış.", 403);

        user.Provider = "Google";
        user.GoogleSubject ??= googleSub;
        user.AvatarUrl ??= picture;
        user.LastLoginAt = DateTime.UtcNow;
    }

    await db.SaveChangesAsync(ct);
    return Results.Ok(ToAuthResponse(user, jwt));
});

group.MapGet("/me", async (ClaimsPrincipal principal, IdentityDbContext db, CancellationToken ct) =>
{
    var sub = principal.GetUserId();
    if (string.IsNullOrWhiteSpace(sub) || !Guid.TryParse(sub, out var id))
        return Results.Unauthorized();

    var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id, ct);
    return user is null ? Results.Unauthorized() : Results.Ok(MapUser(user));
}).RequireAuthorization();

// ---------- Admin Yetkili Üye Yönetimi Uçları ----------

group.MapGet("/users", async (IdentityDbContext db, CancellationToken ct) =>
{
    var users = await db.Users.AsNoTracking()
        .OrderByDescending(u => u.Role == "Admin")
        .ThenByDescending(u => u.CreatedAt)
        .ToListAsync(ct);

    return Results.Ok(users.Select(MapUser));
}).RequireAuthorization(JwtAuthConstants.AdminPolicy);

group.MapPut("/users/{id:guid}/role", async (Guid id, UpdateUserRoleRequest req, IdentityDbContext db, CancellationToken ct) =>
{
    var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct)
               ?? throw new DomainException("Üye bulunamadı.", 404);

    if (req.Role is not ("Admin" or "Customer"))
        throw new DomainException("Geçersiz rol. Yalnızca 'Admin' veya 'Customer' atanabilir.");

    user.Role = req.Role;
    if (req.IsActive.HasValue)
        user.IsActive = req.IsActive.Value;

    await db.SaveChangesAsync(ct);
    return Results.Ok(MapUser(user));
}).RequireAuthorization(JwtAuthConstants.AdminPolicy);

app.Run();

static object ToAuthResponse(AppUser u, JwtTokenGenerator jwt)
{
    var (token, expiresAt) = jwt.Generate(u);
    return new
    {
        accessToken = token,
        expiresAt,
        user = MapUser(u)
    };
}

static object MapUser(AppUser u) => new
{
    id = u.Id.ToString("N"),
    u.Email,
    u.FullName,
    u.Role,
    u.Provider,
    u.AvatarUrl,
    u.IsActive,
    u.CreatedAt,
    u.LastLoginAt
};

static async Task SeedUsersAsync(IServiceProvider services)
{
    using var scope = services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
    await db.Database.EnsureCreatedAsync();

    if (await db.Users.AnyAsync())
        return;

    db.Users.AddRange(
        new AppUser
        {
            Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Email = "admin@minimagaza.com",
            FullName = "Sistem Yöneticisi (Admin)",
            PasswordHash = PasswordHasher.Hash("Admin123!"),
            Role = "Admin",
            Provider = "Local",
            IsActive = true
        },
        new AppUser
        {
            Id = Guid.Parse("22222222-2222-2222-2222-222222222222"),
            Email = "musteri@minimagaza.com",
            FullName = "Demo Müşteri",
            PasswordHash = PasswordHasher.Hash("Musteri123!"),
            Role = "Customer",
            Provider = "Local",
            IsActive = true
        });

    await db.SaveChangesAsync();
}

public sealed record LoginRequest(string Email, string Password);
public sealed record RegisterRequest(string Email, string FullName, string Password);
public sealed record GoogleLoginRequest(string? Credential, string? Email, string? FullName, string? RequestedRole);
public sealed record UpdateUserRoleRequest(string Role, bool? IsActive);
public sealed record GoogleTokenInfo(string Sub, string Email, string? Name, string? Picture, string Aud);

