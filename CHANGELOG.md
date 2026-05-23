# Changelog

All notable changes to **cloudsmith-core** will be documented in this file.

The format is based on [Keep a Changelog 1.1.0](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [0.5.0] - 2026-05-23

### Added

- `core.relays` and `core.relay_enrollment_tokens` tables for the on-prem Relay enrollment flow (migration `M20260523008`).
- `inventory:write` and `monitoring:write` permission grants for ingest paths from the Relay (migration `M20260523009`).

### Changed

- RBAC seed broadened to provision the `OrgAdmin`, `OrgViewer`, and `Auditor` roles with their default permission sets (migration `M20260523006`).

### Removed

- `core.secrets_references` table — consolidated into `core.secret_refs` introduced in 0.4.0 (migration `M20260523007`).

## [0.4.0] - 2026-05-23

### Added

- `core.user_invitations` table to back the Users-and-invitations portal page (migration `M20260523002`).
- `core.secret_refs` table — the canonical platform secret-reference store (migration `M20260523004`).
- Platform / Identity / Secrets / Audit permission grants attached to the `PlatformAdmin` role (migration `M20260523005`).

## [0.3.0] - 2026-05-23

### Added

- `core.identity_providers` table to persist IdP configuration created via the in-platform IdP wizards (migration `M20260523001`).

## [0.2.0] - 2026-05-22

### Added

- `core.platform_setup` and `core.local_credentials` tables, plus the `SetupService`, to back the ADR-047 first-run setup wizard (migration `M20260522001`).
