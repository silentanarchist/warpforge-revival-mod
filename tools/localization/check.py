#!/usr/bin/env python3
"""Checks translation files against their English: python check.py <mod|site|guides|patcher|server> [language ...]

For each language (all of them when none are named) it reports:
  - keys missing from the translation, and keys the English no longer has
  - {placeholders} that differ from the English (each one must stay, spelled exactly the same; using one twice is fine)
  - HTML tags that differ from the English, or tags that are not allowed (site and guides: strong, em, code, b, i, a, br)
  - text inside <code>...</code> that was changed (code, file names and commands are never translated)
  - empty translations
Exit code 1 when anything is wrong, so it can run before a commit or a deploy.
"""
import collections
import json
import os
import re
import sys

ROOT = os.path.join(os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))), "localization")
TAG = re.compile(r"</?(?:strong|code|em|b|i|a)>|<br>")
ANY_TAG = re.compile(r"<[^>]*>")
PLACEHOLDER = re.compile(r"\{\w+\}")
CODE = re.compile(r"<code>(.*?)</code>", re.S)


def load(path):
    with open(path, encoding="utf-8") as f:
        return json.load(f)


def check(area, lang, en):
    path = os.path.join(ROOT, area, lang + ".json")
    if not os.path.exists(path):
        return [f"{lang}: no file"]
    d = load(path)
    out = []
    missing = [k for k in en if k not in d]
    extra = [k for k in d if k not in en]
    if missing:
        out.append(f"{lang}: {len(missing)} missing, e.g. {missing[:5]}")
    if extra:
        out.append(f"{lang}: {len(extra)} not in English any more, e.g. {extra[:5]}")
    for k, e in en.items():
        t = d.get(k)
        if t is None:
            continue
        if not isinstance(t, str) or not t.strip():
            out.append(f"{lang}: {k}: empty")
            continue
        if set(PLACEHOLDER.findall(e)) != set(PLACEHOLDER.findall(t)):          # using one twice is fine
            out.append(f"{lang}: {k}: placeholders {sorted(set(PLACEHOLDER.findall(t)))} should be {sorted(set(PLACEHOLDER.findall(e)))}")
        if area in ("site", "guides"):
            if collections.Counter(TAG.findall(e)) != collections.Counter(TAG.findall(t)):
                out.append(f"{lang}: {k}: tags differ from the English")
            bad = [x for x in ANY_TAG.findall(t) if not TAG.fullmatch(x)]
            if bad:
                out.append(f"{lang}: {k}: tags not allowed: {bad[:3]}")
            if sorted(CODE.findall(e)) != sorted(CODE.findall(t)):
                out.append(f"{lang}: {k}: text inside <code> was changed")
    return out


def main():
    if len(sys.argv) < 2 or sys.argv[1] not in ("mod", "site", "guides", "patcher", "server"):
        print(__doc__)
        return 2
    area = sys.argv[1]
    en = load(os.path.join(ROOT, area, "en.json"))
    langs = sys.argv[2:] or sorted(n[:-5] for n in os.listdir(os.path.join(ROOT, area)) if n.endswith(".json") and n != "en.json")
    problems = []
    for lang in langs:
        problems += check(area, lang, en)
    for p in problems:
        print(p)
    print(f"{area}: {len(langs)} language(s) checked, {len(problems)} problem(s)")
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main())
