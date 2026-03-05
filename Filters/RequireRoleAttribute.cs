using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NexusCoreDotNet.Enums;
using NexusCoreDotNet.Services;

namespace NexusCoreDotNet.Filters;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public class RequireRoleAttribute : Attribute, IPageFilter
{
    private readonly Role _minimumRole;

    public RequireRoleAttribute(Role minimumRole = Role.VIEWER)
    {
        _minimumRole = minimumRole;
    }

    public void OnPageHandlerSelected(PageHandlerSelectedContext context) { }

    public void OnPageHandlerExecuting(PageHandlerExecutingContext context)
    {
        var user = context.HttpContext.User;

        if (user?.Identity?.IsAuthenticated != true)
        {
            var returnUrl = Uri.EscapeDataString(context.HttpContext.Request.Path);
            context.Result = new RedirectResult($"/Login?returnUrl={returnUrl}");
            return;
        }

        var roleClaim = user.FindFirst("role")?.Value;
        if (roleClaim == null || !Enum.TryParse<Role>(roleClaim, out var userRole))
        {
            context.Result = new RedirectResult("/Login");
            return;
        }

        if (!userRole.HasAtLeast(_minimumRole))
        {
            context.Result = new StatusCodeResult(403);
            return;
        }

        // Check org status — redirect PENDING org users to pending page
        // (except SUPERADMIN who can see everything)
        if (userRole != Role.SUPERADMIN)
        {
            var orgStatusStr = user.FindFirst("orgStatus")?.Value;
            if (orgStatusStr == "PENDING")
            {
                context.Result = new RedirectResult("/PendingApproval");
                return;
            }
        }
    }

    public void OnPageHandlerExecuted(PageHandlerExecutedContext context) { }
}
