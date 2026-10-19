"""Builds the static font files bundled with the desktop app.

WPF cannot reliably select weights from variable fonts, so this script instantiates static
Bricolage Grotesque weights (OFL-1.1, no Reserved Font Name) from the Google Fonts variable font and
copies the static JetBrains Mono files (OFL-1.1) from the official release.

Usage:
    pip install fonttools
    python scripts/make-fonts.py <BricolageGrotesque[opsz,wdth,wght].ttf> <JetBrainsMono-2.304.zip>
"""
import sys
import zipfile
from pathlib import Path

from fontTools.ttLib import TTFont
from fontTools.varLib import instancer

root = Path(__file__).resolve().parent.parent
out = root / "src" / "OrbitConvert.App" / "Assets" / "Fonts"
out.mkdir(parents=True, exist_ok=True)

variable, jetbrains_zip = Path(sys.argv[1]), Path(sys.argv[2])

# Optical size 14 suits UI text; the display weight uses a larger optical size like the design.
for weight, name, opsz in [(400, "Regular", 14), (500, "Medium", 14), (600, "SemiBold", 14),
                           (700, "Bold", 24), (800, "ExtraBold", 36)]:
    font = TTFont(variable)
    static = instancer.instantiateVariableFont(font, {"wght": weight, "wdth": 100, "opsz": opsz}, updateFontNames=True)
    # One family across all files: Regular/Bold use the legacy RIBBI names, other weights get their
    # own legacy family, and IDs 16/17 carry the typographic family so WPF groups them by weight.
    family = "Bricolage Grotesque"
    ribbi = name in ("Regular", "Bold")
    table = static["name"]
    for record in list(table.names):
        if record.nameID in (1, 2, 4, 6, 16, 17):
            table.removeNames(nameID=record.nameID)
    table.setName(family if ribbi else f"{family} {name}", 1, 3, 1, 0x409)
    table.setName(name if ribbi else "Regular", 2, 3, 1, 0x409)
    table.setName(f"{family} {name}", 4, 3, 1, 0x409)
    table.setName(f"BricolageGrotesque-{name}", 6, 3, 1, 0x409)
    table.setName(family, 16, 3, 1, 0x409)
    table.setName(name, 17, 3, 1, 0x409)
    static["OS/2"].fsSelection = (static["OS/2"].fsSelection & ~0b1100001) | (0b100000 if name == "Bold" else 0b1000000)
    static["head"].macStyle = 1 if name == "Bold" else 0
    static.save(out / f"BricolageGrotesque-{name}.ttf")
    print("wrote", f"BricolageGrotesque-{name}.ttf")

with zipfile.ZipFile(jetbrains_zip) as z:
    for weight in ("Regular", "Medium", "Bold"):
        data = z.read(f"fonts/ttf/JetBrainsMono-{weight}.ttf")
        (out / f"JetBrainsMono-{weight}.ttf").write_bytes(data)
        print("wrote", f"JetBrainsMono-{weight}.ttf")
    (root / "licenses" / "JetBrainsMono-OFL.txt").write_bytes(z.read("OFL.txt"))
