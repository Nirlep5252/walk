# Windows installer and auto-update

This repo now uses Velopack for:

- a Windows installer
- GitHub Releases as the update feed
- in-app auto-update checks from `https://github.com/Nirlep5252/walk`

## One-time setup

1. Make sure the repo stays public or provide authenticated GitHub access for downloads.
2. Make sure GitHub Actions is enabled for the repository.
3. Configure Windows signing if you have it. Unsigned `.exe` assets are still likely to be blocked by SmartScreen and antivirus reputation checks, but this workflow keeps generating the standard installer and portable assets.

### Signing options

The release script now supports exactly one Windows signing mode:

- `VELOPACK_AZURE_TRUSTED_SIGN_FILE`
- `VELOPACK_SIGN_PARAMS`
- `VELOPACK_SIGN_TEMPLATE`

If none of those are configured, tagged releases still publish as normal, including `Setup.exe`.

#### Recommended: Azure Trusted Signing

Add these GitHub Actions secrets:

- `WINDOWS_AZURE_TRUSTED_SIGN_FILE_B64`: base64-encoded UTF-8 JSON metadata file for Trusted Signing
- `AZURE_CLIENT_ID`
- `AZURE_TENANT_ID`
- `AZURE_SUBSCRIPTION_ID`

The metadata JSON should contain:

```json
{
  "Endpoint": "<Trusted Signing account endpoint>",
  "CodeSigningAccountName": "<Trusted Signing account name>",
  "CertificateProfileName": "<Certificate profile name>"
}
```

Then the workflow will:

- write the metadata file to the runner temp directory
- sign in to Azure with `azure/login`
- pass `--azureTrustedSignFile` to Velopack

#### Alternative: existing signtool parameters

If you already sign with `signtool.exe`, add this GitHub Actions secret instead:

- `WINDOWS_SIGN_PARAMS`

Example value:

```text
/td sha256 /fd sha256 /tr http://timestamp.digicert.com /f C:\path\to\cert.pfx /p <password>
```

#### Alternative: custom signing tool

If you use a different signer such as `AzureSignTool.exe` or `jsign`, add this GitHub Actions secret instead:

- `WINDOWS_SIGN_TEMPLATE`

Example value:

```text
AzureSignTool.exe sign ... {{file}}
```

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
- signs the Velopack installer assets when signing is configured
- builds Velopack installer and update packages
- uploads the release assets to the matching GitHub release
- publishes that GitHub release

## Local release build

To build the installer locally without uploading:

```powershell
./scripts/Publish-WindowsRelease.ps1 -Version 0.2.0
```

To build locally with Azure Trusted Signing metadata:

```powershell
$env:VELOPACK_AZURE_TRUSTED_SIGN_FILE = "C:\path\to\trusted-signing-metadata.json"
./scripts/Publish-WindowsRelease.ps1 -Version 0.2.0 -RequireSigning
```

To build locally with existing `signtool.exe` arguments:

```powershell
$env:VELOPACK_SIGN_PARAMS = '/td sha256 /fd sha256 /tr http://timestamp.digicert.com /f C:\path\to\cert.pfx /p <password>'
./scripts/Publish-WindowsRelease.ps1 -Version 0.2.0 -RequireSigning
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
