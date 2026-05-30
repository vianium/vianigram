# Changelog — vianigram

All notable changes to this repository are documented here. The format
follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and
uses the Windows package version form (`major.minor.build.revision`) for
app releases.

Unreleased changes are listed under `## [Unreleased]`. Each tagged
release moves the content from there into a dated version heading.

---

## [Unreleased]

- _Track changes after `0.1.2.0` here._

---

## [v0.1.2.0] — 2026-05-30

### Added
- MTProxy entry points on the welcome, phone-number, and QR-login flows so
  network setup is reachable before sign-in.
- QR-login compatibility smoke coverage for token parsing and polling paths.
- Dialog snapshot loading, avatar resolution, avatar disk cache, and stable
  generated placeholders for peers without photos.
- Document file fetching and progressive audio/video buffering through the
  media bounded context.
- Persistent DC options, endpoint health, media DC prewarm, and imported
  authorization cache support for faster media bootstrap.
- Auth-key reuse policy documentation for the MTProto/session storage path.

### Changed
- Release metadata, package manifest, assembly versions, and visible app text
  now target the Vianigram `0.1.2.0` alpha package.
- Release packaging is prepared for a tagged `Release|ARM` APPX artifact.
- VoIP backend consolidation: managed code (`Vianigram.Calls`,
  `Vianigram.Composition`) now references `Vianium.VoIP` from the sibling
  `vianium-voip` repository directly, rather than going through an
  intermediate native wrapper.
- SRP-2048 (Telegram 2FA) WinRT projection moved from the legacy
  `Vianigram.Crypto` namespace to the new `Vianium.Crypto` namespace.
  Managed call sites (`NativeSrpClientPort.cs`) updated; the WinRT
  surface is otherwise identical (same `SrpClient.ComputeProofAsync`
  signature, same `SrpProofResult` shape).
- Login and media text now consistently names MTProxy and the visible app
  version as `0.1.2.0`.
- Media fetching now separates lightweight thumbnails/previews from
  user-triggered document and progressive playback downloads.
- Media autodetect downloads are bounded by a small concurrency gate to avoid
  flooding the MTProto channel on image-heavy chats.

### Removed
- `Core/Vianigram.Core.Voip/` native project removed — was a stale
  duplicate (PolyForm-NC license, fewer metrics, older retry policy) of
  the code that already lives, fully refreshed and Apache-2.0, in the
  `vianium-voip` repo (see `vianium-voip/src/voip/`). Sln entry,
  `build-validate.cmd` line, and managed `ProjectReference`s pruned.
  No public managed surface changed: `Vianigram.Calls` keeps the same
  hex ports; only the underlying native binary moved.

### Fixed
- Spanish login/QR status copy accents corrected for the `0.1.2.0` alpha.
- APPX packaging version alignment for the main app and smoke-runner
  manifests.
- Login and QR pages now expose the MTProxy settings path before an account
  session exists.

### Security
- MTProto auth-key reuse and media-DC imported authorization behavior is now
  documented and backed by explicit storage ports.

---

## [v0.1.0] — TBD

Initial public release as part of the [Vianium](https://github.com/vianium)
org migration. License: PolyForm-Noncommercial-1.0.0. Tier: Product.

See [`NOTICE`](NOTICE) for copyright attribution and
[`vianium-docs/MIGRATION_PLAN.md`](https://github.com/vianium/vianium-docs/blob/main/MIGRATION_PLAN.md)
for the cross-repo migration narrative.

[Unreleased]: https://github.com/vianium/vianigram/compare/v0.1.2.0...HEAD
[v0.1.2.0]: https://github.com/vianium/vianigram/compare/v0.1.0...v0.1.2.0
[v0.1.0]: https://github.com/vianium/vianigram/releases/tag/v0.1.0
