"""Publish a small, authenticated CI snapshot of GitHub Releases for installed clients."""

import json
import os
import pathlib
import re
import sys
import urllib.request


REPOSITORY = "DevTeam/AI.Client"
ASSET_NAME = re.compile(r"^AI\.(?:Host|Desktop|Mcp\.CSharp)-(?:win|osx|linux)-(?:x64|arm64)\.(?:exe|pkg|deb)$")
DIGEST = re.compile(r"^sha256:[0-9a-fA-F]{64}$")


def main(output: pathlib.Path) -> None:
    token = os.environ["GH_TOKEN"]
    releases = []
    page = 1
    while True:
        request = urllib.request.Request(
            f"https://api.github.com/repos/{REPOSITORY}/releases?per_page=100&page={page}",
            headers={
                "Accept": "application/vnd.github+json",
                "Authorization": f"Bearer {token}",
                "User-Agent": "AI.Client-Release-Catalog/1.0",
            },
        )
        with urllib.request.urlopen(request, timeout=30) as response:
            batch = json.load(response)
        if not isinstance(batch, list):
            raise ValueError("GitHub returned an invalid release list")
        for release in batch:
            if release["draft"]:
                continue
            assets = []
            for asset in release["assets"]:
                if not ASSET_NAME.fullmatch(asset["name"]):
                    continue
                if not DIGEST.fullmatch(asset.get("digest") or ""):
                    raise ValueError(f"Missing SHA-256 digest for {release['tag_name']}/{asset['name']}")
                assets.append({
                    "name": asset["name"],
                    "browser_download_url": asset["browser_download_url"],
                    "digest": asset["digest"],
                    "size": asset["size"],
                })
            if assets:
                releases.append({
                    "tag_name": release["tag_name"],
                    "draft": False,
                    "prerelease": release["prerelease"],
                    "html_url": release["html_url"],
                    "assets": assets,
                })
        if len(batch) < 100:
            break
        page += 1
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps({"schemaVersion": 1, "releases": releases}, separators=(",", ":")), encoding="utf-8")
    print(f"Published {len(releases)} releases in {output}")


if __name__ == "__main__":
    main(pathlib.Path(sys.argv[1]))
