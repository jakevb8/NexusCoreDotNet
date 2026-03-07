using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexusCoreDotNet.Enums;
using NexusCoreDotNet.Services;

namespace NexusCoreDotNet.Api.Controllers;

[ApiController]
[Route("api/v1/assets")]
[Authorize(AuthenticationSchemes = FirebaseJwtDefaults.AuthenticationScheme)]
public class AssetsApiController : ControllerBase
{
    private readonly AssetService _assets;

    public AssetsApiController(AssetService assets)
    {
        _assets = assets;
    }

    // GET /api/v1/assets?page=1&search=...
    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] int page = 1, [FromQuery] string? search = null)
    {
        var orgId = AuthService.GetOrgId(User);
        var (data, total) = await _assets.FindAllAsync(orgId, page, 20, search);
        return Ok(new
        {
            data = data.Select(MapAsset),
            total,
            page,
            perPage = 20
        });
    }

    // GET /api/v1/assets/{id}
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetOne(Guid id)
    {
        var orgId = AuthService.GetOrgId(User);
        var asset = await _assets.FindOneAsync(id, orgId);
        if (asset == null) return NotFound(new { message = "Asset not found" });
        return Ok(MapAsset(asset));
    }

    public record CreateAssetRequest(
        string Name,
        string SKU,
        string? Description,
        string? Status,
        string? AssignedTo);

    // POST /api/v1/assets
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateAssetRequest body)
    {
        if (string.IsNullOrWhiteSpace(body.Name))
            return BadRequest(new { message = "name is required" });
        if (string.IsNullOrWhiteSpace(body.SKU))
            return BadRequest(new { message = "sku is required" });

        var orgId = AuthService.GetOrgId(User);
        var userId = AuthService.GetUserId(User);
        var role = AuthService.GetRole(User);

        if (!role.HasAtLeast(Role.ASSET_MANAGER))
            return Forbid();

        var status = ParseStatus(body.Status) ?? AssetStatus.AVAILABLE;

        try
        {
            var asset = await _assets.CreateAsync(
                body.Name, body.SKU, body.Description, status, body.AssignedTo, orgId, userId);
            return CreatedAtAction(nameof(GetOne), new { id = asset.Id }, MapAsset(asset));
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("Trial limit"))
        {
            return StatusCode(403, new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    public record UpdateAssetRequest(
        string Name,
        string SKU,
        string? Description,
        string? Status,
        string? AssignedTo);

    // PUT /api/v1/assets/{id}
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateAssetRequest body)
    {
        if (string.IsNullOrWhiteSpace(body.Name))
            return BadRequest(new { message = "name is required" });
        if (string.IsNullOrWhiteSpace(body.SKU))
            return BadRequest(new { message = "sku is required" });

        var orgId = AuthService.GetOrgId(User);
        var userId = AuthService.GetUserId(User);
        var role = AuthService.GetRole(User);

        if (!role.HasAtLeast(Role.ASSET_MANAGER))
            return Forbid();

        var status = ParseStatus(body.Status) ?? AssetStatus.AVAILABLE;

        try
        {
            var asset = await _assets.UpdateAsync(
                id, body.Name, body.SKU, body.Description, status, body.AssignedTo, orgId, userId);
            return Ok(MapAsset(asset));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    // DELETE /api/v1/assets/{id}
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var orgId = AuthService.GetOrgId(User);
        var userId = AuthService.GetUserId(User);
        var role = AuthService.GetRole(User);

        if (!role.HasAtLeast(Role.ASSET_MANAGER))
            return Forbid();

        try
        {
            await _assets.DeleteAsync(id, orgId, userId);
            return NoContent();
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    // POST /api/v1/assets/import — CSV bulk import (multipart/form-data)
    [HttpPost("import")]
    public async Task<IActionResult> Import(IFormFile file)
    {
        var orgId = AuthService.GetOrgId(User);
        var userId = AuthService.GetUserId(User);
        var role = AuthService.GetRole(User);

        if (!role.HasAtLeast(Role.ASSET_MANAGER))
            return Forbid();

        if (file == null || file.Length == 0)
            return BadRequest(new { message = "No file provided" });

        using var stream = file.OpenReadStream();
        var records = AssetService.ParseCsvStream(stream);
        var result = await _assets.BulkImportAsync(records, orgId, userId);

        return Ok(new
        {
            created = result.Created,
            skipped = result.Skipped,
            limitReached = result.LimitReached,
            errors = result.Errors
        });
    }

    // GET /api/v1/assets/sample-csv — download a sample CSV template
    [HttpGet("sample-csv")]
    public IActionResult SampleCsv()
    {
        const string csv = "Name,SKU,Description,Status\nLaptop,LAP-001,MacBook Pro 14,AVAILABLE\nMonitor,MON-001,Dell 27\" 4K,IN_USE\n";
        var bytes = System.Text.Encoding.UTF8.GetBytes(csv);
        return File(bytes, "text/csv", "assets-sample.csv");
    }

    private static AssetStatus? ParseStatus(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        return Enum.TryParse<AssetStatus>(s.Replace(" ", "_").ToUpper(), out var v) ? v : null;
    }

    private static object MapAsset(NexusCoreDotNet.Data.Entities.Asset a) => new
    {
        id = a.Id,
        name = a.Name,
        sku = a.SKU,
        description = a.Description,
        status = a.Status.ToString(),
        assignedTo = a.AssignedTo,
        organizationId = a.OrganizationId,
        createdAt = a.CreatedAt,
        updatedAt = a.UpdatedAt
    };
}
