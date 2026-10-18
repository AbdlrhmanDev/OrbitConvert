# Third-party notices and licensing

OrbitConvert's own source code is licensed under the [MIT License](LICENSE).

**The binary distributions (installer and portable ZIP) are not entirely MIT-licensed.** They bundle
third-party components that keep their own licenses, listed below. Full license and notice texts ship
with every build in the `licenses\` folder next to `OrbitConvert.exe`.

## Components bundled in the installer and portable ZIP

| Component | Version | License | Notes |
|---|---|---|---|
| .NET runtime and WPF / Windows Forms | 10.0 | MIT | Self-contained runtime. Notices: `licenses\dotnet-runtime-THIRD-PARTY-NOTICES.txt` |
| [Magick.NET](https://github.com/dlemstra/Magick.NET) (`Magick.NET-Q8-AnyCPU`) | 14.17.2 | Apache-2.0 | .NET wrapper for ImageMagick |
| [ImageMagick](https://imagemagick.org/) (inside `Magick.Native-Q8-x64.dll`) | 7.1.2-32 | ImageMagick License | Attribution required; see below |
| ImageMagick delegate libraries (inside `Magick.Native-Q8-x64.dll`) | various | Mixed — see below | Includes LGPL and MPL components |
| [PDFsharp](https://github.com/empira/PDFsharp) | 6.2.4 | MIT | PDF page operations |
| [PDFtoImage](https://github.com/sungaila/PDFtoImage) | 5.4.0 | MIT | PDF rendering wrapper |
| [PDFium](https://pdfium.googlesource.com/pdfium/) via [pdfium-binaries](https://github.com/bblanchon/pdfium-binaries) (`pdfium.dll`) | 152.0.7961 | BSD-3-Clause / Apache-2.0 | `licenses\PDFium.txt` |
| [SkiaSharp](https://github.com/mono/SkiaSharp) (`libSkiaSharp.dll`) | 4.150.1 | MIT (Skia: BSD-3-Clause) | `licenses\SkiaSharp-*.txt` |
| [SharpCompress](https://github.com/adamhathcock/sharpcompress) | 0.50.5 | MIT | 7z extraction |
| [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) | 8.4.2 | MIT | |
| Microsoft.Extensions.* , System.IO.Hashing, System.Security.Cryptography.Pkcs | 10.0.13 | MIT | |

### ImageMagick and its delegate libraries

`Magick.Native-Q8-x64.dll` is built by the Magick.NET project and statically contains ImageMagick plus
these libraries (versions as of the bundled build): aom, brotli, bzip2, cairo, libcroco, libde265,
OpenEXR, libffi, fontconfig, FreeType, FriBidi, gdk-pixbuf, GLib, HarfBuzz, libheif, Highway, Imath,
libjpeg-turbo, libjxl, Little CMS, liblqr, liblzma, OpenH264, OpenJPEG, OpenJPH, Pango, pixman, libpng,
libraqm, LibRaw, librsvg, libtiff, libwebp, libxml2, libzip, zlib and zstd.

Most are under permissive licenses (BSD, MIT, zlib, Apache-2.0, libpng). The following are not:

| Library | License |
|---|---|
| FriBidi, GLib, gdk-pixbuf, liblqr, LibRaw, libde265, libheif, parts of liblzma | LGPL (2.1 or 3, per library) |
| Pango, librsvg, libcroco | LGPL-2 ("Library GPL") |
| cairo | LGPL-2.1 or MPL-1.1 |

These libraries are used unmodified, as part of a separate, replaceable dynamic library
(`Magick.Native-Q8-x64.dll`). You may replace that DLL with your own build. The corresponding source
code is available from each upstream project and from the Magick.NET build scripts at
<https://github.com/dlemstra/Magick.Native>. The complete license texts, as published by Magick.NET, are
in `licenses\Magick.NET-ImageMagick-and-delegate-libraries.txt`.

ImageMagick is a trademark of ImageMagick Studio LLC. OrbitConvert is not affiliated with or endorsed
by ImageMagick Studio LLC.

**Codec patents.** Some formats (HEIC/HEVC via libde265, H.264, AAC) may be covered by patents in some
countries. OrbitConvert provides no patent license; check the rules where you live and distribute.

## Tools that are *not* bundled

These are downloaded only when the user clicks **Download** in Settings (or runs
`orbitconvert-cli install-tool`), or are installed by the user separately. OrbitConvert runs them as
separate programs and does not link to them.

| Tool | What OrbitConvert uses it for | License | How it is obtained |
|---|---|---|---|
| [FFmpeg](https://ffmpeg.org/) | Video and audio | LGPL-2.1+ or GPL-3.0, depending on build. The pinned [gyan.dev](https://www.gyan.dev/ffmpeg/builds/) "essentials" build is **GPL-3.0** | Optional download (FFmpeg 9.0.2, SHA-256 pinned) or user install |
| [Pandoc](https://pandoc.org/) | Markdown, HTML, DOCX, ODT, EPUB | GPL-2.0-or-later | Optional download (3.12.1, SHA-256 pinned) or user install |
| [7-Zip](https://www.7-zip.org/) (`7zr.exe`) | Creating 7z archives | LGPL-2.1 | Optional download (26.04, SHA-256 pinned) or user install |
| [LibreOffice](https://www.libreoffice.org/) | Office documents to/from PDF | MPL-2.0 | User installs it from libreoffice.org |
| [Ghostscript](https://ghostscript.com/) | Lossy PDF compression | AGPL-3.0 | User installs it from ghostscript.com |

Downloaded tools are stored in `%LOCALAPPDATA%\OrbitConvert\tools\` together with an
`ORBITCONVERT-SOURCE.txt` file recording the download URL, checksum and license. They are not
redistributed by OrbitConvert; they come from the publishers' own release channels.

Because GPL and AGPL tools are only executed as separate processes, at the user's request, and are not
distributed with OrbitConvert, they do not change the license of OrbitConvert itself. If you build a
redistribution that **bundles** any of them, you must follow their licenses (for FFmpeg's GPL build:
provide or offer the corresponding source code, and license the bundle accordingly).

## Original artwork

The OrbitConvert name, logo (`assets/icons`) and icons were created for this project and are covered by
the MIT License. They are not derived from any other product's branding. Interface glyphs come from the
Segoe Fluent Icons / Segoe MDL2 Assets fonts that ship with Windows; the fonts are not redistributed.
