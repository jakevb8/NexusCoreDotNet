using Microsoft.EntityFrameworkCore;
using NexusCoreDotNet.Data;
using NexusCoreDotNet.Data.Entities;

namespace NexusCoreDotNet.Services;

public class EventsService
{
    private readonly AppDbContext _db;

    public EventsService(AppDbContext db)
    {
        _db = db;
    }

    public record EventsResult(IList<KafkaEvent> Data, int Total, int Page, int PerPage);

    public async Task<EventsResult> FindAllAsync(Guid organizationId, int page = 1, int perPage = 50)
    {
        var skip = (page - 1) * perPage;

        var query = _db.KafkaEvents.Where(e => e.OrganizationId == organizationId);

        var total = await query.CountAsync();
        var data = await query
            .OrderByDescending(e => e.OccurredAt)
            .Skip(skip)
            .Take(perPage)
            .ToListAsync();

        return new EventsResult(data, total, page, perPage);
    }
}
