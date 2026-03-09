using Microsoft.AspNetCore.Mvc.RazorPages;
using NexusCoreDotNet.Data.Entities;
using NexusCoreDotNet.Enums;
using NexusCoreDotNet.Filters;
using NexusCoreDotNet.Services;

namespace NexusCoreDotNet.Pages.Events;

[RequireRole(Role.VIEWER)]
public class IndexModel : PageModel
{
    private readonly EventsService _events;
    public IndexModel(EventsService events) { _events = events; }

    public IList<KafkaEvent> Events { get; private set; } = new List<KafkaEvent>();
    public int Total { get; private set; }
    public int CurrentPage { get; private set; }
    public int PerPage { get; private set; } = 50;
    public int TotalPages => (int)Math.Ceiling((double)Total / PerPage);

    public async Task OnGetAsync(int page = 1)
    {
        var orgId = AuthService.GetOrgId(User);
        CurrentPage = page;
        var result = await _events.FindAllAsync(orgId, page, PerPage);
        Events = result.Data;
        Total = result.Total;
    }
}
