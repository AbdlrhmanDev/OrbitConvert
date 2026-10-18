# Roadmap

Items are listed by priority. Nothing here is implemented yet unless marked done.

## Next

- **Code signing** for the MSI, executables and checksums, once a certificate is available.
- **MSIX / sparse package with an `IExplorerCommand` handler**, so “Convert with OrbitConvert” appears in
  the Windows 11 compact context menu. Requires signing.
- **Manual validation** of the shell checklist on Windows 10 and on mixed-DPI multi-monitor setups,
  with results recorded in release notes.
- **Translations**: move UI strings to resource files and accept community translations (the language
  selector currently offers English only).

## Later

- Image option preview (before/after, estimated file size).
- Trim start/end for video and audio; subtitle stream handling for MKV.
- Per-file target selection in mixed batches (today a batch shares one target).
- HEIC output when a bundled encoder becomes available.
- Optional OCR for scanned PDFs (would be a separate, clearly labelled engine).
- ARM64 builds.
- winget manifest.

## Done in 0.1.0

- Conversion engine with capability registry, content detection, validation and atomic output.
- Image (bundled), video/audio (FFmpeg), documents (Pandoc, LibreOffice), PDF tools, archives.
- Radial menu with four activation modes (drop zone, shortcut, context menu/Send To, experimental shake).
- Desktop app: home, convert/batch queue, tools, history, settings; light/dark themes.
- Command-line tool, per-user MSI, portable ZIP, CI workflows.
