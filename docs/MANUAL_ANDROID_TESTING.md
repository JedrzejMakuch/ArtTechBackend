# Manual LAN and Android development testing

The development API uses exact configured CORS origins. Do not use
`AllowAnyOrigin`, and do not commit a machine-specific LAN address.

## Start the backend for a phone

From `ArtTechBackend`, run PowerShell with the Development environment and
explicit LAN-test overrides:

```powershell
$env:ASPNETCORE_ENVIRONMENT = "Development"
$env:ASPNETCORE_URLS = "http://0.0.0.0:5188"
$env:Cors__AllowedOrigins__0 = "http://localhost:5017"
$env:Cors__AllowedOrigins__1 = "http://localhost:5189"
$env:ArtworkStorage__PublicBaseUrl = "http://192.168.1.242:5188"
$env:DevelopmentDemo__PublicBaseUrl = "http://192.168.1.242:5188"
dotnet run --project ArtTechGallery.API --no-launch-profile
```

The browser panel on this PC uses `http://localhost:5017` (or the actual
static-server port). The phone uses `http://192.168.1.242:5188`. If the
panel is served on another origin, add that exact origin as another
`Cors__AllowedOrigins__N` value for the local process only.

Windows Firewall may need an inbound TCP rule for port 5188. That is a
machine-level operation and is intentionally not changed by the project.

## Unity Android origin

Create the ignored file
`ARWallArtPrototype/Assets/Resources/ViewerLocal.json` before building:

```json
{
  "apiBaseUrl": "http://192.168.1.242:5188",
  "shareBaseUrl": "http://192.168.1.242:5189"
}
```

Only `apiBaseUrl` is required for the viewer smoke test. The file is ignored
by Git and overrides the portable empty scene defaults at runtime. Remove or
replace it before sharing a build. The Unity scene itself contains no LAN
address.
