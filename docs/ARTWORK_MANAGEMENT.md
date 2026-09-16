# Artwork Management API

Completed and validated on 2026-09-16. Uses the existing Identity bearer
authentication and `ActiveUser` policy; anonymous viewers still need no account.

## Endpoints

Base route: `/api/artist/exhibitions/{exhibitionId:guid}/artworks`.

| Method | Suffix | Result |
| --- | --- | --- |
| GET | none | 200, ordered array (including inactive artworks); empty owned exhibition returns `[]` |
| POST | none | 201, created artwork and Location header |
| GET | `/{id:guid}` | 200, owned artwork |
| PUT | `/{id:guid}` | 200, saved artwork |
| DELETE | `/{id:guid}` | 204, permanent removal; repeated deletion returns 404 |

Ownership follows authenticated user -> artist profile -> exhibition -> artwork.
Both route IDs must identify that owned relationship. Missing profiles, missing
parents/artworks, mismatched parent/artwork pairs and cross-owner access return
the same 404. No request can move an artwork or choose its owner.
Inactive users receive 403. Inactive profiles remain readable but cannot write.

## Create/update body

POST and PUT share `SaveArtworkRequest`; PUT supplies the editable metadata,
not a partial patch. Unknown fields are rejected with ValidationProblemDetails.

| JSON field | Rules |
| --- | --- |
| `title` | Required, trimmed, 1–200 characters |
| `description` | Optional, at most 2000 characters; null/omitted becomes empty string |
| `creationYear` | Integer, 1 through the current UTC year |
| `widthCm`, `heightCm` | Required decimal numbers, 1–1000 centimeters inclusive, at most two fractional decimal places |
| `imageUrl` | Required, trimmed, at most 1000 characters; absolute HTTP(S), with host, without credentials, fragment, whitespace or backslashes |
| `sortOrder` | Integer 0–2147483647; defaults to 0 when omitted |

Responses contain `id`, `exhibitionId`, the seven metadata fields above (counting
width and height separately), `isActive` and `createdAt`. IDs, parent relationship,
activity and creation timestamp are server controlled. Artwork IDs are generated
GUIDs, stable on edit; the existing public artwork endpoint already uses these
IDs. ProfileCode and ExhibitionCode remain independent public identifiers.

## Lifecycle, ordering and deletion

Artworks can be created, edited and removed in Draft, Published and Deactivated
exhibitions. Changes in Published exhibitions take effect immediately. New
artworks are active; the parent publication state controls public visibility.
Existing inactive artworks remain manageable without implicitly reactivating them.

Set `sortOrder` through POST/PUT. Both management lists and public exhibition
arrays order by SortOrder ascending, then ID ascending in PostgreSQL. Duplicate
values and gaps are valid; insertion/deletion never renumbers other artworks.
There is no atomic bulk reorder or revision mechanism. Concurrent metadata
updates have no conflict token; overlapping changed fields use the last save.
Parent removal during create and artwork removal during a pending update/delete
return 404. No artificial 409 is introduced for ordinary metadata writes.

Hard deletion removes only the subordinate artwork row; it has no dependent
records. The exhibition/profile survive. Parent deletion retains the existing
cascade to artworks. No referenced image or remote file is deleted.

## Managed JPG/PNG uploads and local storage

POST and PUT also accept `multipart/form-data` on the same owned routes. Fields
are `title`, `description`, `creationYear`, `widthCm`, `heightCm`, `sortOrder`
and `image`. Create requires exactly one image; update accepts zero or one and
retains the current image when omitted. Unknown/duplicate fields and extra file
parts are rejected. `imageUrl` is never writable in multipart requests.

Uploads accept non-animated JPEG or PNG bytes up to 10 MiB and 4096 x 4096
pixels. The API checks the declared MIME type, detected encoded format and a
complete bounded decode. Original validated bytes are preserved without image
processing. Submitted filenames are ignored. Files use the server-generated
artwork ID and a random immutable version under the configured runtime root.

`IArtworkStorage` and `ArtworkImageReference` are provider-neutral Core
contracts. `LocalArtworkStorage` writes to a temporary sibling and atomically
renames it. Startup removes abandoned temporary writes. Runtime data is separate
from `DevelopmentAssets`, ignored by Git and excluded from publish input.

Set both values locally or through environment variables:

```json
"ArtworkStorage": {
  "RootPath": "../RuntimeData/ArtworkImages",
  "PublicBaseUrl": "http://<viewer-reachable-backend-origin>:5184"
}
```

`PublicBaseUrl` must be explicit; request Host headers are not trusted. For a
physical Android device, use the PC's reachable origin in ignored development
configuration or `ArtworkStorage__PublicBaseUrl`. Do not commit a LAN address.

Managed images are delivered anonymously only at the current URL
`GET /api/artworks/{artworkId}/images/{version}.{extension}` and only while the
artwork is active, its exhibition is Published, and its profile/user are active.
The response is `image/jpeg` or `image/png` with `X-Content-Type-Options:
nosniff`. There is no unrestricted static-file mapping for runtime uploads.

Replacement writes a new version, commits its URL, then removes the previous
managed file. Failed database saves compensate by removing the new file.
Deletion commits the artwork removal before deleting only a recognized owned
managed reference. Cleanup failures are logged without exposing physical paths.
Per-artwork PostgreSQL advisory transaction locks serialize participating
replacement/delete operations. External URLs and development fixtures are never
deleted or fetched.

The JSON contract remains temporarily supported for external URL compatibility.
Create cannot submit a reserved managed route. Update may echo only that exact
artwork's current managed URL; it cannot forge or attach a different managed
reference. Changing a managed artwork back to an external URL retires its former
local file after the database save.

## Public compatibility and remaining storage boundary

Both `/api/exhibitions/{exhibitionCode}` and `/api/artworks/{id}` require an active
artwork, Published exhibition, active profile and active owning user. Hidden
content returns 404 (or is excluded from the exhibition array). Republish restores
eligible artwork visibility. Public JSON field names/types remain unchanged;
Unity still receives centimeters and converts to meters, and uses array order.

External `imageUrl` values remain supported by the legacy JSON contract and are
not fetched, inspected or deleted. HTTP remains accepted for existing LAN
development; `/dev-assets/artworks` is still served only in Development. No
production provider, resizing, malware scanning or backup system was added.
Finalized orphan files after a process crash between file finalization and the
database save require an operational reconciliation pass; normal failures are
compensated and abandoned `.tmp-*` writes are removed at storage startup.

Invalid requests return 400, missing/invalid authentication 401, inactive access
403 and unavailable owned resources 404 using existing ProblemDetails behavior.
The existing exact-origin CORS policy now allows DELETE for future panel use.

## Validation and limitations

- Backend restore and Debug/Release compilation passed with 0 warnings/errors.
- Full isolated PostgreSQL suite: 92 passed, 0 failed, 0 skipped.
- Upload coverage includes JPEG/PNG persistence, metadata-only updates,
  immutable replacement/deletion cleanup, database/storage failure behavior,
  concurrent replacements, restart persistence, temporary-file reconciliation,
  ownership/inactivity, publication visibility, invalid/MIME-mismatched/truncated/
  oversized content, pixel limits, strict multipart structure and forged routes.
- Existing coverage still verifies lifecycle CRUD, deterministic ordering, exact
  public JSON keys/values and repeated demo seeding with preserved fixtures.
- EF reports no model changes since the last migration. No artwork migration,
  schema or seed implementation change was required.
- NuGet reported no known vulnerable direct or transitive API packages. Release
  publish passed and contained neither RuntimeData nor DevelopmentAssets.

Unity was not modified or rerun. The existing physical-device result remains
historical; a fresh Android run with a managed uploaded URL is required.
The panel upload UI is complete and validated separately. Production provider
selection remains intentionally deferred. A process crash after file
finalization but before database commit can leave a finalized orphan; this is
accepted local-foundation technical debt and is not currently handled by a
background job.
