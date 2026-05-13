# Changelog

All notable changes to **Tamp.AdoGit** are recorded here.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/);
versions follow [SemVer](https://semver.org/spec/v2.0.0.html).

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
