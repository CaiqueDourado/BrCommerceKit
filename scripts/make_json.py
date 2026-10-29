"""One platform detection result, as JSON (docs/example-detection.json).

    python scripts/make_json.py                # needs the .NET 8 SDK
    python scripts/make_json.py --no-restore   # offline, after a restore

Runs the small console project tools/DetectExample, which calls ``PlatformDetector.DetectAsync``
on loja-exemplo.com.br with an in-memory HTTP handler built from the test fixtures (vtex-home.html
and google-shopping-rss.xml, the same setup as the test Detects_vtex_with_feed_and_follows_relative_redirect).
The JSON is what ``brcommerce detect --json`` prints for a ``DetectionResult``. No network.
Run from the repository root.
"""

from __future__ import annotations

import json
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "docs" / "example-detection.json"


def main() -> None:
    cmd = ["dotnet", "run", "-c", "Release", "--project", str(ROOT / "tools" / "DetectExample")]
    if "--no-restore" in sys.argv[1:]:
        cmd.append("--no-restore")
    cmd += ["--", str(ROOT / "tests" / "BrCommerce.Tests" / "Fixtures")]
    stdout = subprocess.run(cmd, check=True, capture_output=True, text=True, encoding="utf-8", cwd=ROOT).stdout
    result = json.loads(stdout)
    OUT.write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(OUT)


if __name__ == "__main__":
    main()
