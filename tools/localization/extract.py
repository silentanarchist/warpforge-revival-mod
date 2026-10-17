#!/usr/bin/env python3
"""Collects the mod's texts for translation.

Every sentence the mod shows is written as Loc.T("area.key", "English text", ...) in WarpforgeRevival/*.cs. This finds
them all and writes localization/mod/en.json (key -> English, sorted), the file the translators work from. Run it after
changing or adding texts; it fails when one key is given two different English texts, or when a text is not a plain
string (the English has to be readable here, so no $"..." or + in it).

    python3 tools/localization/extract.py          write localization/mod/en.json
    python3 tools/localization/extract.py --check  only check that en.json is up to date (exit 1 if not)
"""
import json, os, re, sys

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
SRC = os.path.join(ROOT, "WarpforgeRevival")
OUT = os.path.join(ROOT, "localization", "mod", "en.json")
# Loc.T("key", "text" ...  : a C# regular string literal (escapes allowed), right after the key
CALL = re.compile(r'Loc\.T\(\s*"([a-zA-Z0-9_.]+)"\s*,\s*(\$?@?)"((?:[^"\\]|\\.)*)"')
KEY_ONLY = re.compile(r'Loc\.T\(\s*"([a-zA-Z0-9_.]+)"')

def unescape(s):
    return bytes(s, "utf-8").decode("unicode_escape").encode("latin-1").decode("utf-8")

def main():
    found, where, bad = {}, {}, []
    for name in sorted(os.listdir(SRC)):
        if not name.endswith(".cs") or name == "Loc.cs":
            continue
        text = open(os.path.join(SRC, name), encoding="utf-8").read()
        good = {m.start() for m in CALL.finditer(text)}
        for m in KEY_ONLY.finditer(text):
            if m.start() not in good:
                bad.append(f"{name}: Loc.T(\"{m.group(1)}\", ...) - the English must be a plain \"...\" string")
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
            print("localization/mod/en.json is out of date: run tools/localization/extract.py")
            return 1
        print(f"en.json up to date ({len(found)} texts)")
        return 0
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    with open(OUT, "w", encoding="utf-8", newline="\n") as f:
        f.write(out)
    print(f"{len(found)} texts written to localization/mod/en.json")
    return 0

if __name__ == "__main__":
    sys.exit(main())
