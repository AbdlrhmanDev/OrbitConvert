# Security policy

## Supported versions

Only the latest release receives security fixes while the project is in the 0.x series.

## Reporting a vulnerability

Please **do not** open a public issue for security problems.

Report privately through GitHub's
[private vulnerability reporting](https://github.com/AbdlrhmanDev/OrbitConvert/security/advisories/new)
(Security tab → *Report a vulnerability*). Include:

- the OrbitConvert version and Windows version;
- what an attacker can achieve and under which conditions (for example, “a crafted ZIP writes outside
  the destination folder”);
- steps or a minimal sample file to reproduce it.

You should receive an acknowledgement within 7 days. Fixes are released as soon as practical, and
reporters are credited in the release notes unless they prefer otherwise.

## Scope

In scope: OrbitConvert's own code, including how it starts external tools, extracts archives, downloads
tools, handles file names, and integrates with Windows.

Out of scope: vulnerabilities inside FFmpeg, Pandoc, LibreOffice, 7-Zip, Ghostscript or ImageMagick
themselves — please report those upstream. If OrbitConvert makes such a bug easier to reach (for
example by failing to sandbox a tool), that part is in scope.

The design measures are described in [docs/SECURITY.md](docs/SECURITY.md).
