using System.Threading.RateLimiting;
using FirebaseAdmin;
using Google.Apis.Auth.OAuth2;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using NexusCoreDotNet.Data;
using NexusCoreDotNet.Middleware;
using NexusCoreDotNet.Services;

var builder = WebApplication.CreateBuilder(args);

// ── Configuration ────────────────────────────────────────────────────────────
// Support DATABASE_URL environment variable (Railway / Neon style)
var databaseUrl = Environment.GetEnvironmentVariable("DATABASE_URL");
if (!string.IsNullOrEmpty(databaseUrl))
{
    builder.Configuration["ConnectionStrings:DefaultConnection"] = databaseUrl;
}

var firebaseProjectId = (Environment.GetEnvironmentVariable("FIREBASE_PROJECT_ID")
    ?? builder.Configuration["Firebase:ProjectId"] ?? string.Empty)
    .Trim('"');

// ── Firebase Admin ────────────────────────────────────────────────────────────
if (FirebaseApp.DefaultInstance == null)
{
    GoogleCredential credential;
    var clientEmail = Environment.GetEnvironmentVariable("FIREBASE_CLIENT_EMAIL");
    var privateKey = (Environment.GetEnvironmentVariable("FIREBASE_PRIVATE_KEY") ?? string.Empty)
        .Replace("\\n", "\n")
        .Trim('"');

    if (!string.IsNullOrEmpty(clientEmail) && !string.IsNullOrEmpty(privateKey))
    {
        // Construct credentials from individual env vars (Railway / CI)
        var serviceAccountJson = System.Text.Json.JsonSerializer.Serialize(new
        {
            type = "service_account",
            project_id = firebaseProjectId,
            private_key = privateKey,
            client_email = clientEmail
        });
        credential = GoogleCredential.FromJson(serviceAccountJson)
            .CreateScoped("https://www.googleapis.com/auth/cloud-platform");
    }
    else
    {
        // Fall back to Application Default Credentials (local dev with GOOGLE_APPLICATION_CREDENTIALS)
        credential = GoogleCredential.GetApplicationDefault()
            .CreateScoped("https://www.googleapis.com/auth/cloud-platform");
    }

    FirebaseApp.Create(new AppOptions
    {
        ProjectId = firebaseProjectId,
        Credential = credential
    });
}

// ── Database ──────────────────────────────────────────────────────────────────
builder.Services.AddDbContext<AppDbContext>(options =>
{
    var connStr = builder.Configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("DefaultConnection is not configured");
    options.UseNpgsql(connStr, o => o.EnableRetryOnFailure(3));
});

// ── Caching ───────────────────────────────────────────────────────────────────
builder.Services.AddMemoryCache();
builder.Services.AddDistributedMemoryCache();

// ── Session ───────────────────────────────────────────────────────────────────
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromHours(24);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
});

// ── Authentication ────────────────────────────────────────────────────────────
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Login";
        options.LogoutPath = "/Logout";
        options.AccessDeniedPath = "/AccessDenied";
        options.ExpireTimeSpan = TimeSpan.FromDays(7);
        options.SlidingExpiration = true;
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    });

builder.Services.AddAuthorization();

// ── HTTP Client ───────────────────────────────────────────────────────────────
builder.Services.AddHttpClient("Resend");

// ── Application Services ──────────────────────────────────────────────────────
builder.Services.AddScoped<FirebaseAuthService>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<AssetService>();
builder.Services.AddScoped<AuditService>();
builder.Services.AddScoped<ReportsService>();
builder.Services.AddScoped<UserService>();
builder.Services.AddScoped<EmailService>();

// ── Razor Pages ───────────────────────────────────────────────────────────────
builder.Services.AddRazorPages(options =>
{
    options.Conventions.AuthorizeFolder("/Dashboard");
    options.Conventions.AuthorizeFolder("/Assets");
    options.Conventions.AuthorizeFolder("/Team");
    options.Conventions.AuthorizeFolder("/Reports");
});

// ── Rate Limiting ─────────────────────────────────────────────────────────────
builder.Services.AddRateLimiter(options =>
{
    // Global: 300 requests per 15 minutes
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 300,
                Window = TimeSpan.FromMinutes(15),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            }));

    // Strict: 5 requests per hour for registration endpoints
    options.AddFixedWindowLimiter("registration", opt =>
    {
        opt.PermitLimit = 5;
        opt.Window = TimeSpan.FromHours(1);
        opt.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        opt.QueueLimit = 0;
    });

    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});

// ── Anti-forgery ──────────────────────────────────────────────────────────────
builder.Services.AddAntiforgery(o => o.HeaderName = "X-XSRF-TOKEN");

var app = builder.Build();

// ── Middleware pipeline ───────────────────────────────────────────────────────
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseRateLimiter();
app.UseSession();
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<SessionAuthMiddleware>();

app.MapRazorPages();

app.MapGet("/", context =>
{
    context.Response.Redirect("/Dashboard");
    return Task.CompletedTask;
});

// Logout endpoint
app.MapPost("/Logout", async (HttpContext context) =>
{
    await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    context.Response.Redirect("/Login");
});

app.Run();
