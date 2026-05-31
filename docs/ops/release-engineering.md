# Sentinel.CLI — Release Engineering

**Scope:** packaging, CI, release, and the end-user install story for `Sentinel.CLI`, distributed as a .NET global tool.
**Date:** 2026-05-30
**Status:** Accepted

The product wedge is **frictionless .NET-native install**: `dotnet tool install -g Sentinel.CLI` and one command to run — no Docker, no Go binary. Every decision here is in service of keeping that install path one line long and reliable. Where a choice would tax that path, it is called out explicitly.

> The repo is not yet a git repo with a GitHub remote. This document and the two workflows are written for when it becomes one. The one-time prerequisites (lock files, MinVer, nuget.org policy) are flagged below and must be done before the first `release.yml` run.

---

## 1. `dotnet tool` packaging

### 1.1 Host csproj properties

`src/Sentinel.CLI/Sentinel.CLI.csproj` already declares `PackAsTool`, `ToolCommandName=sentinel`, `PackageId=Sentinel.CLI`, and `PackageOutputPath`. The packaging metadata below should be added so the nuget.org listing is complete and SourceLink works. Recommended final shape of the host project's first `PropertyGroup`:

```xml
<PropertyGroup>
  <OutputType>Exe</OutputType>
  <RootNamespace>Sentinel.CLI</RootNamespace>
  <AssemblyName>sentinel</AssemblyName>

  <!-- Tool packaging -->
  <PackAsTool>true</PackAsTool>
  <ToolCommandName>sentinel</ToolCommandName>
  <PackageId>Sentinel.CLI</PackageId>
  <PackageOutputPath>$(MSBuildThisFileDirectory)..\..\artifacts\</PackageOutputPath>
  <IsPackable>true</IsPackable>

  <!-- Runtime roll-forward: lets the tool run on a newer 10.x ASP.NET Core /
       runtime than it was built against. Becomes load-bearing once the Receiver
       (Phase 3) pulls in the Microsoft.AspNetCore.App framework reference. -->
  <RollForward>LatestMinor</RollForward>

  <!-- nuget.org listing metadata -->
  <Description>Local-first OpenTelemetry receiver with an interactive terminal UI. No Docker, no browser.</Description>
  <Authors>Sentinel.CLI</Authors>
  <PackageTags>opentelemetry;otlp;tracing;observability;tui;terminal;dotnet-tool;cli</PackageTags>
  <PackageLicenseExpression>MIT</PackageLicenseExpression>
  <PackageReadmeFile>README.md</PackageReadmeFile>
  <PackageIcon>icon.png</PackageIcon>
  <PackageProjectUrl>https://github.com/OWNER/Sentinel.CLI</PackageProjectUrl>
  <RepositoryUrl>https://github.com/OWNER/Sentinel.CLI</RepositoryUrl>
  <RepositoryType>git</RepositoryType>
  <PublishRepositoryUrl>true</PublishRepositoryUrl>
</PropertyGroup>

<ItemGroup>
  <None Include="..\..\README.md" Pack="true" PackagePath="\" />
  <None Include="..\..\assets\icon.png" Pack="true" PackagePath="\" />
</ItemGroup>
```

Replace `OWNER` with the GitHub org/user once the remote exists.

**`PackageIcon` / `PackageReadmeFile` fail the pack if the file is missing (NU5046) — they error, they do not warn.** Two order-sensitive consequences:
- `assets/icon.png` must exist (128×128 PNG, < 1 MB) **before** the tagged release, or **omit the `PackageIcon` line entirely**. Pasting the metadata block without the file in place breaks the release pack.
- The packed `README.md` **is the nuget.org listing page** — `PackageReadmeFile=README.md` ships `..\..\README.md` as the package's front page. The current README is stale (`net8.0`, "skeleton only"); it must be refreshed **before** the first publish, not as later cleanup, or the listing ships wrong.

### 1.2 Determinism + SourceLink (CI-gated)

Add to `Directory.Build.props` so symbols resolve to the published commit. **Gate the CI-only knobs** so local builds in a not-yet-git folder do not fail — SourceLink warns when there is no git remote, and with `TreatWarningsAsErrors=true` that warning becomes a hard error.

```xml
<PropertyGroup Condition="'$(GITHUB_ACTIONS)' == 'true'">
  <ContinuousIntegrationBuild>true</ContinuousIntegrationBuild>
  <DeterministicSourcePaths>true</DeterministicSourcePaths>
  <EmbedUntrackedSources>true</EmbedUntrackedSources>
  <IncludeSymbols>true</IncludeSymbols>
  <SymbolPackageFormat>snupkg</SymbolPackageFormat>
</PropertyGroup>
```

Add `Microsoft.SourceLink.GitHub` to CPM and reference it from the host project (or solution-wide via `Directory.Build.props` once the remote exists). Until then, leave SourceLink out so local builds stay green; it is a release-quality nicety, not a blocker for the first publish.

### 1.3 Keep the tool package to one project

`dotnet pack` on the **solution** would emit a `.nupkg` for every packable project — including the Domain/Application/Infrastructure/Tui libraries, which have no business on nuget.org. Two defenses, both applied:

1. **Pack the project, not the solution.** `release.yml` runs `dotnet pack src/Sentinel.CLI/Sentinel.CLI.csproj`, never `dotnet pack Sentinel.CLI.sln`.
2. **Belt and braces:** set `<IsPackable>false</IsPackable>` on the four library `src` projects (Domain, Application, Infrastructure, Tui). Today only the `.Tests` projects carry `IsPackable=false` (via the `Directory.Build.props` `.Tests` condition); the libraries do not. Add it to each library csproj's `PropertyGroup`.

### 1.4 The ASP.NET framework-reference question

**Can a `PackAsTool` package that (transitively, via the Receiver) framework-references `Microsoft.AspNetCore.App` be installed as a global tool? Yes — with a runtime requirement at *run* time, not install time.**

Today the tool references Application + Infrastructure + Tui, none of which carry ASP.NET. The current package is a plain console tool, framework-dependent on the base `Microsoft.NETCore.App` shared framework only — maximally frictionless. The ASP.NET constraint is a **Phase 3 (Receiver) concern**, not a today concern.

When `Sentinel.CLI.Receiver` lands, it carries `<FrameworkReference Include="Microsoft.AspNetCore.App" />`. That framework reference flows transitively to the host through the `ProjectReference`, and the host's pivot to `WebApplication.CreateSlimBuilder` requires it directly. From that point the tool package framework-references ASP.NET Core. Consequences, stated precisely:

- **Install always succeeds; run can fail.** `dotnet tool install -g` only unpacks the nupkg and writes a shim. It does **not** check shared frameworks. Framework resolution happens at first launch. A machine that has only the **base .NET runtime** (not the ASP.NET Core runtime, not the SDK) will install fine and then fail at launch with: `The framework 'Microsoft.AspNetCore.App', version '10.0.x' was not found.`
- **Primary mitigation — the audience already has it.** The audience is .NET developers debugging their own .NET apps. They have the .NET SDK, which bundles the ASP.NET Core shared framework. For them, framework-dependent "just works." Document the requirement as: **".NET 10 SDK, or the ASP.NET Core 10 Runtime."**
- **Roll-forward.** `<RollForward>LatestMinor</RollForward>` (§1.1) lets a tool built against 10.0 run on a later 10.x ASP.NET Core runtime, so users on patched runtimes are not blocked. This is the explicit runtime-config knob; do not rely on the default.

**Self-contained / RID-specific tool packaging (hedge — verify before adopting).** Modern .NET supports self-contained tools that bundle the runtime so no shared framework is required on the target. This buys reach to non-SDK machines at a real cost: per-RID packages (win-x64, linux-x64, osx-arm64, …), a much larger download (tens of MB vs. the small framework-dependent package), and changed install UX. **Verify the exact .NET 10 mechanism and flags against current docs before committing to it** — do not treat the specifics as settled here. Recommendation for v0: ship the small framework-dependent package for the SDK audience; revisit self-contained only if telemetry shows non-SDK installs failing at launch.

### 1.5 Terminal.Gui native/console considerations

Terminal.Gui v2.4.3 is **pure managed** (ADR-0004 confirms "no native dependencies beyond the .NET runtime"). There are **no RID-specific native assets** — one package works on Windows, macOS, and Linux. Nothing in the TUI layer forces a RID-specific tool package. The only cross-terminal concern (ASCII bars, graceful monochrome degradation) is a rendering choice already made in the TUI layer, not a packaging concern.

---

## 2. CI workflow (`.github/workflows/ci.yml`)

Triggers on `pull_request` and `push` to `main`. Pinned, least-privilege, single build-test job on `ubuntu-24.04`.

- **`permissions: {}`** at the top (deny-all); the job grants only `contents: read`.
- **Concurrency** `ci-…-${{ github.head_ref || github.ref }}` with `cancel-in-progress: true` — superseded PR runs are cancelled (no external state mutated, so safe).
- **SDK via `global.json`.** `actions/setup-dotnet` uses `global-json-file: global.json` with no `version:` input, honoring the pinned `10.0.300` + `rollForward: latestFeature`.
- **Locked-mode restore.** `dotnet restore --locked-mode` fails on stale/missing lock files — deterministic restore under CPM. **Prerequisite:** lock files must exist first (§5.2).
- **Warnings as errors** comes free from `Directory.Build.props` (`TreatWarningsAsErrors=true`); no extra flag.
- **Test results** uploaded as an artifact (TRX + coverage), with `if: ${{ !cancelled() }}` so a test failure still uploads the report.

Pinned actions (SHAs verified against the GitHub API on 2026-05-30):

| Action | Tag | SHA |
|---|---|---|
| `actions/checkout` | v6.0.2 | `de0fac2e4500dabe0009e67214ff5f5447ce83dd` |
| `actions/setup-dotnet` | v5.3.0 | `9a946fdbd5fb07b82b2f5a4466058b876ab72bb2` |
| `actions/upload-artifact` | v7.0.1 | `043fb46d1a93c77aae656e7c1c64a875d1fc6a0a` |

---

## 3. Release workflow (`.github/workflows/release.yml`)

Triggers on a pushed tag `v*`. Packs the tool and publishes to nuget.org.

### 3.1 Feed: nuget.org (not GitHub Packages)

**Recommendation: nuget.org.** This is the single most important release decision for a global tool, because it directly governs the install command length — the product wedge.

`dotnet tool install -g` from **GitHub Packages** requires `--add-source https://nuget.pkg.github.com/OWNER/index.json` **and authentication**, even for public packages — GitHub Packages has no anonymous read for NuGet. The user must create a PAT, configure a `nuget.config` with credentials, then install. That is the opposite of frictionless. nuget.org is the default feed: `dotnet tool install -g Sentinel.CLI` works with zero configuration on any machine with the .NET SDK. For a tool whose entire pitch is one-line install, GitHub Packages is disqualified.

### 3.2 Versioning: MinVer (tag-driven)

**Recommendation: MinVer.** The tag is the single source of truth — push `v1.2.3`, get package `1.2.3`. No `version.json` to keep in sync, no manual `<Version>` bump to forget. Untagged commits ahead of the last tag automatically become pre-releases (`1.2.4-alpha.0.N`), which feeds the `--prerelease` install path naturally.

Nerdbank.GitVersioning was considered and rejected: it shines when you need a four-part assembly version controlled separately from the NuGet version. A single global tool has no such need — the extra `version.json` machinery is unjustified.

Setup (one-time):
- Add `MinVer` to CPM and reference it from the host project.
- Set `<MinVerTagPrefix>v</MinVerTagPrefix>` in the host csproj so MinVer matches `v1.2.3` tags.
- `release.yml` checks out with `fetch-depth: 0` — MinVer reads tag history; a shallow clone yields `0.0.0`.

### 3.3 Idempotent push

The workflow does **not** create tags (a human pushes the tag), so there is no tag-collision concern. The idempotency lever is `dotnet nuget push --skip-duplicate`: re-running a failed release job re-pushes an already-published version as a no-op success instead of erroring. The `.snupkg` symbol package alongside the `.nupkg` is picked up automatically.

### 3.4 Secrets: OIDC trusted publishing (primary), API key (fallback)

**Primary: nuget.org Trusted Publishing via OIDC.** No long-lived API key stored anywhere. The job declares `id-token: write`, and `NuGet/login@v1.2.0` exchanges the GitHub OIDC token for a temporary nuget.org API key valid for 1 hour, requested immediately before push.

One-time setup on nuget.org (Username → Trusted Publishing → add policy):
- **Repository Owner:** the GitHub org/user
- **Repository:** `Sentinel.CLI`
- **Workflow File:** `release.yml` (file name only, no path)
- **Environment:** `release` (matches the `environment: release` in the job)

The only secret stored is `NUGET_USER` (the nuget.org **profile name**, not email) — kept as a secret so it is not echoed in logs, not because it is sensitive. The `release` GitHub environment also gates the job behind required reviewers — appropriate for a publish step.

**Private-repo 7-day activation window.** On a newly created **private** repo, a Trusted Publishing policy starts temporarily active for 7 days; if no publish happens in that window it goes inactive (nuget.org needs the repo/owner IDs from a successful publish's OIDC token to lock the policy against resurrection attacks). If Sentinel.CLI starts private, either publish a `v0.x` within 7 days of creating the policy, or restart the window from the nuget.org UI — otherwise the first release fails with a 403. Public repos are unaffected.

**Fallback (if Trusted Publishing is not yet enabled on the account — it is rolling out gradually):** store a per-package-scoped nuget.org API key as the environment secret `NUGET_API_KEY`, remove the `NuGet/login` step and the `id-token: write` permission, and push with `--api-key "${{ secrets.NUGET_API_KEY }}"`. Scope the key to the `Sentinel.CLI` package glob only — never an account-wide key.

Pinned action (verified against the GitHub API on 2026-05-30):

| Action | Tag | SHA |
|---|---|---|
| `NuGet/login` | v1.2.0 | `8d196754b4036150537f80ac539e15c2f1028841` |

---

## 4. The install story

### 4.1 End-user commands

```bash
# Install (latest stable) — the one-line wedge
dotnet tool install -g Sentinel.CLI

# Run
sentinel

# Update to the latest stable
dotnet tool update -g Sentinel.CLI

# Uninstall
dotnet tool uninstall -g Sentinel.CLI
```

Pre-release and pinned variants:

```bash
# Latest pre-release (e.g. an -alpha / -rc build)
dotnet tool install -g Sentinel.CLI --prerelease

# Pin an exact version (reproducible installs / CI)
dotnet tool install -g Sentinel.CLI --version 1.2.3
```

### 4.2 README install snippet (for the technical writer)

> The current `README.md` is stale — it says `net8.0` and "skeleton only". That is out of scope for this doc to rewrite, but the writer should refresh it. The install snippet to drop in:

````markdown
## Install

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download) (or, once the
OTLP receiver ships, the ASP.NET Core 10 Runtime).

```bash
dotnet tool install -g Sentinel.CLI
sentinel
```

To try a pre-release build, add `--prerelease`. To pin a version, add `--version <x.y.z>`.
Update with `dotnet tool update -g Sentinel.CLI`.
````

---

## 5. Gotchas

### 5.1 `github.ref_name` vs `head_ref`/`base_ref`

On a `pull_request` event, `github.ref_name` is `<PR_NUMBER>/merge`, **not** the branch name. `ci.yml` avoids it entirely (uses `github.head_ref` in the concurrency group). On `release.yml`'s **tag** trigger, `github.ref_name` **is** the tag (`v1.2.3`) — correct usage, and it is only used there for the run summary. The trap is event-specific, not a blanket ban.

### 5.2 CPM + `--locked-mode` — the first-run trap

`--locked-mode` restore requires committed `packages.lock.json` files. They do not exist yet, and `setup-dotnet`'s `cache-dependency-path: '**/packages.lock.json'` keys the NuGet cache off them too — one prerequisite, two consumers. Both CI and release `--locked-mode` restores will **fail on the first run** until lock files exist. One-time setup, in order:

1. Add to `Directory.Build.props`: `<RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>`.
2. Run `dotnet restore Sentinel.CLI.sln` once locally to generate `packages.lock.json` per project.
3. Commit the lock files (they must **not** be gitignored — the current `.gitignore` does not ignore them, good).
4. *Then* merge `ci.yml` / `release.yml`.

Regenerate and re-commit lock files whenever package versions change in CPM; otherwise locked-mode restore fails (by design — that is the determinism guarantee working).

### 5.3 SDK `rollForward` in CI

`global.json` pins `10.0.300` with `rollForward: latestFeature` and `allowPrerelease: false`. `setup-dotnet` with `global-json-file` honors this: the GitHub runner installs an SDK satisfying the policy rather than whatever ships preinstalled. CI and local builds therefore use the same SDK resolution — no "works on my machine" SDK drift.

### 5.4 Packaging a multi-project solution where only one project is the tool

Covered in §1.3: pack the **project** (`src/Sentinel.CLI/Sentinel.CLI.csproj`), not the solution, and set `IsPackable=false` on the four library projects. Without this, a release publishes stray library packages to nuget.org. This is a publish-once mistake that is painful to unwind (nuget.org packages cannot be deleted, only unlisted).

### 5.5 SourceLink + warnings-as-errors + no-git-repo

`TreatWarningsAsErrors=true` plus SourceLink in a folder with no git remote turns a SourceLink warning into a build failure. All determinism/SourceLink knobs are gated on `'$(GITHUB_ACTIONS)' == 'true'` (§1.2) so local builds stay green until the repo has a remote.

---

## 6. One-time prerequisites checklist

Before the first `release.yml` run:

- [ ] Initialize git, push to a GitHub remote; replace `OWNER` in csproj metadata.
- [ ] Add `<RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>`, restore, commit `packages.lock.json` files (§5.2).
- [ ] Add `MinVer` (CPM + host reference) and `<MinVerTagPrefix>v</MinVerTagPrefix>` (§3.2).
- [ ] Set `IsPackable=false` on Domain/Application/Infrastructure/Tui csprojs (§1.3).
- [ ] Add nuget.org listing metadata to the host csproj; ensure `assets/icon.png` exists **or** omit the `PackageIcon` line — missing file = NU5046 pack failure (§1.1).
- [ ] **Refresh `README.md`** (currently stale `net8.0`/"skeleton only") — it is the nuget.org listing front page (§1.1, §4.2).
- [ ] Configure the nuget.org Trusted Publishing policy (Owner/Repo/`release.yml`/`release`) and the `release` GitHub environment with `NUGET_USER` (§3.4).
- [ ] (Optional, release-quality) Add `Microsoft.SourceLink.GitHub` + CI-gated determinism props (§1.2).
- [ ] Tag `v0.x.0` and push to trigger the first release.
