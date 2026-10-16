"""Downloads the newest successful CI build's `plantapi-ipa` artifact and saves
the contained .ipa as %USERPROFILE%\\Downloads\\ozok.ipa (always this name).

Usage: python tools\\fetch-ipa.py

Auth: reuses the GitHub token already stored in the Windows credential manager
(via `git credential fill`) -- no separate login needed.
"""

import json
import os
import shutil
import subprocess
import urllib.request
import zipfile

REPO = "evanzhao140/67-my-style"
TMP = os.path.join(os.environ.get("TEMP", "."), "fetch-ipa")
DEST = os.path.join(os.environ["USERPROFILE"], "Downloads", "ozok.ipa")


def token() -> str:
    out = subprocess.run(
        ["git", "credential", "fill"],
        input="protocol=https\nhost=github.com\n\n",
        capture_output=True,
        text=True,
        check=True,
    ).stdout
    for line in out.splitlines():
        if line.startswith("password="):
            return line[len("password="):]
    raise SystemExit("no GitHub token found in the git credential store")


def api_get(url: str, tok: str):
    req = urllib.request.Request(
        url,
        headers={
            "Authorization": "Bearer " + tok,
            "Accept": "application/vnd.github+json",
            "User-Agent": "fetch-ipa",
        },
    )
    with urllib.request.urlopen(req, timeout=60) as r:
        return json.loads(r.read())


def download(url: str, tok: str, out_path: str) -> None:
    # curl handles the GitHub -> Azure SAS redirect on this PC; urllib breaks
    # (401) when following the cross-host 302.
    subprocess.run(
        ["curl.exe", "-s", "-L", "-f", "-H", f"Authorization: Bearer {tok}", "-o", out_path, url],
        check=True,
    )


def main() -> None:
    os.makedirs(TMP, exist_ok=True)
    tok = token()

    runs = api_get(
        f"https://api.github.com/repos/{REPO}/actions/runs?branch=main&per_page=10", tok
    )
    run = next(
        (
            r
            for r in runs["workflow_runs"]
            if r["status"] == "completed" and r["conclusion"] == "success"
        ),
        None,
    )
    if run is None:
        raise SystemExit("no successful workflow run found on main")

    arts = api_get(
        f"https://api.github.com/repos/{REPO}/actions/runs/{run['id']}/artifacts", tok
    )
    art = next(
        (a for a in arts["artifacts"] if a["name"] == "plantapi-ipa" and not a["expired"]),
        None,
    )
    if art is None:
        raise SystemExit(f"no plantapi-ipa artifact on run {run['id']}")

    zpath = os.path.join(TMP, "ipa.zip")
    download(art["archive_download_url"], tok, zpath)

    with zipfile.ZipFile(zpath) as z:
        names = [n for n in z.namelist() if n.lower().endswith(".ipa")]
        if not names:
            raise SystemExit("no .ipa inside the artifact zip")
        with z.open(names[0]) as src, open(DEST, "wb") as dst:
            shutil.copyfileobj(src, dst)

    shutil.rmtree(TMP, ignore_errors=True)
    print(f"saved {DEST} ({os.path.getsize(DEST)} bytes) from run {run['id']}")


if __name__ == "__main__":
    main()
