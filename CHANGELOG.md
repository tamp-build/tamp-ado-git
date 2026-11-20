# Changelog

All notable changes to **Tamp.AdoGit** are recorded here.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/);
versions follow [SemVer](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [0.2.0] - 2026-11-20 - .NET 11 in, .NET 8 and .NET 9 out

### Changed

- **Multi-targets `net10.0;net11.0`** instead of `net8.0;net9.0;net10.0` ([ADR 0015](https://github.com/tamp-build/tamp/blob/main/docs/adr/0015-target-framework-strategy.md)). .NET 11 shipped 2026-11-10 and .NET 8 and .NET 9 reached end of support the same day; the rule is to add a TFM the day a release ships and drop one the day Microsoft ends its support. `global.json` pins SDK 11.0.100.

  **If you target `net8.0` or `net9.0`, stay on the previous version** — this one carries no assets your project can resolve.

- **`Tamp.*` pinned to 1.21.0**, which carries a SARIF fix worth knowing about if this project's output is read as evidence: before 1.21.0, a SARIF result whose scanner omitted `level` was read as `none` — the least severe value — instead of the declared `warning`, on every runtime before net11 ([tamp#166](https://github.com/tamp-build/tamp/issues/166)).

- **Publishing moved to nuget.org trusted publishing (OIDC).** The `tamp_nuget_api_key` org secret expired 2026-11-01 along with every other pre-2026-08-17 nuget.org key. The publish job now mints a short-lived key from its own OIDC token, so there is no long-lived credential to rotate or leak.

## [0.1.2] — 2026-09-27

### Fixed

- **Redaction now covers the base64 auth credential, not just the raw PAT** ([#5](https://github.com/tamp-build/tamp-ado-git/issues/5)). The `-c http.extraHeader=AUTHORIZATION: Basic <b64>` placed on the command line contains `base64(":" + pat)` — a *different literal* than the raw PAT, so registering only the PAT left the transmitted credential unredacted (redaction matches values literally). The command plan now registers **both** the PAT and its derived base64 credential (via `Secret.Derive`, Tamp.Core 1.15.2), so either form is scrubbed from any logged output / `--capture-logs` artifact. Latent (git doesn't echo `-c` values today) but one behaviour-change away, and capture logs are published as CI artifacts. Requires **Tamp.Core ≥ 1.15.2**.

### Changed

- Bumped `Tamp.Core` pin 1.10.0 → 1.15.2 (for `Secret.Derive`).

## [0.1.1] — 2026-09-27

### Added

- Package now ships XML documentation files (`.xml`) alongside the assembly, so consumers get IntelliSense and API docs. (Mirrors [tamp-build/tamp#3](https://github.com/tamp-build/tamp/pull/50).)


## [0.1.0] - 2026-05-13

### Added

- Initial release. PAT-injected git wrapper for Azure DevOps. Verb surface: `Fetch`,
  `Push`, `PullRebase`, `Clone`, `LsRemote`, `Raw`. Filed under TAM-174.

- Auto-injected `-c http.extraHeader=AUTHORIZATION: Basic <b64>` on every command.
  PAT is `Secret`-typed and propagates into `CommandPlan.Secrets` for redaction.

- `AddConfig(key, value)` for additional `git -c` pairs (e.g. `user.email`).

- `Push.ForceWithLease` exposes only `--force-with-lease` (not raw `--force`) — adopters
  who genuinely need raw `--force` use the `Raw` escape hatch, making the choice visible
  at the call site.

### Notes

- Driven by Strata's adoption-wave gap list 2026-05-13 (P0 priority — most-repeated
  boilerplate across Strata's pipelines and inter-agent automation). Pinned to
  `Tamp.Core` / `Tamp.NetCli.V10` at 1.4.1 (the version whose `InternalsVisibleTo` list
  includes `Tamp.AdoGit`).
