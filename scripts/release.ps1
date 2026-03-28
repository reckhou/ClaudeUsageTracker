#Requires -Version 5.1
<#
.SYNOPSIS
    Build MAUI Windows release and publish to GitHub Releases.

.PARAMETER Version
    Semantic version string (e.g. 1.2.3). Required.

.PARAMETER Notes
    Optional release notes. Defaults to "Release v{Version}".

.EXAMPLE
    .\scripts\release.ps1 -Version 1.0.0
    .\scripts\release.ps1 -Version 1.2.3 -Notes "Fixed dashboard crash"
#>

param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version,

    [string]$Notes = ""
)

$ErrorActionPreference = "Stop"

$RepoRoot   = Split-Path $PSScriptRoot -Parent
$CsprojPath = Join-Path $RepoRoot "src\ClaudeUsageTracker.Maui\ClaudeUsageTracker.Maui.csproj"
$PublishDir = Join-Path $RepoRoot "publish"
$ZipName    = "ClaudeUsageTracker-v$Version-win-x64.zip"
$ZipPath    = Join-Path $PublishDir $ZipName

if (-not $Notes) { $Notes = "Release v$Version" }

# 1. Check prerequisites
Write-Host "Checking prerequisites..." -ForegroundColor Cyan

if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
    Write-Error "GitHub CLI (gh) is not installed. Install from https://cli.github.com"
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Error "dotnet CLI is not found in PATH."
}

# 2. Verify clean working tree
Write-Host "Checking git status..." -ForegroundColor Cyan

Push-Location $RepoRoot
$gitStatus = git status --porcelain
if ($gitStatus) {
    Write-Error "Working tree is not clean. Commit or stash changes before releasing:`n$gitStatus"
}

# 3. Bump version in .csproj
Write-Host "Bumping version to $Version..." -ForegroundColor Cyan

$patch = ($Version -split '\.')[2]
$xml   = [xml](Get-Content $CsprojPath -Raw)

$displayVersionNode = $xml.SelectSingleNode("//ApplicationDisplayVersion")
$versionNode        = $xml.SelectSingleNode("//ApplicationVersion")

if (-not $displayVersionNode -or -not $versionNode) {
    Write-Error "Could not find <ApplicationDisplayVersion> or <ApplicationVersion> in $CsprojPath"
}

$displayVersionNode.InnerText = $Version
$versionNode.InnerText        = $patch

$xml.Save($CsprojPath)
Write-Host "  ApplicationDisplayVersion = $Version" -ForegroundColor Gray
Write-Host "  ApplicationVersion        = $patch" -ForegroundColor Gray

# 4. Build + publish MAUI Windows
Write-Host "Publishing MAUI Windows (self-contained, win-x64)..." -ForegroundColor Cyan

if (Test-Path $PublishDir) { Remove-Item $PublishDir -Recurse -Force }
New-Item -ItemType Directory -Path $PublishDir | Out-Null

dotnet publish $CsprojPath `
    -f net9.0-windows10.0.19041.0 `
    -c Release `
    -p:WindowsPackageType=None `
    -p:SelfContained=true `
    -r win-x64 `
    --output $PublishDir

if ($LASTEXITCODE -ne 0) {
    Write-Error "dotnet publish failed with exit code $LASTEXITCODE"
}

# 5. Zip the output
Write-Host "Zipping to $ZipName..." -ForegroundColor Cyan

Compress-Archive -Path "$PublishDir\*" -DestinationPath $ZipPath -Force

# 6. Commit version bump
Write-Host "Committing version bump..." -ForegroundColor Cyan

git add $CsprojPath
git commit -m "chore: release v$Version"

if ($LASTEXITCODE -ne 0) {
    Write-Error "git commit failed"
}

# 7. Tag
Write-Host "Tagging v$Version..." -ForegroundColor Cyan

git tag "v$Version"

if ($LASTEXITCODE -ne 0) {
    Write-Error "git tag failed -- tag may already exist"
}

# 8. Push commit + tag
Write-Host "Pushing to remote..." -ForegroundColor Cyan

git push
git push origin "v$Version"

if ($LASTEXITCODE -ne 0) {
    Write-Error "git push failed"
}

# 9. Create GitHub Release
Write-Host "Creating GitHub Release v$Version..." -ForegroundColor Cyan

$releaseUrl = gh release create "v$Version" --title "v$Version" --notes $Notes $ZipPath

if ($LASTEXITCODE -ne 0) {
    Write-Error "gh release create failed"
}

Pop-Location

Write-Host ""
Write-Host "Release published: $releaseUrl" -ForegroundColor Green
Write-Host "Artifact: $ZipName" -ForegroundColor Green
