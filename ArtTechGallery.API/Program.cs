using ArtTechGallery.Core.Models;
using ArtTechGallery.Infrastructure.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using ArtTechGallery.API.Authorization;
using Microsoft.AspNetCore.Authorization;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>(options =>
{
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        x => x.MigrationsAssembly("ArtTechGallery.Infrastructure"));
});

builder.Services
    .AddIdentityCore<User>()
    .AddRoles<IdentityRole<Guid>>()
    .AddEntityFrameworkStores<AppDbContext>()
    .AddApiEndpoints();

builder.Services
    .AddAuthentication(IdentityConstants.BearerScheme)
    .AddBearerToken(IdentityConstants.BearerScheme);

builder.Services.AddAuthorization();
builder.Services.AddAuthorizationBuilder().AddPolicy(ActiveUserRequirement.PolicyName, policy =>
    policy.AddAuthenticationSchemes(IdentityConstants.BearerScheme)
        .RequireAuthenticatedUser().AddRequirements(new ActiveUserRequirement()));
builder.Services.AddScoped<IAuthorizationHandler, ActiveUserAuthorizationHandler>();
builder.Services.AddSingleton<ProfileCodeGenerator>();

builder.Services.AddControllers();
builder.Services.AddProblemDetails();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    // Opt-in local fixtures, separate from any future production image storage.
    Uri? demoBaseUri = AppDbSeeder.ValidateDevelopmentPublicBaseUrl(
        builder.Configuration["DevelopmentDemo:PublicBaseUrl"]);

    if (demoBaseUri is not null)
    {
        var assets = new PhysicalFileProvider(Path.Combine(
            app.Environment.ContentRootPath, "DevelopmentAssets", "Artworks"));
        app.Lifetime.ApplicationStopped.Register(assets.Dispose);
        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = assets,
            RequestPath = AppDbSeeder.DevelopmentAssetRequestPath
        });
    }

    await using AsyncServiceScope scope = app.Services.CreateAsyncScope();

    AppDbContext dbContext =
        scope.ServiceProvider.GetRequiredService<AppDbContext>();

    UserManager<User> userManager =
        scope.ServiceProvider.GetRequiredService<UserManager<User>>();

    await dbContext.Database.MigrateAsync();

    await AppDbSeeder.SeedAsync(dbContext, userManager, demoBaseUri);
}

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.MapIdentityApi<User>();

app.Run();

// Entry point for the integration test host.
public partial class Program { }
