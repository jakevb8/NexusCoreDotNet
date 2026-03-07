using System.Threading.RateLimiting;
using FirebaseAdmin;
using Google.Apis.Auth.OAuth2;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NexusCoreDotNet.Api;
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
// Two FirebaseApp instances are required:
//   1. Default ("nexus-core-dotnet") — used by Razor Pages (web browser sign-in).
//   2. "rms" ("nexus-core-rms") — used by REST API to verify tokens from the
//      Android / React Native / iOS mobile clients, which authenticate against
//      the nexus-core-rms Firebase project.
// VerifyIdTokenAsync checks the token's "aud" claim against the app's ProjectId,
// so each app must be initialized with the correct project ID.
// The service account credential only needs to be valid for admin operations;
// JWT signature verification uses Google's public JWKS regardless of project.
GoogleCredential? sharedCredential = null;
if (FirebaseApp.DefaultInstance == null)
{
    var clientEmail = Environment.GetEnvironmentVariable("FIREBASE_CLIENT_EMAIL");
    var privateKey = (Environment.GetEnvironmentVariable("FIREBASE_PRIVATE_KEY") ?? string.Empty)
        .Replace("\\n", "\n")
        .Trim('"');

    if (!string.IsNullOrEmpty(clientEmail) && !string.IsNullOrEmpty(privateKey))
    {
        var serviceAccountJson = System.Text.Json.JsonSerializer.Serialize(new
        {
            type = "service_account",
            project_id = firebaseProjectId,
            private_key = privateKey,
            client_email = clientEmail
        });
        sharedCredential = GoogleCredential.FromJson(serviceAccountJson)
            .CreateScoped("https://www.googleapis.com/auth/cloud-platform");
    }
    else
    {
        sharedCredential = GoogleCredential.GetApplicationDefault()
            .CreateScoped("https://www.googleapis.com/auth/cloud-platform");
    }

    // App 1: default — for Razor Pages (nexus-core-dotnet project)
    FirebaseApp.Create(new AppOptions
    {
        ProjectId = firebaseProjectId,
        Credential = sharedCredential
    });

    // App 2: "rms" — for REST API / mobile clients (nexus-core-rms project)
    FirebaseApp.Create(new AppOptions
    {
        ProjectId = "nexus-core-rms",
        Credential = sharedCredential
    }, "rms");
}

// ── Database ──────────────────────────────────────────────────────────────────
var dbConnStr = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("DefaultConnection is not configured");

var dataSourceBuilder = new NpgsqlDataSourceBuilder(dbConnStr);
// Register native PG enum types at the Npgsql data source level.
// With EF 9 + external NpgsqlDataSource, MapEnum must be called BOTH here
// AND in the UseNpgsql() call below — the data source registration handles
// low-level ADO reads/writes; the UseNpgsql registration handles EF query translation.
var passthrough = new Npgsql.NameTranslation.NpgsqlNullNameTranslator();
dataSourceBuilder.MapEnum<NexusCoreDotNet.Enums.OrgStatus>("OrgStatus", passthrough);
dataSourceBuilder.MapEnum<NexusCoreDotNet.Enums.Role>("Role", passthrough);
dataSourceBuilder.MapEnum<NexusCoreDotNet.Enums.AssetStatus>("AssetStatus", passthrough);
var dataSource = dataSourceBuilder.Build();

builder.Services.AddDbContext<AppDbContext>(options =>
{
    options.UseNpgsql(dataSource, o =>
    {
        o.EnableRetryOnFailure(3);
        // EF 9 requires MapEnum on the UseNpgsql options as well when using an
        // external NpgsqlDataSource — this wires up EF's query translation layer.
        o.MapEnum<NexusCoreDotNet.Enums.OrgStatus>("OrgStatus", nameTranslator: passthrough);
        o.MapEnum<NexusCoreDotNet.Enums.Role>("Role", nameTranslator: passthrough);
        o.MapEnum<NexusCoreDotNet.Enums.AssetStatus>("AssetStatus", nameTranslator: passthrough);
    });
});

// ── DataProtection ────────────────────────────────────────────────────────────
// Persist keys to the database so they survive container restarts/redeployments.
// Without this, a new key ring is generated on every startup and existing session
// cookies / antiforgery tokens can no longer be decrypted.
builder.Services.AddDataProtection()
    .PersistKeysToDbContext<AppDbContext>()
    .SetApplicationName("NexusCoreDotNet");

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
// Default scheme = Cookie (for Razor Pages).
// FirebaseJwt scheme = Bearer token validation (for /api/v1/* REST endpoints).
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
    })
    .AddScheme<AuthenticationSchemeOptions, FirebaseJwtHandler>(
        FirebaseJwtDefaults.AuthenticationScheme, _ => { });

builder.Services.AddAuthorization();

// ── CORS ──────────────────────────────────────────────────────────────────────
// Allow mobile clients (Android / iOS / React Native) and local dev to call
// the /api/v1/* endpoints. Razor Pages are not affected by CORS.
builder.Services.AddCors(options =>
{
    options.AddPolicy("ApiCors", policy =>
        policy.SetIsOriginAllowed(_ => true)   // mobile apps use opaque origins
              .AllowAnyHeader()
              .AllowAnyMethod());
});

// ── Controllers (REST API) ────────────────────────────────────────────────────
builder.Services.AddControllers();

// ── HTTP Client ───────────────────────────────────────────────────────────────
builder.Services.AddHttpClient("Resend");

// ── Application Services ──────────────────────────────────────────────────────
builder.Services.AddScoped<IFirebaseAuthService, FirebaseAuthService>();
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

// ── Database bootstrap ────────────────────────────────────────────────────────
// Ensure the DataProtectionKeys table exists. This table is not managed by
// Prisma (it is owned by ASP.NET Core Data Protection), so we create it with
// raw SQL on startup if it does not already exist. Without it the app crashes
// before serving a single request.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.ExecuteSqlRaw("""
        CREATE TABLE IF NOT EXISTS "DataProtectionKeys" (
            "Id"           SERIAL NOT NULL,
            "FriendlyName" TEXT,
            "Xml"          TEXT,
            CONSTRAINT "DataProtectionKeys_pkey" PRIMARY KEY ("Id")
        )
        """);

    // Log how many keys are persisted — useful for diagnosing key ring issues.
    var keyCount = db.DataProtectionKeys.Count();
    var startupLogger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    startupLogger.LogInformation("DataProtection: {KeyCount} key(s) persisted in database", keyCount);
}

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

        // Antiforgery validation failures mean the user has a stale session cookie
        // (e.g. after a redeployment before key persistence was set up). Redirect to
        // login so they can get a fresh session rather than showing a bare 400/500.
        if (ex is Microsoft.AspNetCore.Antiforgery.AntiforgeryValidationException)
        {
            logger.LogWarning("Antiforgery validation failed — redirecting to login for {Path}", ctx.Request.Path);
            ctx.Response.Redirect("/Login?reason=session_expired");
            return;
        }

        logger.LogError(ex, "Unhandled exception for {Method} {Path}", ctx.Request.Method, ctx.Request.Path);
        ctx.Response.StatusCode = 500;
        ctx.Response.ContentType = "text/plain";
        await ctx.Response.WriteAsync("Internal Server Error");
    }));
    // Do NOT use HSTS or HTTPS redirect — Railway terminates TLS at its proxy
}

app.UseStaticFiles();
app.UseRouting();
app.UseCors("ApiCors");
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
app.MapControllers();

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
