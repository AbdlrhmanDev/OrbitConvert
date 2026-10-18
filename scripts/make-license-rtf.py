"""Regenerates installer/License.rtf from LICENSE so the installer never shows stale terms.

Usage: python scripts/make-license-rtf.py
"""
from pathlib import Path

root = Path(__file__).resolve().parent.parent
text = (root / "LICENSE").read_text(encoding="utf-8")
escaped = text.replace("\\", "\\\\").replace("{", "\\{").replace("}", "\\}")
body = "\\par\n".join(escaped.splitlines())
rtf = "{\\rtf1\\ansi\\deff0{\\fonttbl{\\f0 Segoe UI;}}\\fs18\n" + body + "\\par\n}\n"
(root / "installer" / "License.rtf").write_text(rtf, encoding="ascii", errors="replace")
print("wrote installer/License.rtf")
