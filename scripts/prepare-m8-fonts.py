"""Acquire official, unmodified Satoshi static fonts. No font conversion/subsetting.

Run from any directory with Python 3. Reuses a verified local archive when present.
"""
import hashlib
import json
from pathlib import Path
from urllib.request import urlopen
from zipfile import ZipFile

ROOT = Path(__file__).resolve().parents[1]
URL = "https://api.fontshare.com/v2/fonts/download/satoshi"
ARCHIVE = ROOT / "artifacts/m8-verification/satoshi-official.zip"
DEST = ROOT / "Inventor XR SO/Assets/XrSo/Ui/Fonts"


def main():
    ARCHIVE.parent.mkdir(parents=True, exist_ok=True)
    if not ARCHIVE.exists():
        with urlopen(URL, timeout=60) as response:
            ARCHIVE.write_bytes(response.read())
    manifest = ROOT / "assets/design-system/hive-ape/font-provenance.json"
    archive_hash = hashlib.sha256(ARCHIVE.read_bytes()).hexdigest()
    if manifest.exists() and json.loads(manifest.read_text(encoding="utf-8"))["archive_sha256"] != archive_hash:
        raise RuntimeError("Official archive changed: review license and provenance before accepting new fonts")
    records = []
    with ZipFile(ARCHIVE) as bundle:
        for name in ("Satoshi-Medium.ttf", "Satoshi-Bold.ttf"):
            source = f"Satoshi_Complete/Fonts/WEB/fonts/{name}"
            target = DEST / name
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_bytes(bundle.read(source))
            records.append({"entry": source, "path": str(target.relative_to(ROOT)).replace("\\", "/"),
                            "sha256": hashlib.sha256(target.read_bytes()).hexdigest()})
        license_text = bundle.read("Satoshi_Complete/License/FFL.txt")
        for target in (DEST / "FFL.txt", ROOT / "assets/design-system/hive-ape/licenses/Satoshi-FFL.txt"):
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_bytes(license_text)
    value = {"url": URL, "retrieved_date": "2026-10-04", "archive_sha256": archive_hash,
             "processing": "Official static binaries extracted byte-for-byte; no derivatives of font software.", "files": records}
    manifest.write_text(json.dumps(value, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(value, indent=2))


if __name__ == "__main__":
    main()
