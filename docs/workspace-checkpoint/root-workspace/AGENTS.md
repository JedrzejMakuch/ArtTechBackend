# AR Home Exhibition --- Coding Agent Instructions

## Workspace

This workspace contains three independent Git repositories:

- `ArtTechBackend/` --- .NET backend and PostgreSQL integration tests.
- `ARWallArtPrototype/` --- Unity/AR Android client.
- `ArtTechArtistPanel/` --- standalone Blazor WebAssembly artist panel.

The panel is the third repository, not a backend subproject. It is an
independent repository with its own published `origin/master` history.

Keep all three as separate Git repositories with separate histories.

Do not treat the parent workspace as a monorepo and do not merge
repository histories.

Before doing project work, read `docs/PROJECT_CONTEXT.md`. Then inspect
the actual relevant code before proposing or making changes.

## Source of truth

Use actual code, configuration, migrations, manifests and tests to establish
what exists. Use `docs/PROJECT_CONTEXT.md` for product intent and recorded
milestone status. The current task prompt defines the authorized change;
do not use an older roadmap to override the user's current instructions.

If documentation conflicts with code, do not silently reshape the code
to match stale documentation. Report the discrepancy and use the code as
the truth about implementation state.

## Working rules

-   Work only on the requested milestone/task. Do not expand scope
    without asking.
-   Preserve the existing architecture unless there is a concrete
    technical reason to change it.
-   Prefer the smallest complete change that fits existing conventions.
-   Search for existing implementations before creating new abstractions
    or duplicate code.
-   Do not introduce microservices.
-   Do not add viewer authentication.
-   Quick Mode is the primary AR mode. Do not make plane detection a
    core dependency.
-   Keep public `ProfileCode` / `ExhibitionCode` identifiers separate
    from database IDs.
-   JPG/PNG working flow comes before KTX2 or premature asset
    optimization.
-   Do not build social-network features, marketplace/payments, viewer
    accounts or a complex image editor as part of MVP.
-   Do not commit secrets, real credentials or machine-specific
    configuration.
-   Avoid unrelated formatting/refactors while implementing a focused
    task.

## Frontend architecture decision

The artist web panel technology is **Blazor / .NET**.

Do not propose React, Vue, Angular or another frontend framework unless
the user explicitly reopens that architectural decision.

The panel must: - use the existing `ArtTechBackend` as the single
backend/API; - reuse the existing ASP.NET Core Identity
registration/login/refresh endpoints; - not introduce a second Identity
database or duplicate user store; - not access PostgreSQL directly; -
communicate with the backend through HTTP APIs; - remain a separate Git
repository named `ArtTechArtistPanel`; - keep environment-specific API
URLs out of committed machine-specific configuration.

The frontend hosting model is fixed: **standalone Blazor WebAssembly
targeting .NET 10**.

It is a separate static browser application that communicates with
`ArtTechBackend` over HTTP. Do not introduce a frontend server or a
SignalR-circuit-dependent Blazor Server design unless the user
explicitly reopens this architectural decision.

The implemented frontend foundation connects to real existing backend
endpoints rather than mocked exhibition/artwork APIs.

## Validation

For backend changes: - inspect `git status` before and after; - run
restore/build for the affected solution/projects; - run relevant tests
if present; - do not create migrations unless the model/schema actually
changes.

For Unity changes: - keep compatibility with Unity `6000.3.9f1`, AR
Foundation `6.3.5`, ARCore `6.3.5`, URP `17.3.0`; - preserve serialized
Inspector references where possible; - avoid editor-only APIs in runtime
code; - consider Android networking/runtime behavior, not only Editor
Play Mode; - do not edit generated Unity folders or commit generated
artifacts.

## Change protocol

Unless the user explicitly asks for immediate implementation: 1. inspect
relevant files; 2. summarize current behavior; 3. propose a short
implementation plan; 4. identify files expected to change and risks; 5.
wait for approval before editing.

After implementation: - summarize changed files and behavior; - state
exactly what validation was run and its result; - state
limitations/manual checks still required; - show `git status` for each
affected repository; - do not commit or push unless explicitly asked.

## Model routing

When model selection is available, use the lightest model that can safely
complete the task. These are routing preferences, not an instruction to
spawn sub-agents or a claim that the active model can switch itself.

-   **Luna** --- documentation, Git/status checks, small mechanical
    edits, simple tests, cleanup.
-   **Terra** --- routine CRUD, DTOs, validation, ordinary frontend
    components/forms, straightforward API wiring.
-   **Sol** --- larger features, authentication flows, complex tests,
    non-trivial refactors and debugging.
-   **Astra** --- architecture audits, cross-repository/vertical-slice
    design, Unity/AR work, security/ownership/storage decisions,
    difficult end-to-end problems.

Do not spend Astra on repetitive implementation when a smaller model can
follow an established pattern. Escalate to a stronger model when the
task crosses architectural boundaries, has meaningful
security/data-ownership risk, or requires difficult debugging.

## Completed Frontend Foundation checkpoint

The fixed frontend architecture is standalone Blazor WebAssembly on .NET 10,
using the existing backend Identity endpoints and user store over HTTP.

```text
register/login/refresh -> protected /profile -> onboarding/edit
    -> fresh anonymous public-profile verification
```

The foundation is complete and validated. Recorded results from 2026-09-14/15:
panel restore/build and Release publish passed; 30 panel tests and 33 backend
PostgreSQL tests passed; Chrome E2E passed against development and Release
builds. Those counts describe that milestone, not later expanded suites.
No schema migration or Unity change was part of Frontend Foundation.

Access tokens remain memory-only; refresh tokens use tab-scoped sessionStorage.
Refresh is serialized, authenticated 401 is retried once, and session-generation
checks discard stale responses after logout. JavaScript/XSS can access browser
tokens; logout is local to the tab without server revocation; durable remember-me
and refresh-token reuse detection are absent. Production HTTPS/CORS/CSP/static
SPA routing, persistent Data Protection keys and broader device/accessibility
checks remain deployment work.

At this checkpoint, the next milestone was **Owned Exhibition Management API**.
Before implementing it, the required decisions were explicit draft/published/
deactivated states, authenticated ownership, anonymous visibility, stable
server-generated ExhibitionCode and allowed transitions. These are now
implemented; preserve them rather than treating the completed API as missing.
The sequence was Exhibition API -> Exhibition UI -> Artwork API -> Artwork UI
-> production storage -> sharing/QR/deep links -> MVP stabilization/deployment.

## Implemented artist-management state

Runtime Artwork Loading, Artist Management Foundation, Artist Panel ---
Frontend Foundation, Owned Exhibition Management API, and **Exhibition
Management UI** are complete and validated.

The Blazor artist panel now provides the functional authenticated flow:

``` text
register / login
        ↓
artist profile
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

Implemented protected routes: - `/exhibitions` --- own exhibition list,
empty state and lifecycle actions, - `/exhibitions/new` --- create a
Draft exhibition, - `/exhibitions/{id}` --- details, artwork management and publication, with explicit metadata editing.

The UI uses the existing bearer/session infrastructure and the real
Owned Exhibition Management API. It shows server-reported Draft /
Published / Deactivated state, prevents duplicate actions while requests
run, preserves failed-save input, re-fetches authoritative state after
409 conflicts, handles validation/403/404/network/server errors,
requires confirmation for deactivation, and displays the stable
`ExhibitionCode` for published exhibitions.

Exhibition Management UI validation: - panel restore passed, - build
passed with 0 warnings/errors, - 59 panel tests passed (29 added for
this slice), - Release publish passed, - Chrome validation against final
Release output passed with no runtime errors, - responsive checks passed
at 320, 390 and 1280 px without overflow, - browser E2E used a
disposable PostgreSQL database and covered full lifecycle, anonymous
visibility, reload/session restoration, genuine 409 recovery,
cross-owner rejection and existing profile/auth flows, - backend and
Unity were unchanged.

Remaining frontend checks include production hosting, Firefox/Safari,
physical mobile browsers and screen-reader testing. Unsaved edits do not
survive navigation/reload.

Do not treat Exhibition Management UI as unfinished.

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

The API accepts multipart JPG/PNG uploads behind `IArtworkStorage`; local
runtime storage is the current provider and production cloud storage is a later
replacement. Existing JSON external-ImageUrl compatibility remains temporary.
Development assets remain fixtures only. Public JSON/Unity units are unchanged.

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
changed in that navigation slice. Local upload/storage is now complete behind
the storage abstraction; production hosting, other
browsers, screen readers and physical mobile-device checks remain outstanding.

The File Upload + Local Storage Foundation is now complete. Do not treat
production cloud storage as implemented; it remains a later provider swap.

### Not implemented yet

Do not treat these as complete: - production cloud/provider storage, - sharing /
QR / deep links, - statistics, - final Web + Unity visual redesign, - KTX2.
The local upload/storage foundation and functional Unity public-viewer
navigation are implemented; preserve them.

### Roadmap after File Upload + Local Storage Foundation

1.  Public artist profile / Published exhibition viewer flow in Unity (now implemented).
2.  Sharing / QR / deep-link flow using ProfileCode and ExhibitionCode.
3.  Complete functional UI/navigation states.
4.  Final coherent Web + Unity UI/UX/visual identity under 7system.
5.  Replace LocalArtworkStorage with a chosen production storage provider.
6.  MVP deployment/stabilization.
7.  Real transfer/loading/memory measurements.
8.  Consider KTX2 only if measurements justify it.

Do not commit or push unless explicitly asked.

## Current verified state — 2026-09-16

The File Upload + Local Storage Foundation is complete. The backend accepts
authenticated multipart JPG/PNG artwork uploads and replacements, validates
content and dimensions, stores immutable managed image versions behind the
provider-neutral `IArtworkStorage` boundary, and serves current managed images
through the controlled anonymous image endpoint. The local implementation is
runtime-only and must remain outside Git; `DevelopmentAssets` remain fixtures.
No database migration was required. The public `imageUrl` contract remains
compatible with Unity, and Unity was not modified.

The panel uploads JPG/PNG files instead of asking artists to type `ImageUrl`.
Create requires a file; edit retains or replaces the current image. Existing
bearer refresh/retry behavior replays multipart requests exactly once with their
metadata and bytes.

Validation is complete: backend restore/build and Release publish passed with
zero warnings/errors, 92 PostgreSQL tests passed, EF reported no model change,
and no RuntimeData or DevelopmentAssets entered publish output. Panel
restore/build and Release publish passed, 93 tests passed, and real Release
Chrome E2E passed against the backend with disposable PostgreSQL and isolated
storage, including upload, replacement, visibility, reload, deletion and
320/390/1280 responsive checks.

Accepted local-storage technical debt: a process crash after an image is
finalized but before its database transaction commits can leave a finalized
orphan. Do not expand this milestone into a background cleanup subsystem.

The sequence recorded when local upload/storage was completed was:

1. Public artist profile / Published exhibition viewer flow in Unity (now implemented).
2. Sharing / QR / deep-link flow using `ProfileCode` and `ExhibitionCode`.
3. Complete functional UI/navigation states.
4. Final coherent Web + Unity UI/UX/visual identity under 7system.
5. Replace `LocalArtworkStorage` with a chosen production storage provider.
6. MVP deployment/stabilization.
7. Real transfer/loading/memory measurements.
8. Consider KTX2 only if measurements justify it.

Product decisions for later viewer/sharing work: profile and exhibition QR/link
entries are normal public links containing their respective public codes; the
installed app resolves a link into the correct Unity screen, while a device
without the app follows an install/fallback path. Unity's public profile shows
only Published exhibitions. The viewer flow is Artist Profile → Published
Exhibitions → Exhibition → Artwork → Quick Mode AR. Draft and Deactivated
exhibitions remain anonymous-invisible, viewers need no account, and final
visual design is intentionally postponed until functional flows exist. The
product/company brand is 7system; final UI should be coherent and intentionally
designed rather than generic.

## Current verified state — public Unity viewer

The functional anonymous Unity viewer now supports:

```text
manual public-code entry
    -> Artist Profile -> Published Exhibitions -> Exhibition -> Artwork
    -> Quick Mode AR
```

It also accepts a direct `ExhibitionCode` entry. `OpenProfile(profileCode)` and
`OpenExhibition(exhibitionCode)` establish navigation roots using public codes;
they do not parse URLs or Android intents. Entry, Profile, Exhibition, Artwork
and AR remain states in the existing SampleScene. Visible Back and Android Back
share the same hierarchy. The existing runtime image loader and Quick Mode
physical-scale path are retained rather than duplicated.

The profile client consumes the unchanged public profile contract. Only
Published exhibitions returned by the backend appear. A fresh public exhibition
request remains authoritative, so an exhibition deactivated after profile
discovery becomes gracefully unavailable. Requests use cancellation plus
navigation generations so stale successes and errors cannot replace newer
screens. The exhibition loader owns runtime textures across Exhibition,
Artwork and AR; preview and Quick Mode borrow those textures until explicit
release. Partial image failures retain usable artwork and metadata.

The committed scene contains no developer LAN origin and no automatic demo
code. Local Editor/Android builds read an ignored
`Assets/Resources/ViewerLocal.json`; backend listening address, managed-image
public origin and filesystem storage root remain separate configuration.

Automated validation completed on 2026-09-16: Unity compiled cleanly; 2 EditMode
scene/configuration tests passed; a 19-test PlayMode run passed against the real
SampleScene and included an opt-in real-backend flow with disposable PostgreSQL
and isolated image storage. It covered public ProfileCode entry, Published-only
listing, managed JPG/PNG downloads, shared preview/AR texture, physical unit
conversion, direct ExhibitionCode entry, empty profile, deactivation,
republishing and network retry. The new navigation has not yet been rerun on a
physical Android device; the earlier physical-device result covers the runtime
artwork-loading/Quick Mode path only.

The current next product milestone is **Sharing / QR / deep-link flow using
ProfileCode and ExhibitionCode**. Do not implement it unless explicitly asked.
