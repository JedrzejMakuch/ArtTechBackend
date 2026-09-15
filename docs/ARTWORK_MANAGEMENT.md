# Artwork Management API

Completed and validated on 2026-09-15. Uses the existing Identity bearer
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

## Public compatibility and image/storage boundary

Both `/api/exhibitions/{exhibitionCode}` and `/api/artworks/{id}` require an active
artwork, Published exhibition, active profile and active owning user. Hidden
content returns 404 (or is excluded from the exhibition array). Republish restores
eligible artwork visibility. Public JSON field names/types remain unchanged;
Unity still receives centimeters and converts to meters, and uses array order.

For this milestone `imageUrl` is an artist-provided external reference. The API
does not fetch, validate image bytes, upload, process, own or delete the resource.
HTTP remains accepted for existing LAN development; `/dev-assets/artworks` is
still served only in Development. No production storage system was added.
Clients must supply reachable JPG/PNG content supported by the viewer. URL
availability, redirects, expiration, third-party tracking and private-network
destinations are not validated. Do not put secrets into URLs. Production storage
must later establish managed asset ownership and trusted delivery URLs rather
than treating this temporary field as a storage key or file-system path.

Invalid requests return 400, missing/invalid authentication 401, inactive access
403 and unavailable owned resources 404 using existing ProblemDetails behavior.
The existing exact-origin CORS policy now allows DELETE for future panel use.

## Validation and limitations

- Backend restore passed; build passed with 0 warnings and 0 errors.
- Full isolated PostgreSQL suite: 82 passed, 0 failed, 0 skipped (57 prior tests,
  24 artwork cases and one DELETE preflight case).
- Coverage includes lifecycle CRUD, ownership, inactivity, rejected fields,
  dimensions/URLs, deterministic ordering, deletion races, exact public JSON
  keys/values and repeated demo seeding with preserved IDs/dimensions/URLs/order.
- EF reports no model changes since the last migration. No artwork migration,
  schema or seed implementation change was required.
- Tracked and untracked diff whitespace checks passed. Earlier uncommitted
  exhibition/foundation work is retained separately from this milestone.

Unity and Blazor were not modified or rerun. The existing physical-device result
remains historical; a fresh Android run with newly managed content was not done.
Artwork UI and production upload/storage remain separate future milestones.
