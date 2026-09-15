using ArtTechGallery.API.Authorization;
using ArtTechGallery.Core.DTOs;
using ArtTechGallery.Core.Models;
using ArtTechGallery.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ArtTechGallery.API.Controllers;

[ApiController]
[Route("api/artist/exhibitions")]
[Authorize(Policy = ActiveUserRequirement.PolicyName)]
public sealed class ArtistExhibitionsController(
    AppDbContext dbContext,
    UserManager<User> userManager,
    ExhibitionCodeGenerator codeGenerator) : ControllerBase
{
    private Guid CurrentUserId => Guid.Parse(userManager.GetUserId(User)!);
    private IQueryable<Exhibition> Owned => dbContext.Exhibitions
        .Where(x => x.ArtistProfile.UserId == CurrentUserId);

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<OwnExhibitionDto>>> List(CancellationToken cancellationToken)
    {
        if (!await dbContext.ArtistProfiles.AnyAsync(x => x.UserId == CurrentUserId, cancellationToken))
            return MissingProfile();
        var exhibitions = await Owned.AsNoTracking().OrderBy(x => x.SortOrder)
            .ThenBy(x => x.CreatedAt).ThenBy(x => x.Id).ToListAsync(cancellationToken);
        return Ok(exhibitions.Select(ToDto).ToArray());
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<OwnExhibitionDto>> Get(Guid id, CancellationToken cancellationToken)
    {
        var exhibition = await Owned.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        return exhibition is null ? MissingExhibition() : Ok(ToDto(exhibition));
    }

    [HttpPost]
    public async Task<ActionResult<OwnExhibitionDto>> Create(SaveExhibitionRequest request, CancellationToken cancellationToken)
    {
        var profile = await dbContext.ArtistProfiles.AsNoTracking()
            .SingleOrDefaultAsync(x => x.UserId == CurrentUserId, cancellationToken);
        if (profile is null) return MissingProfile();
        if (!profile.IsActive) return InactiveProfile();

        var exhibition = new Exhibition { Id = Guid.NewGuid(), ArtistProfileId = profile.Id };
        SetMetadata(exhibition, request);
        for (int attempt = 0; attempt < 5; attempt++)
        {
            exhibition.ExhibitionCode = codeGenerator.Generate();
            dbContext.Exhibitions.Add(exhibition);
            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
                await dbContext.Entry(exhibition).ReloadAsync(cancellationToken);
                return CreatedAtAction(nameof(Get), new { id = exhibition.Id }, ToDto(exhibition));
            }
            catch (DbUpdateException exception) when (exception.InnerException is PostgresException
                { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "IX_Exhibitions_ExhibitionCode" })
            {
                dbContext.Entry(exhibition).State = EntityState.Detached;
            }
        }
        return Problem(statusCode: StatusCodes.Status409Conflict, title: "Exhibition code unavailable",
            detail: "Could not allocate a unique exhibition code. Please retry the request.");
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<OwnExhibitionDto>> Update(Guid id, SaveExhibitionRequest request, CancellationToken cancellationToken)
    {
        var exhibition = await Owned.Include(x => x.ArtistProfile)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (exhibition is null) return MissingExhibition();
        if (!exhibition.ArtistProfile.IsActive) return InactiveProfile();
        SetMetadata(exhibition, request);
        return await Save(exhibition, cancellationToken);
    }

    [HttpPost("{id:guid}/publish")]
    public Task<ActionResult<OwnExhibitionDto>> Publish(Guid id, ExhibitionTransitionRequest request, CancellationToken cancellationToken)
        => Transition(id, ExhibitionStatus.Published, cancellationToken);

    [HttpPost("{id:guid}/deactivate")]
    public Task<ActionResult<OwnExhibitionDto>> Deactivate(Guid id, ExhibitionTransitionRequest request, CancellationToken cancellationToken)
        => Transition(id, ExhibitionStatus.Deactivated, cancellationToken);

    private async Task<ActionResult<OwnExhibitionDto>> Transition(Guid id, ExhibitionStatus target, CancellationToken cancellationToken)
    {
        var exhibition = await Owned.Include(x => x.ArtistProfile)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (exhibition is null) return MissingExhibition();
        if (!exhibition.ArtistProfile.IsActive) return InactiveProfile();
        bool valid = target == ExhibitionStatus.Published
            ? exhibition.Status is ExhibitionStatus.Draft or ExhibitionStatus.Deactivated
            : exhibition.Status == ExhibitionStatus.Published;
        if (!valid)
            return Problem(statusCode: StatusCodes.Status409Conflict, title: "Invalid exhibition transition",
                detail: $"Cannot change {exhibition.Status.ToString().ToLowerInvariant()} to {target.ToString().ToLowerInvariant()}.");
        exhibition.Status = target;
        return await Save(exhibition, cancellationToken);
    }

    private async Task<ActionResult<OwnExhibitionDto>> Save(Exhibition exhibition, CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return Ok(ToDto(exhibition));
        }
        catch (DbUpdateConcurrencyException)
        {
            return Problem(statusCode: StatusCodes.Status409Conflict, title: "Exhibition changed",
                detail: "The exhibition changed during this request. Reload it before retrying.");
        }
    }

    private static void SetMetadata(Exhibition exhibition, SaveExhibitionRequest request)
    {
        exhibition.Title = request.Title;
        exhibition.Description = request.Description ?? string.Empty;
        exhibition.SortOrder = request.SortOrder;
    }

    private ObjectResult MissingProfile() => Problem(statusCode: StatusCodes.Status404NotFound,
        title: "Artist profile not found", detail: "Create your artist profile first.");
    private ObjectResult MissingExhibition() => Problem(statusCode: StatusCodes.Status404NotFound,
        title: "Exhibition not found");
    private ObjectResult InactiveProfile() => Problem(statusCode: StatusCodes.Status403Forbidden,
        title: "Artist profile inactive", detail: "An inactive profile cannot manage exhibitions through this endpoint.");

    private static OwnExhibitionDto ToDto(Exhibition exhibition) => new()
    {
        Id = exhibition.Id, ExhibitionCode = exhibition.ExhibitionCode,
        Title = exhibition.Title, Description = exhibition.Description, SortOrder = exhibition.SortOrder,
        Status = exhibition.Status.ToString().ToLowerInvariant(), CreatedAt = exhibition.CreatedAt
    };
}
