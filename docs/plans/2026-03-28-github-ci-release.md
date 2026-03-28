# GitHub CI + Release Automation Plan

**Goal:** Automated CI for Core/Web on every push, and a one-command local script to build MAUI and publish a GitHub Release.
**Architecture:** Two GitHub Actions workflows handle CI (push/PR validation for Core + Blazor WASM). A PowerShell release script handles MAUI: builds locally, bumps version in the `.csproj`, commits + tags, then uses the `gh` CLI to create a GitHub Release and upload the Windows artifact.
**Tech Stack:** GitHub Actions, .NET 9, .NET MAUI Windows, Blazor WebAssembly, PowerShell, `gh` CLI

---

## Progress

- [x] Task 1: GitHub Actions CI workflow (Core + Blazor WASM)
- [x] Task 2: PowerShell release script (MAUI build + GitHub Release)

---

## Files

- Create: `.github/workflows/ci.yml` — builds Core + Blazor WASM on push/PR to main
- Create: `scripts/release.ps1` — local MAUI publish + version bump + `gh release create`

---

### Task 1: GitHub Actions CI workflow (Core + Blazor WASM)

**Files:** `.github/workflows/ci.yml`

Trigger: `push` and `pull_request` on `main`.

Jobs:
- Single job on `ubuntu-latest`
- Steps:
  1. `actions/checkout@v4`
  2. `actions/setup-dotnet@v4` with `dotnet-version: '9.0.x'`
  3. `dotnet restore ClaudeUsageTracker.sln`
  4. `dotnet build ClaudeUsageTracker.sln --no-restore -c Release` — builds Core + Web (skip MAUI on Linux by MSBuild platform condition)
  5. `dotnet test` if a test project exists (skip gracefully if not)

The MAUI `.csproj` already has a platform guard:
```xml
<TargetFrameworks Condition="$([MSBuild]::IsOSPlatform('windows'))">net9.0-windows10.0.19041.0</TargetFrameworks>
<TargetFrameworks Condition="!$([MSBuild]::IsOSPlatform('windows'))">net9.0-android;net9.0-ios;net9.0-maccatalyst</TargetFrameworks>
```
On Linux runners, MAUI targets Android/iOS/MacCatalyst — but we don't want to build those either. The cleanest fix: exclude the MAUI project from the solution build on CI using a `--project` flag targeting only Core + Web, or add a `Directory.Build.props` condition to skip MAUI entirely on non-Windows.

**Approach:** Build Core and Web projects individually (not the full solution) to avoid MAUI entirely:
```yaml
- run: dotnet build src/ClaudeUsageTracker.Core/ClaudeUsageTracker.Core.csproj -c Release
- run: dotnet build src/ClaudeUsageTracker.Web/ClaudeUsageTracker.Web.csproj -c Release
```

**Verify:** Push a commit to main. GitHub Actions tab shows a passing green check on the CI workflow.

---

### Task 2: PowerShell release script (MAUI build + GitHub Release)

**Files:** `scripts/release.ps1`

Usage: `./scripts/release.ps1 -Version 1.2.3`

Steps the script performs:
1. **Validate** `gh` CLI is installed (`gh --version`)
2. **Validate** git working tree is clean (no uncommitted changes)
3. **Bump version** in `src/ClaudeUsageTracker.Maui/ClaudeUsageTracker.Maui.csproj`:
   - Set `<ApplicationDisplayVersion>` to the provided version (e.g. `1.2.3`)
   - Set `<ApplicationVersion>` to the build number (patch digit, e.g. `3`)
4. **Build + publish** MAUI Windows:
   ```
   dotnet publish src/ClaudeUsageTracker.Maui/ClaudeUsageTracker.Maui.csproj
     -f net9.0-windows10.0.19041.0
     -c Release
     -p:WindowsPackageType=None
     -p:SelfContained=true
     -r win-x64
     --output publish/
   ```
5. **Zip the output** to `publish/ClaudeUsageTracker-v{Version}-win-x64.zip`
6. **Commit version bump**: `git add` the `.csproj`, commit with message `chore: release v{Version}`
7. **Tag**: `git tag v{Version}`
8. **Push**: `git push && git push origin v{Version}`
9. **Create GitHub Release**:
   ```
   gh release create v{Version}
     --title "v{Version}"
     --notes "Release v{Version}"
     publish/ClaudeUsageTracker-v{Version}-win-x64.zip
   ```
10. Print release URL.

Script should use `-ErrorAction Stop` and exit with a clear message on any failure.

**Verify:** Run `./scripts/release.ps1 -Version 1.0.0`. Check GitHub Releases page — a new release `v1.0.0` should appear with the zip artifact attached.

---
