# Owned Exhibition Management API

## Scope and audit

The previous exhibition model had only `IsActive`, defaulting to true. Public
exhibition lookup, profile exhibition summaries, and artwork detail lookup used
that flag. Codes had a unique 16-character database index, but only the demo
seeder assigned exhibition codes; there was no artist exhibition creation API.
The existing User -> ArtistProfile -> Exhibition relationships and Identity
`ActiveUser` ownership conventions are retained. Public DTOs are unchanged.

## Lifecycle

| State | Owner can read/edit metadata | Anonymous visibility | Allowed command |
| --- | --- | --- | --- |
| `draft` | Yes | Hidden | Publish |
| `published` | Yes | Visible if owning profile and user are active | Deactivate |
| `deactivated` | Yes | Hidden | Publish again |

New exhibitions default to draft in both C# and PostgreSQL. Repeating a command
in its target state, or deactivating a draft, returns 409. There is no deletion
or transition back to draft. Editing published metadata takes effect publicly
immediately. Empty exhibitions may be published: artwork readiness requirements
belong to the future artwork-management milestone.

## Authenticated endpoints

All routes require the existing Identity bearer token and `ActiveUser` policy.

| Method | Route | Success |
| --- | --- | --- |
| GET | `/api/artist/exhibitions` | 200, own exhibitions in all states |
| GET | `/api/artist/exhibitions/{id:guid}` | 200, own exhibition |
| POST | `/api/artist/exhibitions` | 201, draft and owner-detail Location |
| PUT | `/api/artist/exhibitions/{id:guid}` | 200, updated metadata |
| POST | `/api/artist/exhibitions/{id:guid}/publish` | 200, published exhibition |
| POST | `/api/artist/exhibitions/{id:guid}/deactivate` | 200, deactivated exhibition |

Create/update accept only:

```json
{ "title": "Exhibition title", "description": "Optional description", "sortOrder": 0 }
```

Title is required, trimmed, nonblank and limited to 200 characters. Description
is optional (null/omitted becomes empty) and limited to 2000 characters.
SortOrder is a nonnegative integer, defaulting to zero. PUT replaces these
metadata fields. Lists sort by SortOrder, CreatedAt, then Id.

Publish/deactivate require an empty JSON object (`{}`). Unknown JSON fields are
rejected on every write. Responses contain `id`, `exhibitionCode`, `title`,
`description`, `sortOrder`, `status` (lowercase string), and `createdAt`.

Ownership comes exclusively from the authenticated user's profile. No request
can choose an owner or write a code, ID, timestamp, or lifecycle state.

- 400: invalid metadata/JSON, including unknown fields (ValidationProblemDetails).
- 401: absent/invalid bearer token.
- 403: inactive/deleted user; inactive profile on any write.
- 404: no profile for list/create, or missing/not-owned detail resource.
- 409: invalid transition, concurrent state change, or exhausted code collisions.

An active owner with an inactive profile can still list/read existing exhibitions.
An active profile with no exhibitions gets an empty list. Cross-owner requests
use the same 404 as nonexistent exhibitions. Errors use ProblemDetails.

## Public visibility and identifiers

`GET /api/exhibitions/{exhibitionCode}` remains anonymous and retains its exact
Unity-facing response fields, active artwork filtering, dimensions and ordering.
Only published exhibitions with active profiles and users are returned. Public
profile summaries and artwork detail lookup use the same publication rule.

Codes use 8 cryptographically random bytes encoded as 16 lowercase hex characters,
independently of database IDs. The existing unique index is authoritative, with
at most five insert attempts on code collisions, matching the profile approach.
Codes never change on metadata edits, publication or deactivation.

Status is an EF concurrency token: overlapping state changes cannot both succeed
from the same prior state. Metadata writes also detect an intervening state
change. Concurrent metadata-only edits retain the existing last-write-wins
approach; there is no general resource version/ETag protocol in this slice.

## Migration and development data

`20260915055802_ExhibitionPublicationStatus` replaces exhibition `IsActive` with
integer `Status`: 0 draft, 1 published, 2 deactivated. A check constraint rejects
other values; the database default is 0. Existing true values become published,
and false values become deactivated, preserving prior visibility and all IDs,
codes, metadata and artwork relationships. No other schema changes are included.

Rollback maps only published rows to true; drafts and deactivated rows become
false. Draft/deactivated distinctions cannot survive rollback to the old schema.
Coordinate the migration with backend deployment because old binaries use the
removed column. The migration was exercised only against isolated test databases.

Fresh demo data is explicitly published. Existing demo startup/URL repair never
republishes deactivated content or changes exhibition codes.

## Validation

On 2026-09-15:

- `dotnet restore ArtTechBackend.slnx`: passed.
- `dotnet build ArtTechBackend.slnx --no-restore`: passed, 0 warnings/errors.
- `dotnet test ArtTechBackend/ArtTechBackend.slnx --no-build --no-restore` with
  `ARTTECH_TEST_POSTGRES` pointing at an isolated local PostgreSQL server:
  57 passed, 0 failed, 0 skipped (33 existing, 24 added).
- `dotnet ef migrations has-pending-model-changes --project ArtTechGallery.Infrastructure --startup-project ArtTechGallery.API --no-build`:
  no pending model changes. EF CLI 10.0.2 reports an age warning versus runtime
  10.0.9; migration generation and validation succeeded.

New maintained PostgreSQL tests cover lifecycle and code stability, all public
visibility paths, exact Unity JSON fields and artwork ordering/metadata,
ownership/missing profiles, inactive users/profiles, bearer requirements,
validation/unknown fields, bounded collision recovery/exhaustion, simultaneous
publication, legacy migration/rollback, database defaults/constraints, and demo
seed preservation. No EF InMemory database is used.

Unity and the Blazor panel were not modified or rebuilt for this backend slice.
Physical Android verification was not repeated; public contract compatibility
is covered by backend integration tests. Exhibition UI remains a separate task.
