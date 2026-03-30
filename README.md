# Claude Usage Tracker

A lightweight Windows desktop app that monitors your AI quota usage across Claude Pro and MiniMaxi in real time.

![Platform](https://img.shields.io/badge/platform-Windows-blue)
![.NET](https://img.shields.io/badge/.NET-9.0-purple)
[![Release](https://img.shields.io/github/v/release/reckhou/ClaudeUsageTracker)](https://github.com/reckhou/ClaudeUsageTracker/releases/latest)

## Features

- **Claude Pro** — session (5-hour interval) and weekly quota usage via silent WebView authentication
- **MiniMaxi** — coding plan usage via Bearer token API
- **Auto-refresh** — configurable interval (1–60 min) with randomised jitter to avoid predictable polling; starts automatically on launch
- **Mini mode** — compact floating window showing all providers at a glance
- **Auto-update** — checks GitHub Releases in the background and self-updates via a PowerShell swap script
- **Persistent settings** — refresh interval survives restarts

## Download

Grab the latest release from the [Releases page](https://github.com/reckhou/ClaudeUsageTracker/releases/latest).

Extract the zip and run `ClaudeUsageTracker.Maui.exe` — no installer required.

> **Requirements:** Windows 10 1809 (build 17763) or later, x64.

## Setup

1. Launch the app — the **Setup** page opens on first run.
2. **Claude Plan** — click *Connect Claude Pro* to authenticate via the embedded browser.
3. **MiniMaxi** — paste your Bearer token and click *Connect*.
4. Click **Go to Dashboard** — the dashboard loads and auto-refresh starts immediately.

## Building from source

```bash
# Install MAUI workload
dotnet workload install maui-windows

# Build
dotnet build src/ClaudeUsageTracker.Maui/ClaudeUsageTracker.Maui.csproj -c Release

# Publish self-contained
dotnet publish src/ClaudeUsageTracker.Maui/ClaudeUsageTracker.Maui.csproj \
  -f net9.0-windows10.0.19041.0 -c Release \
  -p:WindowsPackageType=None -p:SelfContained=true -r win-x64 \
  --output publish
```

## Releasing

Bump `ApplicationDisplayVersion` in `ClaudeUsageTracker.Maui.csproj`, commit, then tag:

```bash
git tag v1.x.x
git push origin v1.x.x
```

GitHub Actions builds the release artifact and publishes it automatically.

## Architecture

| Project | Role |
|---|---|
| `ClaudeUsageTracker.Core` | Provider interfaces, models, update service, SQLite data service |
| `ClaudeUsageTracker.Maui` | .NET MAUI Windows UI — views, viewmodels, platform services |

Provider data is fetched via `IUsageProvider` implementations — adding a new provider means implementing that interface and registering it in `MauiProgram.cs`.
