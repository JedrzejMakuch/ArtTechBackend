# Runtime artwork loading: local development test

These three original test-pattern images are development fixtures, not a storage or
upload implementation. The API serves them from its source tree only in
`Development` and only when `DevelopmentDemo:PublicBaseUrl` is non-empty and valid.
The directory is outside `wwwroot` and excluded from publish output.

## Local PC setup

1. Use a local development PostgreSQL database. Development startup already applies
   existing migrations and seeds demo data; this change adds no migrations.
2. Copy `ArtTechGallery.API/appsettings.Development.example.json` to
   `ArtTechGallery.API/appsettings.Development.json` if that local file does not
   already exist. Configure your local connection string. Do not overwrite existing
   configuration. The local file is ignored by Git.
3. Set `DevelopmentDemo:PublicBaseUrl` in that local file to an origin reachable
   by the viewer, such as `http://<PC-LAN-IP>:5184` (replace the placeholder).
   Alternatively set the environment variable `DevelopmentDemo__PublicBaseUrl`.
   Use `http://localhost:5184` only for an Editor test on the same PC.
4. In PowerShell, from the backend repository:

   ```powershell
   $env:ASPNETCORE_ENVIRONMENT = 'Development'
   dotnet run --project ArtTechGallery.API --no-launch-profile --urls http://0.0.0.0:5184
   ```

   `0.0.0.0` is a listening address, not an image URL. `PublicBaseUrl` must have an
   HTTP(S) scheme and host, with no credentials, path, query, fragment or wildcard
   host. A trailing slash is accepted. Invalid non-empty configuration fails startup
   before migration/seeding; blank configuration disables fixtures and URL repair,
   preserving the previous seed behavior. Changing the public origin and restarting
   updates recognized managed demo URLs to the new origin.

5. Verify `http://<PC-LAN-IP>:5184/api/exhibitions/colors-of-nature` and open every
   returned `imageUrl`. Expected files:

   | File under `/dev-assets/artworks/` | MIME type | Image pixels | Artwork size |
   | --- | --- | --- | --- |
   | `morning-forest.jpg` | `image/jpeg` | 800 x 600 | 80 x 60 cm |
   | `quiet-lake.png` | `image/png` | 1000 x 700 | 100 x 70 cm |
   | `mountain-road.jpg` | `image/jpeg` | 500 x 700 | 50 x 70 cm |

## Existing demo databases

No database reset is needed. Repair requires the demo user's email
`demo@arttechgallery.local`, profile code `demo-artist`, exhibition code
`colors-of-nature`, and one of the original three artwork titles. It replaces only
that artwork's exact original `https://example.com/images/...` placeholder or an
absolute HTTP(S) URL with its exact reserved `/dev-assets/artworks/<filename>` path
and no credentials/query/fragment. The latter paths are designated managed fixtures
regardless of origin, allowing local host/port changes.

Custom URLs, other profiles/exhibitions/artworks and renamed demo artworks are left
alone. IDs, ordering, dimensions, dates and other metadata are untouched. Repeated
startup with the same configuration performs no artwork updates. The profile avatar
placeholder is intentionally unchanged because Unity does not use it in this flow.

## Unity Editor and Android

1. Open `SampleScene` in Unity 6000.3.9f1. On `_Managers`, set
   `ExhibitionApiClient → Base URL` to the same viewer-reachable origin configured
   above. Keep `ExhibitionApiTest → Exhibition Code` as `colors-of-nature`.
2. For HTTP testing, set Player Settings → Allow downloads over HTTP to
   `Development Only`, and build an Android **Development Build**. Ensure the build
   has Internet access permission. Do not bypass TLS certificate validation if using
   HTTPS; the phone must trust the certificate.
3. Put the PC and phone on a network that permits device-to-device access. Allow
   the test port through the PC firewall for the appropriate private network.
   First open the exhibition and image URLs in the phone's browser. On a phone,
   `localhost` normally refers to the phone, not the PC.
4. Run the app. Confirm all three distinct labeled patterns load in API order and
   Previous/Next reaches each one. Sizes must remain 0.8 x 0.6 m, 1 x 0.7 m and
   0.5 x 0.7 m. Check orientation, distance controls, lock/unlock and smoothing.
5. For partial failure, temporarily rename one fixture file locally, press Retry,
   then restore its name and retry again. A failed slot must not show the previous
   artwork. Stop the API or disconnect the phone to test total failure and recovery.
   Retry reloads the whole exhibition, not just failed images. Check repeated retry,
   scene exit while loading and texture memory behavior on-device.

Keep local addresses, connection strings and Unity test configuration uncommitted.
Changing Unity settings is a manual test step; this backend change does not edit
the Unity repository. Restore temporary fixture renames after failure checks.

## Release boundary checks

Run with `ASPNETCORE_ENVIRONMENT=Production` or `Staging` (and no conflicting
`DOTNET_ENVIRONMENT`). `/dev-assets/artworks/morning-forest.jpg` must return 404,
even if the development public URL is configured. These environments do not invoke
the demo seeder. A blank public URL in Development also leaves the asset route off.

Run `dotnet publish ArtTechGallery.API -c Release -o <temporary-output-directory>`.
There must be no `DevelopmentAssets` directory or fixture images in that output.
Run the fixture test from the source project with `dotnet run`, not a published app.
