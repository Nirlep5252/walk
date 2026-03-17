# Windows installer and auto-update

This repo now uses Velopack for:

- a Windows installer
- GitHub Releases as the update feed
- in-app auto-update checks from `https://github.com/Nirlep5252/walk`

## One-time setup

1. Make sure the repo stays public or provide authenticated GitHub access for downloads.
2. Make sure GitHub Actions is enabled for the repository.
3. If you want code signing later, add signing arguments to `scripts/Publish-WindowsRelease.ps1`.

## Normal release flow

1. Pick the version you want to ship, for example `0.2.0`.
2. Create `docs/releases/0.2.0.md` with the markdown changelog for that release. This file is now mandatory and the release workflow fails without it.
3. Create and push a tag named `v0.2.0`.

```powershell
git tag v0.2.0
git push origin v0.2.0
```

That tag triggers [`.github/workflows/release.yml`](../.github/workflows/release.yml), which:

- runs the test suite
- publishes the app for `win-x64`
- builds Velopack installer and update packages
- uploads the release assets to the matching GitHub release
- publishes that GitHub release

## Local release build

To build the installer locally without uploading:

```powershell
./scripts/Publish-WindowsRelease.ps1 -Version 0.2.0
```

To build and upload from your machine:

```powershell
$env:GITHUB_TOKEN = "your-token"
./scripts/Publish-WindowsRelease.ps1 -Version 0.2.0 -UploadToGitHub -PublishRelease
```

Local release artifacts are written to:

- `artifacts/publish/win-x64`
- `artifacts/Releases`

## Versioning notes

- The app displays its version in the launcher footer and tray menu.
- The updater also displays the markdown from `docs/releases/<version>.md` after each installed update.
- GitHub tags should be `vX.Y.Z`.
- The workflow strips the leading `v` and publishes the app as `X.Y.Z`.
