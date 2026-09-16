using ArtTechGallery.Core.Models;
using ArtTechGallery.Infrastructure.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using ArtTechGallery.API.Authorization;
using Microsoft.AspNetCore.Authorization;
using ArtTechGallery.API.Storage;
using ArtTechGallery.Core.Storage;
using ArtTechGallery.Infrastructure.Storage;
using Microsoft.AspNetCore.Http.Features;

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
builder.Services.AddSingleton<ExhibitionCodeGenerator>();

var storageOptions = builder.Configuration.GetSection(ArtworkStorageOptions.Section).Get<ArtworkStorageOptions>()
    ?? new ArtworkStorageOptions();
if (!Uri.TryCreate(storageOptions.PublicBaseUrl, UriKind.Absolute, out var artworkPublicBaseUri)
    || artworkPublicBaseUri.Scheme is not ("http" or "https")
    || artworkPublicBaseUri.GetLeftPart(UriPartial.Authority) + artworkPublicBaseUri.AbsolutePath.TrimEnd('/') != storageOptions.PublicBaseUrl.TrimEnd('/')
    || !string.IsNullOrEmpty(artworkPublicBaseUri.UserInfo))
    throw new InvalidOperationException("ArtworkStorage:PublicBaseUrl must be an absolute HTTP(S) URL without credentials, query or fragment.");
var artworkStorageRoot = Path.IsPathRooted(storageOptions.RootPath)
    ? storageOptions.RootPath : Path.Combine(builder.Environment.ContentRootPath, storageOptions.RootPath);
artworkStorageRoot = Path.GetFullPath(artworkStorageRoot);
var contentRoot = Path.GetFullPath(builder.Environment.ContentRootPath).TrimEnd(Path.DirectorySeparatorChar)
    + Path.DirectorySeparatorChar;
if (string.IsNullOrWhiteSpace(storageOptions.RootPath)
    || artworkStorageRoot.StartsWith(contentRoot, StringComparison.OrdinalIgnoreCase))
    throw new InvalidOperationException("ArtworkStorage:RootPath must be a dedicated directory outside the application content root.");
builder.Services.AddSingleton(new ManagedArtworkImageUrls(new Uri(storageOptions.PublicBaseUrl.TrimEnd('/') + "/")));
builder.Services.AddSingleton<IArtworkStorage>(new LocalArtworkStorage(artworkStorageRoot));
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = ArtworkImageValidator.MaximumBytes + 64 * 1024;
    options.ValueCountLimit = 16;
    options.MultipartHeadersCountLimit = 16;
});

builder.Services.AddControllers();
builder.Services.AddProblemDetails();

builder.Services.AddCors();
builder.Services.AddOptions<Microsoft.AspNetCore.Cors.Infrastructure.CorsOptions>()
    .Configure<IConfiguration>((options, configuration) =>
{
    var origins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
    foreach (var origin in origins)
    {
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || uri.GetLeftPart(UriPartial.Authority) != origin || !string.IsNullOrEmpty(uri.UserInfo))
            throw new InvalidOperationException("Cors:AllowedOrigins must contain exact HTTP(S) origins without paths or trailing slashes.");
    }
    options.AddPolicy("ArtistPanel", policy => policy.WithOrigins(origins)
        .WithMethods("GET", "POST", "PUT", "DELETE").WithHeaders("Authorization", "Content-Type"));
}).ValidateOnStart();

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

app.UseRouting();
app.UseCors("ArtistPanel");
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.MapIdentityApi<User>();

app.Run();

// Entry point for the integration test host.
public partial class Program { }
