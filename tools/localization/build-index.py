#!/usr/bin/env python3
"""Makes localization/index.json: for each area (mod, ...) the languages that have a file, with its SHA-256. The mod
reads the server's copy to know which language files exist and whether its kept copy is current. Run it before copying
the localization folder to the server (W:\\content\\files\\localization\\)."""
import hashlib, json, os, sys

ROOT = os.path.join(os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))), "localization")
index = {}
for area in sorted(os.listdir(ROOT)):
    folder = os.path.join(ROOT, area)
    if not os.path.isdir(folder):
        continue
    for f in sorted(os.listdir(folder)):
        if f.endswith(".json") and f != "en.json":
            data = open(os.path.join(folder, f), "rb").read()
            json.loads(data)                                  # a broken file stops here, before it reaches players
            index.setdefault(area, {})[f[:-5]] = hashlib.sha256(data).hexdigest()
with open(os.path.join(ROOT, "index.json"), "w", encoding="utf-8", newline="\n") as fh:
    json.dump(index, fh, indent=2, sort_keys=True)
    fh.write("\n")
print("index.json:", {a: sorted(l) for a, l in index.items()})
