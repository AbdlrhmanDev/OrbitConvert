<p align="center">
  <img src="assets/icons/orbitconvert.svg" width="112" alt="OrbitConvert logo">
</p>

<h1 align="center">OrbitConvert</h1>

<p align="center">
  Free, private, open-source file conversion for Windows — right from File Explorer.
</p>

<p align="center">
  <img alt="Windows 10 and 11" src="https://img.shields.io/badge/Windows-10%20%7C%2011-0078D4?logo=windows">
  <a href="https://github.com/AbdlrhmanDev/OrbitConvert/actions/workflows/build.yml"><img alt="Build" src="https://github.com/AbdlrhmanDev/OrbitConvert/actions/workflows/build.yml/badge.svg"></a>
  <a href="https://github.com/AbdlrhmanDev/OrbitConvert/actions/workflows/test.yml"><img alt="Tests" src="https://github.com/AbdlrhmanDev/OrbitConvert/actions/workflows/test.yml/badge.svg"></a>
  <a href="LICENSE"><img alt="License: MIT" src="https://img.shields.io/badge/license-MIT-green"></a>
  <a href="https://github.com/AbdlrhmanDev/OrbitConvert/releases"><img alt="Latest release" src="https://img.shields.io/github/v/release/AbdlrhmanDev/OrbitConvert?include_prereleases&sort=semver"></a>
</p>

OrbitConvert converts images, video, audio, documents, PDFs and archives **entirely on your computer**.
Pick files in File Explorer, choose a format from a floating radial menu, and the converted file appears
next to the original. A full desktop app handles batches, options, PDF tools and history.

No account. No uploads. No telemetry.

<p align="center">
  <img src="assets/screenshots/radial-menu.png" width="360" alt="Radial menu offering formats for a video file">
  <img src="assets/screenshots/radial-success.png" width="360" alt="Radial menu after a successful conversion">
</p>

## Features

- **Radial menu** near the cursor that shows only the formats your files can actually become, with
  live progress and a success/failure state. Keyboard: arrows/Tab to move, Enter to convert, 1–9 to
  pick, Esc to close.
- **Four ways to open it**
  - **File Explorer context menu**: “Convert with OrbitConvert” (and a **Send to** entry).
  - **Global shortcut** (default <kbd>Ctrl</kbd>+<kbd>Alt</kbd>+<kbd>O</kbd>) for the files selected
    in the active File Explorer window.
  - **Floating drop zone**: drag files onto a small always-on-top circle.
  - **Shake while dragging** *(experimental, off by default)*: shows a temporary drop target.
- **Real conversions** through proven engines: ImageMagick (bundled), FFmpeg, Pandoc, LibreOffice and
  7-Zip. Nothing is "converted" by renaming extensions. Every output is checked before it is saved.
- **Content-based detection**: files are identified by their bytes, not their extension, and a
  mismatched extension is flagged.
- **Batch processing** with bounded parallelism, per-file or shared options, pause/resume for waiting
  jobs, cancel, retry and reorder (for PDF merging).
- **PDF tools**: merge, split, extract pages, rotate, compress, images → PDF, PDF → images, metadata.
- **Archive tools**: create ZIP/7z, safe extraction (path-traversal and zip-bomb protection), integrity
  checks.
- **Safe by default**: originals are never overwritten; outputs appear atomically; failed or canceled
  conversions leave nothing behind; child processes are killed with their job object.
- **History**, light/dark themes that follow Windows, notifications, and a command-line tool.

## Screenshots

| Home | Convert |
|---|---|
| ![Home screen](assets/screenshots/home.png) | ![Convert screen with images and options](assets/screenshots/convert.png) |
| **PDF & archive tools** | **Settings** |
| ![Tools screen](assets/screenshots/tools.png) | ![Settings screen](assets/screenshots/settings.png) |

All screenshots are of the real application (dark theme, Windows 11).

## Supported conversions

What appears depends on what is installed. Unavailable targets are shown disabled with the reason.

| Category | From | To | Engine |
|---|---|---|---|
| Images | JPG, PNG, WebP, BMP, TIFF, GIF, HEIC/HEIF, AVIF, ICO | JPG, PNG, WebP, BMP, TIFF, GIF, AVIF, ICO, PDF | ImageMagick (bundled) |
| Video | MP4, MKV, MOV, AVI, WebM, GIF | MP4, MKV, MOV, AVI, WebM, GIF; “Compress” (same format, smaller) | FFmpeg |
| Audio | MP3, WAV, FLAC, AAC, OGG, M4A, Opus — and the audio track of any video | MP3, WAV, FLAC, AAC, OGG, M4A, Opus | FFmpeg |
| Documents | Markdown, HTML, DOCX, ODT, EPUB, TXT | DOCX, HTML, Markdown, ODT, EPUB, TXT | Pandoc |
| Documents | DOCX, DOC, ODT, RTF, TXT, HTML | PDF; DOCX ↔ ODT; DOC/RTF → DOCX, ODT, TXT | LibreOffice |
| Documents | Markdown, EPUB | PDF | Pandoc + LibreOffice |
| PDF | PDF | DOCX — **experimental**, see limitations | LibreOffice |
| PDF | PDF | Merge, split, extract pages, rotate, compress, PNG/JPG/WebP pages | PDFsharp + PDFium (bundled) |
| PDF | Images | One PDF (a page per image) | Bundled |
| Archives | Any files | ZIP (bundled), 7z (7-Zip) | |
| Archives | ZIP, 7z | Extract to a folder, verify integrity | Bundled |

**Options**: image quality, resize (never upscales), crop, rotate, metadata removal, sRGB conversion,
transparency background; video resolution presets, quality, codec (H.264, H.265, VP9, AV1), frame rate,
audio removal, GIF frame rate/width; audio bitrate, sample rate, channels; PDF page ranges, rotation,
render DPI, compression level; archive name and compression level.

**Not supported** (and not pretended): HEIC *output* (the bundled ImageMagick cannot write it), OCR,
editable reconstruction of scanned PDFs, password-protected PDFs and archives, DRM-protected media.

## Install

> Releases are **not code-signed**. Windows SmartScreen may warn that the publisher is unknown. Verify
> the download against `SHA256SUMS.txt` from the same release before running it.

1. Download from [Releases](https://github.com/AbdlrhmanDev/OrbitConvert/releases):
   - `OrbitConvert-<version>-win-x64.msi` — installs for the current user, **no admin rights needed**,
     into `%LOCALAPPDATA%\Programs\OrbitConvert`. Optional features: Explorer integration (on),
     start at sign-in (on), desktop shortcut (off).
   - or `OrbitConvert-<version>-win-x64-portable.zip` — unzip anywhere; settings stay in `.\data`.
2. Verify: `Get-FileHash .\OrbitConvert-*.msi -Algorithm SHA256` must match `SHA256SUMS.txt`.
3. Open **Settings → Conversion tools** to add FFmpeg, Pandoc or 7-Zip (one click, checksum-verified),
   or point OrbitConvert to copies you already have. Install LibreOffice and Ghostscript from their
   websites if you need office or lossy PDF conversions.

The builds are self-contained: you do **not** need to install .NET.

Uninstall from **Settings → Apps** (or `msiexec /x OrbitConvert-<version>-win-x64.msi`). This removes the
program, shortcuts and Explorer integration. Your settings, history and downloaded tools in
`%LOCALAPPDATA%\OrbitConvert` are kept; delete that folder to remove them too.

### System requirements

- Windows 10 or Windows 11, 64-bit (x64), on a version still supported by .NET 10
- About 300 MB of disk space, plus space for optional tools (FFmpeg ≈ 300 MB unpacked)

## Use it

- **From File Explorer**: right-click files → *Convert with OrbitConvert* (Windows 11: *Show more
  options* first), or *Send to → OrbitConvert*, or select files and press <kbd>Ctrl</kbd>+<kbd>Alt</kbd>+<kbd>O</kbd>.
- **From the app**: drop files on the window, pick a target, adjust options, press *Start conversion*.
- **Batch**: add many files; targets shown are the ones all files share. Files already in the target
  format are skipped.

### Command line

The installer also includes `orbitconvert-cli.exe`, which uses the same engine:

```powershell
orbitconvert-cli detect photo.heic                       # identify by content
orbitconvert-cli targets clip.mov                        # list available conversions
orbitconvert-cli convert photo.heic --to jpg --quality 85 --strip-metadata
orbitconvert-cli convert clip.mov --to mp4 --resolution 720
orbitconvert-cli convert a.pdf b.pdf --to pdf-merge
orbitconvert-cli convert report.pdf --to pdf-extract --pages "1-3, 7"
orbitconvert-cli convert *.wav --to opus --bitrate 128 --jobs 4 --out D:\Converted
orbitconvert-cli pdf-info report.pdf
orbitconvert-cli test-archive backup.zip
orbitconvert-cli tools                                   # which external tools were found
orbitconvert-cli install-tool ffmpeg                     # asks for confirmation first
```

Run `orbitconvert-cli --help` for every option. Exit codes: 0 success, 1 a conversion failed, 2 usage error.

## Build from source

Requirements: Windows 10/11 x64 and the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```powershell
git clone https://github.com/AbdlrhmanDev/OrbitConvert.git
cd OrbitConvert
dotnet build OrbitConvert.sln -c Release
dotnet test OrbitConvert.sln -c Release
dotnet run --project src/OrbitConvert.App          # start the desktop app
pwsh scripts/build-release.ps1                     # MSI + portable ZIP + checksums in artifacts\release
```

Integration tests that need FFmpeg, Pandoc, LibreOffice or 7-Zip are **skipped** (not failed) when the
tool is missing. Point `ORBITCONVERT_TOOLS_DIR` at a folder containing the tools to run them. See
[docs/DEVELOPMENT.md](docs/DEVELOPMENT.md).

## What has been verified

Verified by the maintainers on Windows 11 (build 26300, x64, 150 % scaling, single monitor):

- The solution builds with zero warnings; 204 automated tests pass (88 core, 89 converter, 27 integration
  with real FFmpeg 9.0.2, Pandoc 3.12.1, LibreOffice 26.8.1 and 7-Zip 26.04).
- The app launches; the radial menu opens from the command line and from the Explorer shell verb, and
  conversions started from it and from the Convert page produce validated files (driven through UI
  Automation).
- The MSI installs without admin rights, registers the context menu, Send To, Start menu and sign-in
  entries, and uninstalls cleanly.

Not yet verified on real hardware: Windows 10, multi-monitor setups with mixed DPI, a physical drag
from Explorer onto the drop zone, the global shortcut against a live Explorer selection, and the
experimental shake gesture. A checklist for these is in [docs/SHELL_INTEGRATION.md](docs/SHELL_INTEGRATION.md#manual-validation-checklist).
Reports are welcome.

## Troubleshooting

| Problem | Fix |
|---|---|
| A format is greyed out | Hover it for the reason. Usually a tool is missing: **Settings → Conversion tools**. |
| No context-menu entry on Windows 11 | It is under **Show more options** (<kbd>Shift</kbd>+<kbd>F10</kbd>). The compact menu needs a signed MSIX package, which this project does not ship yet. |
| Shortcut does nothing | Another app may own it — Settings shows a message. Choose a different key. It only reads selections from File Explorer windows, not the desktop. |
| SmartScreen warning | The binaries are unsigned. Verify the checksum, then choose *More info → Run anyway*. |
| “The file is in use” | Close the program that has the file open, then retry from the queue or History. |
| LibreOffice conversions fail | Install LibreOffice from libreoffice.org; OrbitConvert uses its own private profile, so an open LibreOffice window is fine. |
| PDF compression “could not make it smaller” | The PDF is already compact. Balanced/Strong modes need Ghostscript. |
| Need diagnostics | Logs: `%LOCALAPPDATA%\OrbitConvert\logs` (local only; never sent anywhere). |

## Privacy

OrbitConvert works offline. It never uploads files, has no accounts, analytics or telemetry, and makes
network requests only when **you** click *Download* for a tool or *Check now* / enable update checks
(which ask GitHub for the latest release number). History and logs are stored locally and can be
turned off or deleted in Settings. See [docs/SECURITY.md](docs/SECURITY.md) for details.

## Security

Please report vulnerabilities privately — see [SECURITY.md](SECURITY.md). Do not open public issues for
security problems.

## Contributing

Contributions are welcome: bug reports, translations, new converters and tests. Read
[CONTRIBUTING.md](CONTRIBUTING.md) and the [Code of Conduct](CODE_OF_CONDUCT.md). Architecture overview:
[docs/ARCHITECTURE.md](docs/ARCHITECTURE.md); adding a converter: [docs/CONVERTERS.md](docs/CONVERTERS.md).

## Roadmap

Highlights from [docs/ROADMAP.md](docs/ROADMAP.md): signed releases and an MSIX package with a
Windows 11 top-level context-menu entry, translations, a live preview for image options, more
document routes, and ARM64 builds.

## License

OrbitConvert's source code is under the [MIT License](LICENSE). Binary releases bundle third-party
components under their own licenses (including LGPL and MPL libraries inside ImageMagick's native
DLL); optional tools such as FFmpeg (GPL build), Pandoc (GPL) and Ghostscript (AGPL) are not bundled.
See [NOTICE.md](NOTICE.md) for the full list, versions and obligations.

OrbitConvert is an independent project inspired by the interaction style of radial file converters on
other platforms. It does not contain code, artwork or branding from any of them.
