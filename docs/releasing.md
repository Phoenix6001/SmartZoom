# Releasing

A release is a git tag. Pushing `vX.Y.Z` runs `.github/workflows/release.yml`, which builds exactly what
CI builds on every push, then opens a **draft** GitHub Release with the installer, the framework-dependent
executable, a checksum file, and the CHANGELOG section for that version as its notes. Nothing is public
until a person reads the draft and clicks **Publish release**.

## Before the first release

- The repository must be on GitHub with `main` pushed. The workflow needs nothing else: it publishes with
  the workflow's own token, so there is no secret to configure.
- Decide about code signing. Every asset the workflow produces is unsigned, so Windows SmartScreen warns
  about an unknown publisher on first run. That is acceptable for an early open-source release and the
  README says so; a certificate (or Azure Trusted Signing) can be added to the workflow as one step later.

## Cutting a release

1. **Pick the version.** Versions follow [semantic versioning](https://semver.org/spec/v2.0.0.html); before
   1.0 a breaking change to the settings file is a minor bump.
2. **Bump `Directory.Build.props`.** Change `<Version>` to the new number. This is the only place the
   version is written; the executable, the installer and the release assets all read it from there.
3. **Move the CHANGELOG entries.** In `CHANGELOG.md`, rename `## [Unreleased]` to
   `## [X.Y.Z] - YYYY-MM-DD` and add a fresh, empty `## [Unreleased]` heading above it. The workflow
   refuses to release a version that has no CHANGELOG section, or an empty one.
4. **Commit and tag.**

   ```powershell
   git add Directory.Build.props CHANGELOG.md
   git commit -m "Release X.Y.Z"
   git tag vX.Y.Z
   git push origin main vX.Y.Z
   ```

5. **Read the draft.** The workflow takes about ten minutes (the self-contained publish and the Inno Setup
   compile are most of it). Open the repository's Releases page, check the notes and the three assets, and
   press **Publish release**. If anything is wrong, delete the draft, fix, move the tag
   (`git tag -f vX.Y.Z && git push -f origin vX.Y.Z`) and it runs again.

## What the workflow checks

- The tag equals `<Version>` in `Directory.Build.props`; `v0.2.0` with `0.1.0` inside fails.
- Formatting, build and the full test suite, the same gates as CI.
- `CHANGELOG.md` has a non-empty `## [X.Y.Z]` section.

## The assets

| File | What it is |
|---|---|
| `SmartZoom-X.Y.Z-setup.exe` | The installer: per-user, no administrator rights, carries its own .NET runtime. What almost everyone should download. |
| `SmartZoom-X.Y.Z-win-x64-framework-dependent.exe` | A single executable with no installer. Needs the .NET 10 Desktop Runtime already on the machine. For people who do not want anything installed. |
| `SHA256SUMS.txt` | Checksums of the two files above, for verifying a download: `Get-FileHash .\SmartZoom-X.Y.Z-setup.exe` must match its line. |

## Building the same assets locally

`pwsh install\build.ps1` produces the installer in `install\output\`, and
`dotnet publish src/SmartZoom.App -c Release -r win-x64 --no-self-contained -o publish` the single
executable. The workflow runs those two commands and nothing else, so a locally built asset and a released
one differ only in the machine that compiled them.
