# Artist Management Foundation: own profile

This slice adds artist profile onboarding and editing to the existing layered backend.
It does not add a frontend, exhibition/artwork management, roles or image uploads.
Runtime Artwork Loading remains complete and its public API stays anonymous.

## Authentication and ownership

Use the existing Identity endpoints: `POST /register`, `POST /login?useCookies=false`
and `POST /refresh`. Login accepts email/password and returns opaque Identity bearer
tokens (not JWTs). Send `Authorization: Bearer <accessToken>` on management requests.
No cookie authentication or real email delivery is added.

The `ActiveUser` policy checks the authenticated user ID against the database on each
management request. A profile is not required. Missing, invalid or expired credentials
receive 401; a valid token for an inactive or deleted user receives 403.
This is a management access check, not an account suspension or token revocation system.
Existing Identity login/refresh endpoints do not check the custom `IsActive` property.

Ownership is always resolved from the authenticated principal. The caller cannot
choose a user ID, profile ID or public code to target someone else's profile.

## Management API

| Endpoint | Success | Other expected results |
| --- | --- | --- |
| `GET /api/artist/profile` | 200 with own profile | 404 before onboarding |
| `POST /api/artist/profile` | 201 with own profile and Location header | 409 if a profile already exists or code retries are exhausted |
| `PUT /api/artist/profile` | 200 with updated own profile | 404 if missing; 403 if the profile is inactive |

POST and PUT accept exactly these fields:

```json
{
  "displayName": "Artist name",
  "bio": "Optional biography"
}
```

Display name is trimmed before validation and must contain 1–200 characters.
Bio is optional, may be null/empty, and has a 2,000-character maximum. Missing/null
bio is saved as an empty string; PUT replaces the editable fields rather than patching.
Unknown JSON fields are rejected with 400 ValidationProblemDetails, including owner
IDs, profile codes, activity, creation dates and image URLs. Expected missing/conflict
errors use ProblemDetails. No database exception details are returned for handled conflicts.

Responses contain only `id`, `profileCode`, `displayName`, `bio`, `profileImageUrl`,
`isActive`, and `createdAt`. They do not include Identity account data or owner IDs.

POST sets ID, owner, UTC creation time and activity server-side. The initial image
URL is empty. The profile code is 16 lowercase hexadecimal characters from eight
cryptographically random bytes, generated independently of the database GUID.
The existing unique index enforces code uniqueness; creation makes at most five code
attempts. Only that specific unique-index conflict is retried. The unique user index
also protects concurrent onboarding, returning one success and one 409 conflict.

PUT changes only display name and bio. Code, owner, ID, creation time, image URL and
activity are preserved. An active owner may read an inactive profile but cannot edit,
reactivate or recreate it here. Concurrent edits use last-write-wins semantics.

Active profiles are immediately visible at `GET /api/profiles/{profileCode}` without
authentication. There is no profile draft/publish workflow. Exhibition viewing now
also checks the owning user's activity, consistently with public profile/artwork reads.

## Maintained integration tests

The xUnit project `ArtTechGallery.API.Tests` uses `WebApplicationFactory`, real Identity
registration/bearer login, EF migrations and PostgreSQL. It does not use EF InMemory
or bypass authentication with a fake handler.

Provide a connection to an isolated PostgreSQL test server, using an account allowed
to create/drop databases. The suite creates one random `arttech_tests_<guid>` database,
applies existing migrations, then drops only that generated database in fixture cleanup.
It runs the API in `Testing` so development seeding and development assets are disabled.
There is no default/fallback application database connection for tests.

From the backend repository, in PowerShell:

```powershell
$env:ARTTECH_TEST_POSTGRES = '<connection to your isolated PostgreSQL test server>'
dotnet restore ArtTechBackend.slnx
dotnet build ArtTechBackend.slnx --no-restore
dotnet test ArtTechBackend.slnx --no-build --no-restore
```

Do not commit the connection string. PostgreSQL 18.1 is available locally; the tests
also use ordinary PostgreSQL constraints/migrations suitable for the repository's
PostgreSQL 17 development setup. If no test connection is provided, tests fail with
an explicit setup message instead of silently skipping.

Tests cover the whole profile flow, two-user isolation, missing/duplicate profiles,
forced concurrent creation, inactive/deleted users, malformed/missing bearer tokens,
validation boundaries, protected fields, stable server-owned fields, deterministic
code collisions/retry exhaustion and anonymous public visibility.

Validated on 2026-09-14: restore/build passed with zero warnings/errors; all 22
integration tests passed against an isolated local PostgreSQL 18.1 cluster. No schema
change or migration was needed. The cluster was used only for automated validation;
the application's existing development database was not changed.

## Manual smoke test

1. Start the API against a local development database. Keep local connection strings
   and LAN addresses in ignored local configuration. Use HTTPS when sending real credentials.
2. Register a new test email through `/register`, then log in with
   `/login?useCookies=false`. Use the returned access token in the requests below.
3. GET your profile: expect 404. POST display name/bio: expect 201 and a Location
   header. POST again: expect 409.
4. GET and PUT your profile. Verify trimmed display name, updated biography and a
   stable code/ID/creation time. Open `/api/profiles/{profileCode}` without a token
   and confirm the changes are visible.
5. Register a second user and verify their profile starts at 404 and remains separate.
   Submit an extra `userId`, `profileCode` or `isActive` field: expect 400.
6. Remove or corrupt the bearer token: expect 401 from management while public reads
   remain available. In an isolated test database, mark the user inactive and confirm
   their previous token receives 403 and their public content is hidden.

Use `ArtTechBackend.http` as a request template. Do not save real passwords or tokens
into tracked files. Browser UI, email delivery, suspension workflows, publication and
exhibition/artwork management remain outside this slice.
