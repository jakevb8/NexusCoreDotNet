using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Routing;
using NexusCoreDotNet.Enums;
using NexusCoreDotNet.Filters;

namespace NexusCoreDotNet.Tests;

public class RequireRoleAttributeTests
{
    // ── helpers ───────────────────────────────────────────────────────────────

    private static PageHandlerExecutingContext MakeContext(
        string? role, string orgStatus = "ACTIVE", bool authenticated = true)
    {
        var claims = new List<Claim>();
        if (role != null) claims.Add(new Claim("role", role));
        if (orgStatus != null) claims.Add(new Claim("orgStatus", orgStatus));
        claims.Add(new Claim("sub", Guid.NewGuid().ToString()));

        var identity = authenticated
            ? new ClaimsIdentity(claims, "Cookie")   // authenticationType set → IsAuthenticated = true
            : new ClaimsIdentity(claims);             // no authenticationType   → IsAuthenticated = false
        var principal = new ClaimsPrincipal(identity);

        var httpContext = new DefaultHttpContext { User = principal };
        httpContext.Request.Path = "/Assets/Create";

        var actionContext = new ActionContext(
            httpContext,
            new RouteData(),
            new ActionDescriptor());

        // PageHandlerExecutingContext requires a non-null handlerInstance in .NET 10+.
        // Use a minimal PageModel stub — the filter only reads HttpContext.User, so
        // the specific page model type does not matter.
        var stubPage = new StubPage();
        stubPage.PageContext = new PageContext(actionContext);

        return new PageHandlerExecutingContext(
            new PageContext(actionContext),
            filters: [],
            handlerMethod: null,
            handlerArguments: new Dictionary<string, object?>(),
            handlerInstance: stubPage);
    }

    private static void Invoke(RequireRoleAttribute filter, PageHandlerExecutingContext ctx)
        => filter.OnPageHandlerExecuting(ctx);

    // ── unauthenticated ───────────────────────────────────────────────────────

    [Fact]
    public void Unauthenticated_RedirectsToLogin()
    {
        var filter = new RequireRoleAttribute(Role.VIEWER);
        var ctx = MakeContext(role: null, authenticated: false);

        Invoke(filter, ctx);

        var redirect = Assert.IsType<RedirectResult>(ctx.Result);
        Assert.StartsWith("/Login", redirect.Url);
    }

    // ── VIEWER gate ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData("VIEWER")]
    [InlineData("ASSET_MANAGER")]
    [InlineData("ORG_MANAGER")]
    [InlineData("SUPERADMIN")]
    public void ViewerGate_AllRolesPass(string role)
    {
        var filter = new RequireRoleAttribute(Role.VIEWER);
        var ctx = MakeContext(role);

        Invoke(filter, ctx);

        Assert.Null(ctx.Result); // no redirect = allowed through
    }

    // ── ASSET_MANAGER gate ────────────────────────────────────────────────────

    [Fact]
    public void AssetManagerGate_ViewerIs403()
    {
        var filter = new RequireRoleAttribute(Role.ASSET_MANAGER);
        var ctx = MakeContext("VIEWER");

        Invoke(filter, ctx);

        Assert.IsType<StatusCodeResult>(ctx.Result);
        Assert.Equal(403, ((StatusCodeResult)ctx.Result).StatusCode);
    }

    [Theory]
    [InlineData("ASSET_MANAGER")]
    [InlineData("ORG_MANAGER")]
    [InlineData("SUPERADMIN")]
    public void AssetManagerGate_ManagerAndAbovePass(string role)
    {
        var filter = new RequireRoleAttribute(Role.ASSET_MANAGER);
        var ctx = MakeContext(role);

        Invoke(filter, ctx);

        Assert.Null(ctx.Result); // allowed through
    }

    // ── ORG_MANAGER gate ──────────────────────────────────────────────────────

    [Theory]
    [InlineData("VIEWER")]
    [InlineData("ASSET_MANAGER")]
    public void OrgManagerGate_LowerRolesAre403(string role)
    {
        var filter = new RequireRoleAttribute(Role.ORG_MANAGER);
        var ctx = MakeContext(role);

        Invoke(filter, ctx);

        Assert.IsType<StatusCodeResult>(ctx.Result);
        Assert.Equal(403, ((StatusCodeResult)ctx.Result).StatusCode);
    }

    [Theory]
    [InlineData("ORG_MANAGER")]
    [InlineData("SUPERADMIN")]
    public void OrgManagerGate_OrgManagerAndAbovePass(string role)
    {
        var filter = new RequireRoleAttribute(Role.ORG_MANAGER);
        var ctx = MakeContext(role);

        Invoke(filter, ctx);

        Assert.Null(ctx.Result);
    }

    // ── PENDING org ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData("VIEWER")]
    [InlineData("ASSET_MANAGER")]
    [InlineData("ORG_MANAGER")]
    public void PendingOrg_NonSuperAdminRedirectsToPendingApproval(string role)
    {
        var filter = new RequireRoleAttribute(Role.VIEWER);
        var ctx = MakeContext(role, orgStatus: "PENDING");

        Invoke(filter, ctx);

        var redirect = Assert.IsType<RedirectResult>(ctx.Result);
        Assert.Equal("/PendingApproval", redirect.Url);
    }

    [Fact]
    public void PendingOrg_SuperAdminPassesThrough()
    {
        var filter = new RequireRoleAttribute(Role.VIEWER);
        var ctx = MakeContext("SUPERADMIN", orgStatus: "PENDING");

        Invoke(filter, ctx);

        Assert.Null(ctx.Result);
    }

    // ── missing / invalid role claim ──────────────────────────────────────────

    [Fact]
    public void MissingRoleClaim_RedirectsToLogin()
    {
        var filter = new RequireRoleAttribute(Role.VIEWER);
        var ctx = MakeContext(role: null, authenticated: true); // authenticated but no role claim

        Invoke(filter, ctx);

        var redirect = Assert.IsType<RedirectResult>(ctx.Result);
        Assert.StartsWith("/Login", redirect.Url);
    }

    [Fact]
    public void InvalidRoleClaim_RedirectsToLogin()
    {
        var filter = new RequireRoleAttribute(Role.VIEWER);
        var ctx = MakeContext(role: "GARBAGE_ROLE", authenticated: true);

        Invoke(filter, ctx);

        var redirect = Assert.IsType<RedirectResult>(ctx.Result);
        Assert.StartsWith("/Login", redirect.Url);
    }
}

/// <summary>Minimal PageModel stub required by PageHandlerExecutingContext (non-null handlerInstance).</summary>
internal class StubPage : PageModel { }
