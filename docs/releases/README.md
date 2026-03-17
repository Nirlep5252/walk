# Release changelogs

Create one markdown file per shipped version using the file name pattern `<version>.md`.

Example:

- `docs/releases/0.4.0.md`

Every Windows release now requires this file. `scripts/Publish-WindowsRelease.ps1` and the GitHub release workflow both fail if the matching changelog file is missing or empty.
