# ClaudeUsageTracker

## Release Process

**NEVER run `scripts/release.ps1` automatically.**

The word "release" alone is NOT an instruction to run the release script. Only run it when the user explicitly says something like:
> "Run the release script with version X.Y.Z"

The release script bumps versions, commits, tags, pushes to git, and creates a GitHub Release. Running it incorrectly cannot be undone easily.
