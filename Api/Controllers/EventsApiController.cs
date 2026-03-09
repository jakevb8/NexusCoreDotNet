using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexusCoreDotNet.Services;

namespace NexusCoreDotNet.Api.Controllers;

[ApiController]
[Route("api/v1/events")]
[Authorize(AuthenticationSchemes = FirebaseJwtDefaults.AuthenticationScheme)]
public class EventsApiController : ControllerBase
{
    private readonly EventsService _events;

    public EventsApiController(EventsService events)
    {
        _events = events;
    }

    // GET /api/v1/events?page=1&perPage=50
    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] int page = 1,
        [FromQuery] int perPage = 50)
    {
        var orgId = AuthService.GetOrgId(User);
        var result = await _events.FindAllAsync(orgId, page, perPage);

        return Ok(new
        {
            data = result.Data.Select(e => new
            {
                id = e.Id,
                organizationId = e.OrganizationId,
                assetId = e.AssetId,
                assetName = e.AssetName,
                previousStatus = e.PreviousStatus,
                newStatus = e.NewStatus,
                actorId = e.ActorId,
                occurredAt = e.OccurredAt,
                createdAt = e.CreatedAt,
            }),
            meta = new { total = result.Total, page = result.Page, perPage = result.PerPage }
        });
    }
}
