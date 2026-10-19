# Changelog

All notable changes to OrbitConvert are documented here. The project follows [Semantic Versioning](https://semver.org/).

## [0.2.0] - 2026-10-19

### Added

- A redesigned Windows interface with Home, Convert, Batch, PDF Tools, Image Tools, Automation,
  Presets, History and Settings pages.
- The Orbit Menu, a keyboard-accessible radial conversion menu for File Explorer workflows.
- Image crop and background editors, image shrinking and metadata removal.
- Presets, watch-folder automation, batch controls and local conversion history.
- One-click updates from GitHub Releases. MSI downloads are verified against the release SHA-256
  manifest before Windows Installer starts.
- Real integration coverage for FFmpeg, Pandoc, LibreOffice, 7-Zip and Ghostscript.

### Changed

- Reworked the visual identity, icons, typography, light theme and dark theme.
- Improved archive extraction limits, logging, notifications and conversion feedback.
- Expanded documentation, screenshots, security notes and release validation.

### Security

- Downloads for optional tools and application updates require HTTPS and checksum verification.
- Archive extraction blocks traversal, absolute paths, alternate data streams, dangerous links and
  excessive expansion.

## [0.1.0] - 2026-10-18

### Added

- Initial WPF application, conversion engine, CLI, File Explorer integration, MSI installer and
  portable package.

[0.2.0]: https://github.com/AbdlrhmanDev/OrbitConvert/releases/tag/v0.2.0
[0.1.0]: https://github.com/AbdlrhmanDev/OrbitConvert/releases/tag/v0.1.0
