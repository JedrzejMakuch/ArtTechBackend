using ArtTechGallery.Core.Models;
using ArtTechGallery.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ArtTechGallery.API.Authorization;

public sealed class ActiveUserRequirement : IAuthorizationRequirement
{
    public const string PolicyName = "ActiveUser";
}

public sealed class ActiveUserAuthorizationHandler(
    UserManager<User> userManager,
    AppDbContext dbContext) : AuthorizationHandler<ActiveUserRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context, ActiveUserRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated == true
            && Guid.TryParse(userManager.GetUserId(context.User), out Guid userId)
            && await dbContext.Users.AsNoTracking().AnyAsync(x => x.Id == userId && x.IsActive))
        {
            context.Succeed(requirement);
        }
    }
}
