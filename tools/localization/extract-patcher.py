#!/usr/bin/env python3
"""Collects the Warpforge Patcher app's texts for translation.

The app (silentanarchist/MelonLoaderInstaller, folder WarpforgePatcher) writes every sentence it shows as
Loc.T("area.key", "English text", ...), new LocText("area.key", "English text", ...) or
new UserError("area.key", "English text", ...). This finds them all and writes localization/patcher/en.json
(key -> English, sorted), the file the translators work from. Run it after changing the app's texts; it fails when one
key is given two different English texts, or when a text is not a plain "..." string.

    python3 tools/localization/extract-patcher.py [path/to/WarpforgePatcher]          write localization/patcher/en.json
    python3 tools/localization/extract-patcher.py [path/to/WarpforgePatcher] --check  only check that en.json is up to date

The default path is ../melonloaderinstaller/WarpforgePatcher (the fork checked out next to this repository).
The app builds the language files in (its build.sh copies localization/patcher/*.json into the app), and fetches newer
copies from the server (/api/v1/content/files/localization/patcher/<code>.json, listed in index.json).
"""
import json, os, re, sys

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
OUT = os.path.join(ROOT, "localization", "patcher", "en.json")
CALL = re.compile(r'(?:Loc\.T|LocText|UserError)\(\s*"([a-zA-Z0-9_.]+)"\s*,\s*(\$?@?)"((?:[^"\\]|\\.)*)"')
KEY_ONLY = re.compile(r'(?:Loc\.T|LocText|UserError)\(\s*"([a-zA-Z0-9_.]+)"')

def unescape(s):
    return bytes(s, "utf-8").decode("unicode_escape").encode("latin-1").decode("utf-8")

def main():
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    src = args[0] if args else os.path.join(ROOT, "..", "melonloaderinstaller", "WarpforgePatcher")
    if not os.path.isdir(src):
        print(f"{src} not found: give the path to the app's WarpforgePatcher folder")
        return 1
    found, where, bad = {}, {}, []
    for name in sorted(os.listdir(src)):
        if not name.endswith(".cs") or name == "Loc.cs":
            continue
        text = open(os.path.join(src, name), encoding="utf-8").read()
        good = {m.start() for m in CALL.finditer(text)}
        for m in KEY_ONLY.finditer(text):
            if m.start() not in good:
                bad.append(f"{name}: {m.group(1)} - the English must be a plain \"...\" string right after the key")
        for m in CALL.finditer(text):
            key, prefix, english = m.group(1), m.group(2), unescape(m.group(3))
            if prefix:
                bad.append(f"{name}: {key} - the English must be a plain \"...\" string, not {prefix}\"...\"")
                continue
            if key in found and found[key] != english:
                bad.append(f"{key}: two different English texts ({where[key]} and {name})")
            found[key], where[key] = english, name
    if bad:
        print("\n".join(bad))
        return 1
    out = json.dumps(dict(sorted(found.items())), ensure_ascii=False, indent=2) + "\n"
    if "--check" in sys.argv:
        old = open(OUT, encoding="utf-8").read() if os.path.exists(OUT) else ""
        if old != out:
            print("localization/patcher/en.json is out of date: run tools/localization/extract-patcher.py")
            return 1
        print(f"en.json up to date ({len(found)} texts)")
        return 0
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    with open(OUT, "w", encoding="utf-8", newline="\n") as f:
        f.write(out)
    print(f"{len(found)} texts written to localization/patcher/en.json")
    return 0

if __name__ == "__main__":
    sys.exit(main())
