# AR Home Exhibition — Development MVP checkpoint

Checkpoint date: 2026-09-17

## Status

**DEVELOPMENT MVP IMPLEMENTED.** The three repositories remain independent:

- `ArtTechBackend` — .NET 10 API, Identity, PostgreSQL, exhibition/artwork management, local managed-image storage, public APIs.
- `ArtTechArtistPanel` — standalone Blazor WebAssembly .NET 10 artist panel.
- `ARWallArtPrototype` — Unity 6000.3.9f1 Android/AR viewer.

Implemented flow: authentication → artist profile → exhibitions → artworks → JPG/PNG local upload/storage → publish → public share link/QR; viewer entry by `ProfileCode` or `ExhibitionCode` → published exhibition → artwork → Quick Mode AR at physical scale.

Implemented sharing includes public profile/exhibition links, QR presentation, custom-scheme Android deep-link routing, web fallback, and cold-start/runtime routing. Public visibility remains published-only; viewers do not need accounts.

## Validation status

Automated validation is strong, but the final combined MVP is **not physically Android validated**.

- Artist Panel: 99 passed, 0 failed, 0 skipped; previous browser smoke/E2E passed.
- Unity: EditMode 11 passed; PlayMode 21 passed, with 1 opt-in real-backend test skipped.
- Backend: maintained PostgreSQL suite previously recorded as 93 passed.
- Android physical smoke test: **PENDING**. Unity/Unity Hub became unstable, Gradle/local cache failed, and C: ran out of space. A fresh APK after the launcher-manifest correction was not produced successfully.

The first task after workstation restoration is **PHYSICAL ANDROID MVP SMOKE TEST**. Do not describe the MVP as fully physically validated until it passes.

## Workspace documentation backup

The parent directory is intentionally not a Git repository. A snapshot of the root `AGENTS.md` and `docs/` is stored in this repository under `docs/workspace-checkpoint/root-workspace/` so a clean clone can reconstruct the workspace guidance. The root files remain authoritative when the workspace is assembled again; this copy is the remote recovery copy.

## Intentionally local and excluded

Do not commit or rely on these files being remotely preserved: backend `ArtTechGallery.API/appsettings.Development.json`, Unity `Assets/Resources/ViewerLocal.json`, backend runtime `RuntimeData`/managed uploads, Unity `Library`, `Temp`, `Logs`, `Builds`, `UserSettings`, .NET `bin`/`obj`, panel publish artifacts, test output, and any local credentials/tokens or LAN-specific values. Recreate them from examples and the restore checklist.

## Interruption and resume

Windows/workstation reset is planned. The Unity/Gradle environment is unreliable and physical Android validation was interrupted. Resume with local configuration recreation and the physical Android smoke test; do not start a new product milestone from this checkpoint.
