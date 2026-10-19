# Security and privacy design

This document describes how OrbitConvert protects users and their files. To report a vulnerability,
see the [security policy](../SECURITY.md).

## Privacy

- Conversions run locally. File contents never leave the computer.
- No account, analytics, crash reporting or telemetry of any kind.
- Network access happens only when the user:
  - clicks **Download** for a tool (HTTPS to the publisher's release URL; nothing is sent except the
    request itself), or
  - clicks **Check now** or enables **Check GitHub for new versions** (a request to the GitHub releases
    API and its checksum asset, at most once a day), or
  - explicitly clicks **Download and install** for an available update.
- History is optional and stored in `%LOCALAPPDATA%\OrbitConvert\history.json`. Turning it off stops
  recording; Settings and the History page can clear it.
- Logs stay in `%LOCALAPPDATA%\OrbitConvert\logs`, are deleted after 14 days, and contain file paths
  but never file contents.

## Untrusted input

Files being converted are treated as untrusted.

- **Detection by content.** Formats are identified from file signatures; the extension is only a hint.
- **ImageMagick coders.** OrbitConvert tells ImageMagick which coder to use, based on the verified
  signature. Content cannot switch ImageMagick to script-capable coders (MVG, MSL, SVG, TEXT, URL).
  Image width, height and frame count are capped.
- **Pandoc sandbox.** Pandoc runs with `--sandbox`, so a Markdown or HTML file cannot read other local
  files (for example `![x](C:/Users/…/secret.png)` or include directives) into the output. Users can
  opt out per conversion.
- **LibreOffice** runs headless with a private profile. LibreOffice's default macro security applies;
  OrbitConvert does not enable macros.
- **Ghostscript** runs with `-dSAFER`.
- **No content is executed.** OrbitConvert never runs scripts or programs found in files.

## Process execution

- Every tool is started with an argument **list** (`ProcessStartInfo.ArgumentList`); there is no shell
  and no command-line string building. File names containing quotes, `&`, `%` or leading dashes are
  passed literally (covered by `ProcessRunnerTests`).
- FFmpeg paths use the `file:` protocol prefix so names such as `-y.mp4` or `concat:…` are never parsed
  as options or protocols. Pandoc receives `--` before the input; 7-Zip receives `--` before file names.
- Tools are located in fixed places only (configured path, OrbitConvert's tools folder, the `PATH`,
  known install folders). The current directory is never searched.
- Each child process runs in a Job Object that kills the whole process tree on cancel, timeout or if
  OrbitConvert exits.

## Archive extraction

`SafeArchiveExtractor` (ZIP via `System.IO.Compression`, 7z via SharpCompress) rejects the whole archive
before writing anything unsafe:

- absolute paths, UNC paths, drive letters and `:` (alternate data streams);
- `..` segments or any path that resolves outside the destination;
- Windows device names (`CON`, `NUL`, `COM1`, …) and names ending in a dot or space;
- symbolic-link entries are skipped;
- limits on entry count, total size (64 GiB) and compression ratio (2000:1 once past 1 GiB), counted on
  the bytes actually written rather than the archive's claims;
- every entry's CRC-32 is verified.

Extraction happens in a private work directory and is only moved next to the archive after it fully
succeeds.

## Output safety

- Outputs are written in a private work directory and moved into place only after validation, so a
  half-written file never appears under the final name.
- An existing file is never overwritten unless the user chose *Replace*, and **input files are never
  overwritten** under any setting.
- Outputs are validated: they must exist, be non-empty, and have the signature of the requested format.

## Downloads

- Tool versions, URLs and SHA-256 digests are **pinned** in `ToolPackages` (Infrastructure). A download
  whose digest differs is deleted and reported; nothing is installed.
- Downloads go to `%LOCALAPPDATA%\OrbitConvert\tools`, never to system folders, and archives are
  extracted with the same safe extractor.
- Application updates are downloaded only after explicit confirmation. OrbitConvert selects the MSI
  asset for the reported version, downloads it over HTTPS, verifies it against `SHA256SUMS.txt` from
  the same GitHub release, deletes any mismatch, and only then starts Windows Installer.

## Local inter-process communication

The single-instance pipe is created with `PipeOptions.CurrentUserOnly`, so other users on the same
computer cannot connect. Messages are size-limited JSON containing a command name and file paths.

## Known limitations

- Releases are not code-signed (see the README).
- OrbitConvert relies on the security of the external tools it runs. Keep FFmpeg, Pandoc and LibreOffice
  up to date; the Settings page shows where each tool was found.
