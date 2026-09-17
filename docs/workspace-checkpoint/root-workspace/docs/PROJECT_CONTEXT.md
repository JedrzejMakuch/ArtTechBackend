# AR Home Exhibition --- Project Context

Last updated: 2026-09-16

Numbered headings are document sections, not milestone numbers. Validation
totals are recorded per milestone and must not be treated as current totals
for later, expanded working trees.

## 1. Product

AR Home Exhibition is an ArtTech/SaaS product for independent artists.

The main viewer experience is a Unity mobile application that lets a
person view an artwork on their own wall at approximately real physical
scale.

The viewer does not need an account.

The target MVP flow is:

1.  Artist manages a profile, exhibitions and artworks.
2.  Artist publishes an exhibition.
3.  Artist shares a public link or QR code.
4.  Viewer opens the exhibition without logging in.
5.  Unity loads exhibition data and artwork images from the backend.
6.  Viewer selects an artwork.
7.  Quick Mode AR displays it at the correct physical dimensions.

The product is not intended to become a social network or marketplace
during the MVP stage.

------------------------------------------------------------------------

## 2. Workspace

Local workspace currently:

``` text
AR-Home-Exhibition/
├── AGENTS.md
├── docs/
│   └── PROJECT_CONTEXT.md
├── ArtTechBackend/
├── ARWallArtPrototype/
└── ArtTechArtistPanel/
```

`ArtTechBackend`, `ARWallArtPrototype` and `ArtTechArtistPanel` are
three separate Git repositories with separate histories.

`ArtTechArtistPanel` is the third independent repository with its own published
`origin/master` history. Implementation, validation and Git publication are
tracked independently from the backend and Unity repositories.

The artist panel exists and uses **standalone Blazor WebAssembly
targeting .NET 10**.

The parent `AR-Home-Exhibition` directory is only a shared workspace and
must not be treated as a Git monorepo.

------------------------------------------------------------------------

## 3. Source of truth

Use actual source code, configuration, migrations, manifests, tests and
serialized Unity assets to establish implementation state. This document
records product intent, architecture and milestone history. The current task
prompt defines the authorized change; an older roadmap must not override it.

If this document disagrees with the actual implementation, the code is
the source of truth.

Report the discrepancy instead of blindly changing the implementation to
match stale documentation.

------------------------------------------------------------------------

## 4. Repositories

### Backend

Repository:

``` text
ArtTechBackend
```

Technology:

-   .NET 10
-   ASP.NET Core Web API
-   Entity Framework Core
-   PostgreSQL
-   Npgsql
-   ASP.NET Core Identity
-   bearer authentication

Architecture:

``` text
ArtTechBackend.slnx
├── ArtTechGallery.API/
├── ArtTechGallery.Core/
└── ArtTechGallery.Infrastructure/
```

The backend remains a layered monolith.

`ArtTechGallery.API.Tests/` is also part of the solution. It contains the
maintained integration tests using isolated PostgreSQL databases, not an
additional application service.

Do not introduce microservices without an explicit architectural
decision.

### Unity

Repository:

``` text
ARWallArtPrototype
```

Technology:

-   Unity 6000.3.9f1
-   AR Foundation 6.3.5
-   ARCore 6.3.5
-   URP 17.3.0
-   Input System 1.18.0
-   Android target

### Artist panel

Repository:

``` text
ArtTechArtistPanel
```

Technology and architecture:

-   standalone Blazor WebAssembly
-   .NET 10
-   separate static browser application
-   HTTP communication with the existing `ArtTechBackend`
-   existing ASP.NET Core Identity bearer registration/login/refresh
    flow
-   no second Identity store
-   no direct PostgreSQL access
-   no SignalR circuit dependency

------------------------------------------------------------------------

## 5. Domain model

Current core relationship:

``` text
User
  └── ArtistProfile
        └── Exhibition
              └── Artwork
```

Public identifiers are separate from database IDs.

Important public identifiers:

-   `ProfileCode`
-   `ExhibitionCode`

Do not replace these with database GUIDs in public application flows.

------------------------------------------------------------------------

## 6. Viewer authentication

The viewer does not create an account and does not log in.

Public exhibition viewing must remain available through public
identifiers.

Authentication is intended for artist-side management functionality.

Do not introduce viewer authentication unless the product requirements
explicitly change.

------------------------------------------------------------------------

## 7. AR direction

The primary AR interaction for the MVP is **Quick Mode**.

Quick Mode intentionally does not depend on plane detection as its
primary placement mechanism.

Expected behavior:

-   artwork appears in front of the camera,
-   physical artwork dimensions are preserved,
-   artwork follows the camera smoothly while unlocked,
-   distance can be adjusted,
-   artwork can be locked/unlocked,
-   movement and rotation are smoothed,
-   the interaction should remain simple and predictable.

The existing implementation uses interpolation such as:

-   `Vector3.Lerp`
-   `Quaternion.Slerp`

Plane detection was explored earlier but is not the primary MVP
direction.

Do not redesign the project around plane detection unless explicitly
requested.

------------------------------------------------------------------------

## 8. Backend --- implemented state

The backend currently provides the public foundation required by the
Unity viewer.

Implemented public endpoints include:

``` text
GET /api/profiles/{profileCode}
GET /api/exhibitions/{exhibitionCode}
GET /api/artworks/{id:guid}
```

The exhibition endpoint provides artwork metadata required by Unity,
including:

-   title,
-   description,
-   creation year,
-   physical width,
-   physical height,
-   image URL,
-   sort order.

The backend uses:

-   EF Core migrations,
-   PostgreSQL,
-   seeded development/demo data,
-   unique public codes,
-   flat DTOs,
-   `AsNoTracking` where appropriate,
-   ProblemDetails-based error handling.

Production artwork storage/upload architecture has not yet been
implemented.

The first Artist Management Foundation slice is now implemented and
validated:

``` text
GET /api/artist/profile
POST /api/artist/profile
PUT /api/artist/profile
```

These management endpoints use existing Identity bearer authentication
and an `ActiveUser` policy that checks the current database user exists
and is active. The policy deliberately does not require an
ArtistProfile, so a new user can onboard. Ownership always comes from
the authenticated user ID. Requests accept only display name and
biography and reject unknown JSON fields. Public profile codes are
generated server-side independently of database IDs, with unique-index
collision retries. An inactive profile remains readable by its active
owner but cannot be edited here. The public exhibition endpoint now also
checks the owning user's activity. No database migration was needed for
this slice.

------------------------------------------------------------------------

### Owned Exhibition Management --- completed

The authenticated Owned Exhibition Management backend slice is
implemented and validated.

Management endpoints:

``` text
GET  /api/artist/exhibitions
POST /api/artist/exhibitions
GET  /api/artist/exhibitions/{id}
PUT  /api/artist/exhibitions/{id}
POST /api/artist/exhibitions/{id}/publish
POST /api/artist/exhibitions/{id}/deactivate
```

Lifecycle:

``` text
Draft → Published → Deactivated → Published
```

New exhibitions default to Draft. Draft and Deactivated exhibitions are
hidden from anonymous viewers. Published exhibitions are public only
when the owning user/profile also satisfies the existing
public-visibility rules. Invalid or repeated transitions return 409.

Ownership is derived exclusively from the authenticated user.
Cross-owner and missing managed resources return 404. Inactive users
receive 403. Inactive profiles remain owner-readable but cannot write.

`ExhibitionCode` is generated server-side, is unique and independent of
database IDs, remains stable across metadata edits and lifecycle
transitions, and uses five bounded collision attempts.

Migration `20260915055802_ExhibitionPublicationStatus` replaces
`Exhibition.IsActive` with constrained `ExhibitionStatus`: - existing
active rows → Published, - existing inactive rows → Deactivated, - new
rows → Draft.

IDs, codes, metadata and artwork relationships are preserved.

Anonymous exhibition, profile and artwork endpoints now require
published exhibition content where relevant. The Unity/public response
contract is unchanged.

Validation: - restore passed, - build passed with 0 warnings/errors, -
57 PostgreSQL integration tests passed (33 existing + 24 added), -
`git diff --check` passed, - EF pending-model check found none, -
migration upgrade/rollback, lifecycle, ownership, collision/concurrency
behavior and exact public contract are covered.

The migration has only been exercised against isolated test databases so
far and must accompany backend deployment.

------------------------------------------------------------------------

## 9. Development-only artwork assets

A development-only mechanism exists for real end-to-end Runtime Artwork
Loading tests.

The backend contains dedicated JPG/PNG fixture assets for the demo
exhibition.

They are served under:

``` text
/dev-assets/artworks
```

only when the application runs in the `Development` environment and a
valid:

``` text
DevelopmentDemo:PublicBaseUrl
```

is configured.

The public base URL is intentionally separate from the server listening
address.

For example, the server may listen on:

``` text
0.0.0.0
```

while returned artwork URLs must use an address actually reachable by
the Unity device.

Machine-specific LAN addresses must not be committed as shared project
configuration.

The development seeding flow supports both fresh and already-existing
demo databases.

For recognized demo artwork URLs it can idempotently update development
image URLs while:

-   preserving artwork IDs,
-   preserving ordering,
-   preserving dimensions,
-   preserving metadata,
-   preserving custom image URLs,
-   avoiding unnecessary updates when the desired URL is already
    present.

Development fixture images are excluded from production publish output.

This mechanism is only development infrastructure for testing.

It is not the future production artwork storage architecture.

------------------------------------------------------------------------

## 10. Unity API integration

Unity contains an API client capable of loading an exhibition by its
public `ExhibitionCode`.

The API response is converted into runtime artwork data.

Physical dimensions received in centimeters are converted to meters for
the AR representation.

Runtime artwork ordering follows the API exhibition data.

The scene is wired to the API loading flow.

------------------------------------------------------------------------

## 11. Runtime Artwork Loading --- completed

The Runtime Artwork Loading milestone is complete.

Unity now:

-   downloads JPG/PNG artwork textures from `imageUrl` at runtime,
-   validates image URLs,
-   applies download timeouts,
-   supports cancellation,
-   assigns downloaded textures to the correct runtime `ArtworkData`,
-   preserves artwork ordering,
-   preserves physical dimensions,
-   handles individual image download failures,
-   handles total loading failure,
-   exposes loading/error/progress state,
-   provides retry behavior,
-   prevents overlapping exhibition loads,
-   prevents stale textures from a previous artwork remaining visible,
-   hides unavailable artwork visuals appropriately,
-   cleans up owned runtime textures,
-   supports navigation between artworks,
-   deterministically prepares the Quick Mode prefab,
-   supports a manually configurable API base URL for development.

The relevant implementation includes:

``` text
Assets/Scripts/API/ExhibitionApiClient.cs
Assets/Scripts/API/ExhibitionApiTest.cs
Assets/Scripts/AR_Exhibition_Manager.cs
Assets/Scripts/AR_QuickMode_Manager.cs
Assets/Scenes/SampleScene.unity
```

The current development scene contains the runtime API loading flow and
controls for navigation/retry.

------------------------------------------------------------------------

## 12. Runtime Artwork Loading validation

The Unity-side implementation was validated through:

-   Unity 6000.3.9f1 batch import/compilation,
-   19 temporary Editor validation checks,
-   `git diff --check`,
-   manual real-device testing.

The backend development E2E support was validated through:

-   restore/build with zero warnings and errors,
-   79 integration checks against an isolated PostgreSQL development
    database,
-   fresh demo seeding,
-   repair of existing placeholder URLs,
-   repeat-startup idempotency,
-   unchanged row versions when no update is required,
-   custom URL preservation,
-   metadata preservation,
-   managed development-origin changes,
-   image HTTP responses,
-   MIME type verification,
-   exact fixture bytes,
-   development-only asset routing,
-   Production/Staging asset route rejection,
-   invalid configuration validation,
-   publish-output verification.

A real end-to-end test has been completed successfully on a physical
Android device.

Verified flow:

``` text
PostgreSQL
    ↓
ASP.NET Core / .NET API
    ↓
Exhibition JSON with imageUrl
    ↓
Development JPG/PNG assets over LAN
    ↓
Unity runtime download
    ↓
ArtworkData
    ↓
Quick Mode AR
    ↓
Physical Android device
```

The Android device successfully reached the backend over the local
network, downloaded the runtime artwork assets and rendered the
exhibition through the Unity application.

Therefore, Runtime Artwork Loading must no longer be treated as an
unfinished milestone.

------------------------------------------------------------------------

## 13. Current project state

The project currently has a working vertical slice for the viewer side:

``` text
Database
    ↓
Public exhibition API
    ↓
Runtime artwork metadata
    ↓
Runtime artwork texture
    ↓
Unity
    ↓
Quick Mode AR
```

This vertical slice has been demonstrated locally on a physical Android
device.

The backend public API foundation is substantially implemented.

The Unity viewer has a functional API-driven AR exhibition flow.

The viewer vertical slice, artist profile/frontend foundation, Owned
Exhibition Management API and Blazor Exhibition Management UI are
working. Artwork Management API/UI and local upload/storage are complete.
The Unity public artist-profile / Published-exhibition navigation is also
implemented and validated in the Editor against the real backend (section 26).

------------------------------------------------------------------------

## 14. Frontend technology decision

The artist web panel is implemented in **standalone Blazor WebAssembly
targeting .NET 10**.

This architecture is now fixed unless the user explicitly reopens the
decision.

The panel uses the existing `ArtTechBackend` as its single backend and:

-   reuses the existing ASP.NET Core Identity `/register`, `/login` and
    `/refresh` endpoints,
-   does not create a second Identity database or duplicate user store,
-   does not access PostgreSQL directly,
-   communicates with the backend through HTTP APIs,
-   remains a separate Git repository named `ArtTechArtistPanel`,
-   keeps environment-specific API URLs out of committed
    machine-specific configuration.

High-level architecture:

``` text
Browser
    ↓
ArtTechArtistPanel (standalone Blazor WebAssembly)
    ↓ HTTP
ArtTechBackend (ASP.NET Core API)
    ↓
PostgreSQL
```

The browser session foundation uses an in-memory access token and
tab-scoped `sessionStorage` refresh token. Refresh is serialized,
authenticated 401 responses are retried at most once after refresh, and
logout/session generation handling prevents stale protected responses
from restoring logged-out UI state.

## 15. Artist Panel --- Frontend Foundation completed

The Frontend Foundation milestone is complete and validated.

Implemented vertical slice:

``` text
register / login / refresh
        ↓
authenticated artist session
        ↓
protected /profile
        ↓
GET /api/artist/profile
        ↓
404 => onboarding
existing profile => details => explicit Edit profile
        ↓
POST / PUT /api/artist/profile
        ↓
fresh anonymous GET /api/profiles/{profileCode}
```

Implemented behavior includes:

-   real Identity registration/login/refresh integration,
-   protected routing and logout,
-   access token held in memory,
-   refresh token stored in tab-scoped `sessionStorage`,
-   session restoration through `/refresh`,
-   serialized refresh/token state changes,
-   one authenticated-request retry after a 401,
-   handling for 403, validation, loading and recoverable errors,
-   profile onboarding and editing against the real backend,
-   fresh anonymous public-profile verification after save,
-   configurable exact-origin backend CORS without wildcard origins or
    cookie credentials.

Validation completed on 2026-09-14/15:

-   panel restore/build passed cleanly,
-   30 panel xUnit/bUnit tests passed,
-   Release publish passed,
-   backend restore/build passed cleanly,
-   33 backend PostgreSQL tests passed,
-   Chrome E2E passed against both the development server and published
    Release assets,
-   real registration/login, onboarding, edit, reload/refresh, invalid
    login, logout/protected navigation, second isolated account,
    rejected refresh and injected 401 → refresh → single successful
    write retry were exercised,
-   Unity was untouched,
-   no schema changes or migrations were introduced.

Known limitations at this stage:

-   browser JavaScript/XSS can access browser-held tokens,
-   logout is local to the tab and is not server-side token revocation,
-   there is no durable remember-me or refresh-token reuse detection,
-   production HTTPS/CORS/CSP/static SPA routing, persistent Data
    Protection keys and broader browser/device/accessibility checks
    remain deployment concerns.

Do not treat the artist panel as planned or nonexistent, and do not
treat its hosting model as undecided.

### Roadmap at the Frontend Foundation checkpoint

The next milestone at that checkpoint was **Owned Exhibition Management API**.
Before implementation it required decisions on draft/published/deactivated
states, ownership from the authenticated user, public visibility, stable
server-generated ExhibitionCode and allowed transitions. Section 8 records
the completed implementation of those decisions; it is no longer future work.

The sequence from that checkpoint was:

1. Owned Exhibition Management API (now complete).
2. Exhibition Management UI (now complete).
3. Artwork Management API (now complete and validated; section 17).
4. Artwork Management UI (now complete and validated).
5. File Upload + Local Storage Foundation (now complete).
6. Sharing / QR / deep links.
7. MVP stabilization and deployment.
8. Shared Web + Unity visual polish, real-device measurements, and only then
   measurement-justified KTX2 consideration.

------------------------------------------------------------------------

## 16. Exhibition Management UI --- completed

The Blazor Exhibition Management UI milestone is complete and validated.

Protected routes:

``` text
/exhibitions
/exhibitions/new
/exhibitions/{id}
```

The functional artist flow is now:

``` text
profile
  ↓
own exhibitions
  ↓
create Draft
  ↓
edit metadata
  ↓
publish
  ↓
deactivate / republish
```

The UI: - consumes the real Owned Exhibition Management API, - reuses
the existing bearer authentication, serialized refresh and
single-401-retry behavior, - displays server-reported Draft / Published
/ Deactivated status, - exposes only valid lifecycle actions for the
current state, - prevents duplicate submissions/actions while requests
run, - preserves form input after failed saves, - re-fetches
authoritative state after 409 conflicts, - handles validation, 403, 404,
network and server errors, - provides onboarding guidance for missing
profiles, - remains read-only where appropriate for inactive profiles, -
requires confirmation before deactivation, - displays the stable
`ExhibitionCode` for published exhibitions.

Validation: - restore passed, - build passed with 0 warnings/errors, -
59 panel tests passed, including 29 added for this slice, - Release
publish passed, - Chrome against final Release output passed with no
runtime errors, - responsive checks at 320, 390 and 1280 px showed no
horizontal overflow, - browser E2E against a disposable PostgreSQL
database covered create/edit/publish/deactivate/republish, anonymous
visibility, reload restoration, real 409 recovery, cross-owner rejection
and existing profile/authentication flows.

Backend and Unity were not changed.

Remaining checks: production hosting, Firefox/Safari, physical mobile
browsers and screen-reader accessibility. Unsaved edits do not survive
navigation/reload. The current UI remains functional MVP UI rather than
the final shared Web + Unity visual identity.

------------------------------------------------------------------------

## 17. Artwork Management API --- completed

The **Artwork Management API** is complete and validated.

The owned nested API supports list/create/read/update/hard-delete under
`/api/artist/exhibitions/{exhibitionId}/artworks`. Ownership is derived through
the authenticated user's exhibition; unavailable/cross-owner resources return
404. ActiveUser and inactive-profile write restrictions are preserved.
Artworks are manageable in Draft, Published and Deactivated exhibitions;
published edits take effect immediately. Anonymous visibility requires an
active artwork, Published parent, active profile and active user.

Editable metadata is title, description, creationYear, widthCm, heightCm,
imageUrl and sortOrder. Dimensions remain centimeters (1–1000, at most two
fractional decimal places). Ordering is SortOrder then ID; ties/gaps are valid.
IDs/relationships/activity/timestamps are server controlled. Unknown fields
are rejected. Removal is permanent, without deleting an external image.

The backend now accepts authenticated multipart JPG/PNG uploads and replacements
behind provider-neutral `IArtworkStorage`; local runtime storage is the current
implementation and production cloud storage is a later provider replacement.
Existing JSON external-ImageUrl compatibility remains temporary. Development
assets remain fixtures only. Public JSON/Unity units are unchanged.

Validation on 2026-09-15: restore passed; build passed with 0 warnings/errors;
82 PostgreSQL integration tests passed, 0 failed/skipped (57 prior + 25 added);
EF reported no pending model changes; tracked/untracked whitespace checks passed.
No new migration or seed implementation change was needed. Demo compatibility
and deletion races are covered. Unity and Blazor were untouched; no fresh
physical Android run was performed for this slice.

See `ArtTechBackend/docs/ARTWORK_MANAGEMENT.md` for contracts, ordering,
concurrency and temporary image-reference limitations. Artwork UI is now complete below.

### Artwork Management UI — completed

The Blazor Artwork Management UI is complete and validated. From an owned
exhibition, Manage artworks opens the list; artists can add, edit and permanently
delete artwork with confirmation. Protected routes are
`/exhibitions/{exhibitionId}/artworks`, `/new` and `/{id}` under that list route.
Forms expose metadata, centimeter dimensions, JPG/PNG file selection and display order.
They reuse the existing authenticated HTTP/session infrastructure and ProblemDetails
handling. Inactive profiles are read-only. Failed saves preserve input; duplicate
writes and stale responses after navigation/logout are guarded. The backend
remains authoritative for ownership, visibility and validation.

Validation on 2026-09-15: panel restore/build passed (0 warnings/errors), 85 panel
tests passed (26 added), Release publish passed, backend restore/build passed and
all 82 isolated PostgreSQL tests passed. Chrome on final Release assets passed
real artwork CRUD, public metadata/image/deletion checks, reload restoration,
protected routes and cross-owner rejection; no runtime errors. Editor widths
1280/390/320 px and a 320 px list had no horizontal overflow. Whitespace checks
passed. Backend source and Unity were unchanged; no migration was added.

Remaining limitations: production cloud storage is absent, unsaved input does not survive navigation,
creation has no idempotency key and metadata edits have no revision conflict
check. Existing browser-token security limitations remain. Production hosting,
Firefox/Safari, screen readers and physical mobile browsers need separate checks.
No fresh Unity/Android run was performed. See `ArtTechArtistPanel/VALIDATION.md`.

### Artist Panel navigation correction — completed

Profile and exhibition pages now default to details views. A new user without
an artist profile still receives onboarding. Existing profiles expose Edit profile;
successful saves return to details with fresh anonymous public verification.
Exhibition creation opens its details/management page. Edit exhibition is explicit,
and saving or cancelling returns to details. Publication/status/code controls stay
on the management view. The existing artwork list is also shown there with Add
artwork and explicit Edit artwork actions. Artwork creation, saving and confirmed
deletion return to the parent exhibition details page. The standalone artwork-list
route remains available. Edit modes reuse the current routes and components.

Profile 404 investigation: the backend returns 404 only when the authenticated
user has no ArtistProfile. Browser automation observed exactly one such response
for initial onboarding and no additional profile 404s after creation, including
reload, login and exhibition/artwork navigation. No API/auth/cache workaround was
needed. The embedded artwork list reuses parent profile/exhibition state rather
than repeating those reads.

Validated after interruption recovery: panel restore/build passed (0 warnings or
errors); all 87 panel tests passed; Release publish passed (optional wasm-tools
workload remains absent); all 82 isolated PostgreSQL tests passed. Chrome against
final Release output passed View -> Edit -> Save -> View, onboarding, lifecycle,
artwork CRUD, anonymous access, profile response counts and session/ownership
regressions, with no runtime errors. Populated exhibition details and artwork
forms passed 320/390/1280 px overflow checks; the 320 px details screenshot was
reviewed. Whitespace checks passed. Backend, APIs, migrations and Unity were not
changed. This validation predates the now-complete local upload/storage
foundation. Production cloud/provider storage remains unstarted. Production hosting, other
browsers, screen readers and physical mobile-device checks remain outstanding.

The File Upload + Local Storage Foundation is complete. Production cloud
storage remains a later provider replacement.

Sequence recorded after Artwork Management UI (with later completion noted):

1.  Public artist profile / Published exhibition viewer flow in Unity (now implemented; section 26).
2.  Sharing / QR / deep-link flow using ProfileCode and ExhibitionCode.
3.  Complete functional UI/navigation states.
4.  Final coherent Web + Unity UI/UX/visual identity under 7system.
5.  Replace LocalArtworkStorage with a chosen production storage provider.
6.  MVP deployment/stabilization.
7.  Real transfer/loading/memory measurements.
8.  Consider KTX2 only if measurements justify it.

KTX2 remains a later measurement-driven optimization, not an MVP
blocker.

------------------------------------------------------------------------

## 18. Production image storage

The File Upload + Local Storage Foundation is complete. Production cloud/provider
storage is intentionally not implemented; replace LocalArtworkStorage later
without changing the panel, public contracts or Unity image loading.

The current `/dev-assets/artworks` mechanism must not be promoted into
production storage architecture.

When production upload/storage becomes the active task:

1.  inspect the current backend and deployment assumptions,
2.  define the minimum storage requirements,
3.  choose an architecture intentionally,
4.  keep storage concerns separated from domain/public identifiers,
5.  avoid premature optimization.

Initial product support should prioritize normal JPG/PNG files.

KTX2 or more advanced texture delivery remains a later optimization and
should only be considered after real measurements show it is needed.

------------------------------------------------------------------------

## 19. Deep link and QR

Deep link / QR remains part of the intended viewer flow but is not yet
complete.

Target experience:

``` text
Artist publishes exhibition
        ↓
public link / QR
        ↓
viewer opens exhibition
        ↓
Unity resolves ExhibitionCode
        ↓
API loads exhibition
        ↓
Quick Mode AR
```

Do not let deep-link work block the artist-management foundation unless
the current task explicitly targets viewer entry/sharing.

------------------------------------------------------------------------

## 20. Out of scope for current MVP work

Unless explicitly requested, do not add:

-   social feeds,
-   comments,
-   messaging,
-   likes/follows,
-   marketplace functionality,
-   payments,
-   viewer accounts,
-   microservices,
-   complex exhibition editors,
-   plane detection as the primary viewer interaction,
-   KTX2 as an initial requirement,
-   speculative production infrastructure unrelated to the current
    milestone.

------------------------------------------------------------------------

## 21. Working rules for coding agents

Before changing code:

1.  Read `AGENTS.md`.
2.  Read this file.
3.  Inspect the actual files relevant to the requested task.
4.  Compare documentation with actual implementation.
5.  Identify the smallest complete solution.

During implementation:

-   work only within the requested scope,
-   preserve existing architecture unless change is justified,
-   avoid unrelated refactors,
-   do not commit machine-specific configuration,
-   do not add secrets,
-   keep development-only infrastructure clearly separated from
    production behavior,
-   preserve public codes,
-   preserve viewer-without-login behavior,
-   preserve Quick Mode as the primary AR mode.

After implementation:

1.  summarize changed files,
2.  explain behavior,
3.  report validation performed,
4.  report remaining risks/manual checks,
5.  show Git status for every modified repository.

Do not commit or push unless explicitly instructed.

------------------------------------------------------------------------

## 22. Git

The workspace currently contains three independent repositories:

``` text
ArtTechBackend/
ARWallArtPrototype/
ArtTechArtistPanel/
```

All three repositories keep separate histories and must be committed
separately.

The artist panel repository has its initial milestone history and configured
GitHub `origin` remote. Backend and panel milestone changes are committed and
published independently; Unity reports `master...origin/master` and remains
clean. Recheck Git status before each task rather than treating this snapshot
as permanent.

Changes must be committed separately.

Do not run Git commands against the parent workspace as if it were a
monorepo.

The completed Unity Runtime Artwork Loading implementation has been
committed and pushed.

Backend development E2E support should likewise remain a logically
separate backend change.

Local machine configuration such as LAN IP addresses must not be
committed.

------------------------------------------------------------------------

## 23. Coding-model routing

When model selection is available, use the lightest model that can safely
complete the task:

-   **Luna** --- documentation, Git/status work, small mechanical
    changes, simple tests and cleanup.
-   **Terra** --- ordinary CRUD, DTOs, validation, frontend
    components/forms and straightforward API integration.
-   **Sol** --- larger features, auth flows, complex tests, non-trivial
    refactors and debugging.
-   **Astra** --- architecture audits, cross-cutting decisions,
    Unity/AR, storage/security/ownership design and difficult end-to-end
    slices.

This routing is a cost/limit strategy, not a product requirement. All
models must follow the same `AGENTS.md`, this context file and the
actual codebase.

Routing preferences do not authorize spawning sub-agents, do not require
delegation, and do not imply that the active model can switch itself.

Do not use Astra for repetitive work that can safely follow an
established pattern with a smaller model.

------------------------------------------------------------------------

## 24. Definition of the MVP

The MVP is successful when:

1.  an artist can authenticate,
2.  an artist can manage their profile,
3.  an artist can create and manage exhibitions,
4.  an artist can add artworks with images and physical dimensions,
5.  an exhibition can be published and shared,
6.  a viewer can open it without creating an account,
7.  Unity loads the exhibition and artwork images from the backend,
8.  the viewer can select an artwork,
9.  Quick Mode displays the artwork at the intended physical scale.

The viewer-side API → image → AR portion of this flow is now working
locally and has been verified on a physical Android device.

Owned exhibition management is complete in both backend and Blazor UI.
Artwork Management API and UI are complete and validated. File Upload + Local
Storage Foundation is also complete; the next work is the viewer/sharing
sequence recorded below.

------------------------------------------------------------------------

## 25. File Upload + Local Storage Foundation --- completed 2026-09-16

The backend supports authenticated multipart JPG/PNG artwork creation and
replacement with a 10 MiB limit, 4096×4096 pixel limit, content/decode
validation, immutable managed image versions and controlled anonymous image
delivery. `IArtworkStorage` is provider-neutral; `LocalArtworkStorage` stores
runtime files outside Git. Existing DevelopmentAssets remain test fixtures.
The public `imageUrl` contract is unchanged for Unity, no schema migration was
required, and Unity was not modified.

The panel no longer asks artists to type ImageUrl. Create requires a JPG/PNG
selection, edit retains the existing image unless replaced, and multipart 401
refresh/replay preserves metadata and exact bytes through the existing bearer
handler.

Validation: backend restore/build and Release publish passed with zero
warnings/errors; 92 PostgreSQL tests passed; EF reported no model changes and
publish contained no RuntimeData or DevelopmentAssets. Panel restore/build and
Release publish passed; 93 tests passed; real Release Chrome E2E against the
actual backend, disposable PostgreSQL and isolated storage passed upload,
metadata-only edit, replacement, publication visibility, deactivation/republish,
reload, deletion and 320/390/1280 responsive checks with no browser runtime
errors.

Known local-storage limitation: a process crash after a managed image is
finalized but before its database transaction commits can leave a finalized
orphan. This is accepted technical debt; no background cleanup subsystem is
part of this foundation.

The sequence recorded at completion of local upload/storage was:

1. Public artist profile / Published exhibition viewer flow in Unity (now implemented; section 26).
2. Sharing / QR / deep-link flow using ProfileCode and ExhibitionCode.
3. Complete functional UI/navigation states.
4. Final coherent Web + Unity UI/UX/visual identity under 7system.
5. Replace LocalArtworkStorage with a chosen production storage provider.
6. MVP deployment/stabilization.
7. Real transfer/loading/memory measurements.
8. Consider KTX2 only if measurements justify it.

Profile and exhibition QR/link entries will be normal public links containing
ProfileCode and ExhibitionCode. The installed app should resolve each link into
the correct Unity screen; without the app, the link should use an install or
fallback path. Unity's public profile shows only Published exhibitions. The
viewer flow is Artist Profile → Published Exhibitions → Exhibition → Artwork →
Quick Mode AR. Draft and Deactivated exhibitions are anonymous-invisible, the
viewer needs no account, and final visual design is intentionally postponed
until functional flows exist. The product/company brand is 7system; final UI
should be coherent and intentionally designed rather than generic.

------------------------------------------------------------------------

## 26. Public artist profile / Published exhibition viewer — implemented 2026-09-16

The Unity Android viewer remains anonymous and now provides the functional
single-scene navigation flow:

```text
Entry
  -> Artist Profile
  -> Published Exhibitions
  -> Exhibition
  -> Artwork
  -> Quick Mode AR
```

Entry accepts either a public `ProfileCode` or a public `ExhibitionCode`.
`OpenProfile` and `OpenExhibition` accept codes only, establish a new navigation
root, cancel incompatible work and are intentionally independent from future
URL, QR and Android App Link handling. Direct ExhibitionCode navigation returns
to Entry; an exhibition reached through a profile returns to that profile.
Visible Back and Android/system Back use the same controller.

The implementation consumes the existing public contracts without DTO changes.
Profile shows public artist metadata and the backend-filtered Published
exhibition summaries. Selecting a summary always loads the exhibition afresh,
so deactivation between discovery and selection returns an unavailable state.
Draft and Deactivated exhibitions remain hidden from anonymous viewers.

The existing `ExhibitionApiTest` component retains its serialized identity and
continues to perform sequential runtime JPG/PNG loading. Navigation controls its
lifetime explicitly: the loaded exhibition and loader-owned textures survive
Exhibition -> Artwork -> AR navigation, while preview and Quick Mode borrow the
same texture. Leaving or replacing an exhibition detaches consumers, cancels
requests and releases textures. Width/height remain centimeters in the public
API and are converted to meters exactly once for Quick Mode.

Profile and exhibition requests distinguish unavailable content from safe
recoverable errors. Empty profiles/exhibitions, partial/total image failures and
Retry have functional states. Cancellation is paired with generation checks so
late successes, late errors and stale Retry callbacks cannot replace a newer
navigation root. Artwork metadata remains available when its image fails, and
View in AR is disabled until that artwork has a usable texture.

The SampleScene no longer commits a machine-specific LAN API URL or an automatic
demo ExhibitionCode. Local Editor/Android builds use ignored
`Assets/Resources/ViewerLocal.json` for a reachable backend origin. The backend
listening address, `ArtworkStorage:PublicBaseUrl` and storage filesystem root
remain separate. Local configuration, managed runtime uploads and generated
build/test output stay outside Git.

Validation completed on 2026-09-16: Unity compiled successfully; 2 EditMode
scene/configuration tests passed; a 19-test PlayMode run passed. The latter ran
the actual SampleScene against the actual backend with disposable PostgreSQL and
isolated managed-image storage, and verified ProfileCode -> Published exhibition
-> JPG/PNG artwork -> preview/Quick Mode preparation, direct ExhibitionCode
entry, empty-profile state, deactivation/unavailability, republishing and
network failure/retry. Automated tests also cover profile parsing, back history,
stale callbacks, retry generations, partial/total image failure, texture
release, centimeter-to-meter conversion and Quick Mode lock/frame state.

No physical Android device was connected for this new navigation validation.
The previously verified physical-device flow proves public exhibition JSON,
runtime image loading, physical dimensions and Quick Mode AR, but ProfileCode
entry, direct ExhibitionCode entry and Android Back still require the documented
device smoke test in `ARWallArtPrototype/docs/PUBLIC_VIEWER.md`.

The current next major milestone is **Sharing / QR / deep-link flow using
ProfileCode and ExhibitionCode**. QR generation, URL parsing, Android App Links,
deferred deep linking and web/install fallback are not implemented yet.
