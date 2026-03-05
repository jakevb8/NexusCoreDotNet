using System.Threading.RateLimiting;
using FirebaseAdmin;
using Google.Apis.Auth.OAuth2;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NexusCoreDotNet.Data;
using NexusCoreDotNet.Middleware;
using NexusCoreDotNet.Services;

var builder = WebApplication.CreateBuilder(args);

// ── Configuration ────────────────────────────────────────────────────────────
// Support DATABASE_URL environment variable (Railway / Neon postgresql:// style).
// Npgsql does not accept URL format or unknown parameters like channel_binding,
// so we parse the URL manually and build a proper key=value connection string.
var databaseUrl = Environment.GetEnvironmentVariable("DATABASE_URL");
if (!string.IsNullOrEmpty(databaseUrl))
{
    string connStr;
    try
    {
        var uri = new Uri(databaseUrl);
        var userInfo = uri.UserInfo.Split(':', 2);
        var user = Uri.UnescapeDataString(userInfo[0]);
        var password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : string.Empty;
        var host = uri.Host;
        var port = uri.Port > 0 ? uri.Port : 5432;
        var database = uri.AbsolutePath.TrimStart('/');

        // Parse query params but drop ones Npgsql doesn't understand
        var unsupported = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "channel_binding" };
        var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
        var extras = new System.Text.StringBuilder();
        foreach (string key in query)
        {
            if (!unsupported.Contains(key))
                extras.Append($";{key}={query[key]}");
        }

        connStr = $"Host={host};Port={port};Database={database};Username={user};Password={password}{extras}";
    }
    catch
    {
        // Not a URL format — use as-is (already a Npgsql connection string)
        connStr = databaseUrl;
    }
    builder.Configuration["ConnectionStrings:DefaultConnection"] = connStr;
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
// Register .NET enums as their native PostgreSQL enum types (created by Prisma).
// Npgsql requires this mapping before the data source is built.
var dbConnStr = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("DefaultConnection is not configured");

var dataSourceBuilder = new NpgsqlDataSourceBuilder(dbConnStr);
dataSourceBuilder.MapEnum<NexusCoreDotNet.Enums.OrgStatus>("OrgStatus");
dataSourceBuilder.MapEnum<NexusCoreDotNet.Enums.Role>("Role");
dataSourceBuilder.MapEnum<NexusCoreDotNet.Enums.AssetStatus>("AssetStatus");
var dataSource = dataSourceBuilder.Build();

builder.Services.AddDbContext<AppDbContext>(options =>
{
    options.UseNpgsql(dataSource, o => o.EnableRetryOnFailure(3));
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
// Use the framework default header name "RequestVerificationToken" so it matches
// what the Login.cshtml JS sends in the "RequestVerificationToken" fetch header.
builder.Services.AddAntiforgery();

var app = builder.Build();

// ── Middleware pipeline ───────────────────────────────────────────────────────
// Trust Railway's reverse proxy so X-Forwarded-Proto is respected
// (needed for correct cookie Secure policy and OAuth redirect URIs)
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
});

if (!app.Environment.IsDevelopment())
{
    // Log the full exception before returning the error page
    app.UseExceptionHandler(errApp => errApp.Run(async ctx =>
    {
        var ex = ctx.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()?.Error;
        var logger = ctx.RequestServices.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "Unhandled exception for {Method} {Path}", ctx.Request.Method, ctx.Request.Path);
        ctx.Response.StatusCode = 500;
        ctx.Response.ContentType = "text/plain";
        await ctx.Response.WriteAsync("Internal Server Error");
    }));
    // Do NOT use HSTS or HTTPS redirect — Railway terminates TLS at its proxy
}

app.UseStaticFiles();
app.UseRouting();
app.UseRateLimiter();
app.UseSession();
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<SessionAuthMiddleware>();

// Override COOP on /Login so Firebase signInWithPopup can communicate back
// from the Google OAuth popup window. The default Railway/browser value of
// "same-origin" blocks cross-origin popup messaging; "same-origin-allow-popups"
// permits it while still protecting all other pages.
app.Use(async (ctx, next) =>
{
    if (ctx.Request.Path.StartsWithSegments("/Login"))
        ctx.Response.Headers["Cross-Origin-Opener-Policy"] = "same-origin-allow-popups";
    await next();
});

app.MapRazorPages();

// Health check — must return 200 for Railway deployment probe
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

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
