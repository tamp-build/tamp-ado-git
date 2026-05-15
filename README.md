# Tamp.AdoGit

> PAT-injected git wrapper for Azure DevOps. Every fetch / push / clone authenticates via `git -c http.extraHeader="AUTHORIZATION: Basic <b64>"` so multi-tenant developer machines don't get the wrong identity picked by Git Credential Manager.

| Package | Status |
|---|---|
| `Tamp.AdoGit` | 0.1.0 (initial) |

## Why this exists

On developer machines that authenticate to multiple Azure DevOps tenants, Git Credential Manager picks an identity per-host but doesn't know which tenant's PAT applies. The standard workaround — `git -c http.extraHeader=...` on every command — is the most-repeated boilerplate in ADO-targeted build scripts and inter-agent automation. `Tamp.AdoGit` bakes that into every verb so consumers stop maintaining the snippet.

## Install

```bash
dotnet add package Tamp.AdoGit
```

Multi-targets net8 / net9 / net10. Requires `git` on PATH.

## Quick start

```csharp
using Tamp;
using Tamp.AdoGit;

class Build : TampBuild
{
    public static int Main(string[] args) => Execute<Build>(args);

    [FromPath("git")] readonly Tool Git = null!;

    [Secret("ADO PAT", EnvironmentVariable = "ADO_PAT")]
    readonly Secret AdoPat = null!;

    Target Fetch => _ => _.Executes(() => AdoGit.Fetch(Git, AdoPat, s => s
        .SetRemote("origin")
        .SetTags()
        .SetPrune()));

    Target PushFeature => _ => _.Executes(() => AdoGit.Push(Git, AdoPat, s => s
        .SetRef("HEAD:refs/heads/STRATA-460-fix")
        .SetUpstreamFlag()));
}
```

## Verb surface

| Tamp method | git command | Notes |
|---|---|---|
| `AdoGit.Fetch(...)` | `git -c <auth> fetch` | `--tags`, `--prune`, `--depth=N`, refspec opt-in. |
| `AdoGit.Push(...)` | `git -c <auth> push` | `Ref` required. `ForceWithLease` over raw `--force` by design. |
| `AdoGit.PullRebase(...)` | `git -c <auth> pull --rebase` | `--autostash` default-on. |
| `AdoGit.Clone(...)` | `git -c <auth> clone` | `Depth`, `Branch`, `TargetDirectory` optional. |
| `AdoGit.LsRemote(...)` | `git -c <auth> ls-remote` | `--exit-code` for "does this ref exist" gates. |
| `AdoGit.Raw(...)` | `git -c <auth> <args...>` | Escape hatch — header still prepended. |

## How the auth header works

Every `CommandPlan` produced by this package starts with:

```
git -c http.extraHeader=AUTHORIZATION: Basic <base64(":<pat>")> <verb> ...
```

ADO accepts Basic auth where the username is empty and the password is the PAT. The PAT is `Secret`-typed; the runner's redaction table covers it so any logging that captures the raw command line shows `<Secret:ado-pat>` rather than the value.

For additional `git -c` pairs (e.g. `user.email=ci@example.com`), chain `AddConfig(...)`:

```csharp
AdoGit.Push(Git, AdoPat, s => s
    .SetRef("HEAD:refs/heads/main")
    .AddConfig("user.email", "ci@example.com")
    .AddConfig("user.name", "CI Bot"));
```

The auto-injected auth-header `-c` pair comes first; additional `-c` pairs follow in declaration order.

## Force-push policy

`Push.ForceWithLease` (mapping to `--force-with-lease`) is the only force mode exposed. Raw `--force` is intentionally absent — it's destructive against shared branches and easy to misuse. If you genuinely need raw `--force`, drop to `AdoGit.Raw(Git, pat, "push", "--force", ...)` — making the choice visible at the call site is the design point.

## Sibling packages

- [`Tamp.AdoRest.V7`](https://github.com/tamp-build/tamp-ado-rest) — typed wrappers for the ADO REST API (work items, builds, releases).
- [`Tamp.AdoServiceConnection`](https://github.com/tamp-build/tamp-ado-service-connection) — Workload Identity Federation orchestration.
- [`Tamp.Gh`](https://github.com/tamp-build/tamp-gh) — `gh` CLI wrapper for GitHub-targeted projects.

## Releasing

Releases follow the [Tamp dogfood pattern](MAINTAINERS.md): bump `<Version>` in `Directory.Build.props`, tag `v<X.Y.Z>`, GitHub Actions runs `dotnet tamp Ci` then `dotnet tamp Push`.

## Settings authoring style

Examples above use the fluent `Set*`-chain shape. Every wrapper verb also accepts a `new XxxSettings { ... }` object-init form — both produce identical `CommandPlan`s. The fluent shape stays canonical in docs and the `tamp init` template; opt into object-init scaffolding via `tamp init --settings-style=init`.

See [Build Script Authoring → Two authoring styles](https://github.com/tamp-build/tamp/wiki/Build-Script-Authoring#two-authoring-styles-for-wrapper-calls-120) on the wiki for the side-by-side comparison.

## License

MIT. See [LICENSE](LICENSE).
