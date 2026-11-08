using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddMemoryCache();

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") 
    ?? "Host=localhost;Database=shortener_db;Username=postgres;Password=03071972";

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString));

var app = builder.Build();


using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapPost("/api/shorten", async (ShortenRequest request, AppDbContext db, HttpContext httpContext) =>
{
    if (!Uri.TryCreate(request.Url, UriKind.Absolute, out _))
        return Results.BadRequest(new { error = "Некорректный формат URL." });

    string shortCode;

    if (!string.IsNullOrWhiteSpace(request.CustomAlias))
    {
        if (await db.ShortenedUrls.AnyAsync(u => u.ShortCode == request.CustomAlias))
            return Results.Conflict(new { error = "Этот короткий адрес уже занят." });

        shortCode = request.CustomAlias.Trim();
    }
    else
    {
        do
        {
            shortCode = GenerateShortCode();
        } while (await db.ShortenedUrls.AnyAsync(u => u.ShortCode == shortCode));
    }

    var shortenedUrl = new ShortenedUrl
    {
        Id = Guid.NewGuid(),
        OriginalUrl = request.Url,
        ShortCode = shortCode,
        CreatedAtUtc = DateTime.UtcNow,
        ExpiresAtUtc = request.ExpiresInDays.HasValue 
            ? DateTime.UtcNow.AddDays(request.ExpiresInDays.Value) 
            : null,
        ClickCount = 0
    };

    db.ShortenedUrls.Add(shortenedUrl);
    await db.SaveChangesAsync();

    var baseUrl = $"{httpContext.Request.Scheme}://{httpContext.Request.Host}";
    return Results.Ok(new 
    { 
        ShortUrl = $"{baseUrl}/{shortCode}", 
        ShortCode = shortCode,
        ExpiresAtUtc = shortenedUrl.ExpiresAtUtc
    });
});


app.MapGet("/{code}", async (string code, AppDbContext db, IMemoryCache cache, HttpContext httpContext) =>
{
    var entry = await db.ShortenedUrls.FirstOrDefaultAsync(u => u.ShortCode == code);
    if (entry is null)
        return Results.NotFound(new { error = "Ссылка не найдена." });

    if (entry.ExpiresAtUtc.HasValue && entry.ExpiresAtUtc.Value < DateTime.UtcNow)
        return Results.BadRequest(new { error = "Срок действия ссылки истек." });

    cache.Set($"url_{code}", entry.OriginalUrl, TimeSpan.FromMinutes(10));

    entry.ClickCount++;
    db.ClickEvents.Add(new ClickEvent
    {
        Id = Guid.NewGuid(),
        ShortenedUrlId = entry.Id,
        ClickedAtUtc = DateTime.UtcNow,
        UserAgent = httpContext.Request.Headers.UserAgent.ToString()
    });

    await db.SaveChangesAsync();

    return Results.Redirect(entry.OriginalUrl);
});

app.MapGet("/api/stats/{code}", async (string code, AppDbContext db) =>
{
    var entry = await db.ShortenedUrls
        .AsNoTracking()
        .Include(u => u.Clicks)
        .FirstOrDefaultAsync(u => u.ShortCode == code);

    if (entry is null)
        return Results.NotFound();

    return Results.Ok(new 
    { 
        entry.OriginalUrl, 
        entry.ShortCode,
        entry.ClickCount, 
        entry.CreatedAtUtc,
        entry.ExpiresAtUtc,
        RecentClicks = entry.Clicks
            .OrderByDescending(c => c.ClickedAtUtc)
            .Take(10)
            .Select(c => new { c.ClickedAtUtc, c.UserAgent })
    });
});

app.MapDelete("/api/{code}", async (string code, AppDbContext db, IMemoryCache cache) =>
{
    var entry = await db.ShortenedUrls.FirstOrDefaultAsync(u => u.ShortCode == code);
    if (entry is null)
        return Results.NotFound();

    db.ShortenedUrls.Remove(entry);
    await db.SaveChangesAsync();
    cache.Remove($"url_{code}");

    return Results.NoContent();
});

app.Run();

static string GenerateShortCode()
{
    const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
    return new string(Enumerable.Repeat(chars, 6)
        .Select(s => s[Random.Shared.Next(s.Length)]).ToArray());
}

public record ShortenRequest(string Url, string? CustomAlias = null, int? ExpiresInDays = null);

public class ShortenedUrl
{
    public Guid Id { get; set; }
    public string OriginalUrl { get; set; } = string.Empty;
    public string ShortCode { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? ExpiresAtUtc { get; set; }
    public int ClickCount { get; set; }
    public List<ClickEvent> Clicks { get; set; } = new();
}

public class ClickEvent
{
    public Guid Id { get; set; }
    public Guid ShortenedUrlId { get; set; }
    public DateTime ClickedAtUtc { get; set; }
    public string UserAgent { get; set; } = string.Empty;
}

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }
    public DbSet<ShortenedUrl> ShortenedUrls => Set<ShortenedUrl>();
    public DbSet<ClickEvent> ClickEvents => Set<ClickEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ShortenedUrl>()
            .HasIndex(u => u.ShortCode)
            .IsUnique();

        modelBuilder.Entity<ClickEvent>()
            .HasOne<ShortenedUrl>()
            .WithMany(u => u.Clicks)
            .HasForeignKey(c => c.ShortenedUrlId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
