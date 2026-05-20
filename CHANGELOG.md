# Changelog — vianigram

All notable changes to this repository are documented here. The format
follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and
this project adheres to [Semantic Versioning](https://semver.org/).

Unreleased changes are listed under `## [Unreleased]`. Each tagged
release moves the content from there into a dated `## [vX.Y.Z] — YYYY-MM-DD`
heading.

---

## [Unreleased]

### Added
- _Track new features here._

### Changed
- VoIP backend consolidation: managed code (`Vianigram.Calls`,
  `Vianigram.Composition`) now references `Vianium.VoIP` from the sibling
  `vianium-voip` repository directly, rather than going through an
  intermediate native wrapper.
- SRP-2048 (Telegram 2FA) WinRT projection moved from the legacy
  `Vianigram.Crypto` namespace to the new `Vianium.Crypto` namespace.
  Managed call sites (`NativeSrpClientPort.cs`) updated; the WinRT
  surface is otherwise identical (same `SrpClient.ComputeProofAsync`
  signature, same `SrpProofResult` shape).

### Deprecated
- _Track soon-to-be-removed surfaces._

### Removed
- `Core/Vianigram.Core.Voip/` native project removed — was a stale
  duplicate (PolyForm-NC license, fewer metrics, older retry policy) of
  the code that already lives, fully refreshed and Apache-2.0, in the
  `vianium-voip` repo (see `vianium-voip/src/voip/`). Sln entry,
  `build-validate.cmd` line, and managed `ProjectReference`s pruned.
  No public managed surface changed: `Vianigram.Calls` keeps the same
  hex ports; only the underlying native binary moved.

### Fixed
- _Track bug fixes._

### Security
- _Track security-relevant fixes (CVE references where applicable)._

---

## [v0.1.0] — TBD

Initial public release as part of the [Vianium](https://github.com/vianium)
org migration. License: PolyForm-Noncommercial-1.0.0. Tier: Product.

See [`NOTICE`](NOTICE) for copyright attribution and
[`vianium-docs/MIGRATION_PLAN.md`](https://github.com/vianium/vianium-docs/blob/main/MIGRATION_PLAN.md)
for the cross-repo migration narrative.

[Unreleased]: https://github.com/vianium/vianigram/compare/v0.1.0...HEAD
[v0.1.0]: https://github.com/vianium/vianigram/releases/tag/v0.1.0
