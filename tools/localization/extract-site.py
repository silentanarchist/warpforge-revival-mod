#!/usr/bin/env python3
"""Collects the website's texts for translation.

The site (creator.html in the server repo) marks its texts two ways: on elements as data-t="key" (the element's own
text is the English; data-t-title / data-t-placeholder for those attributes), and in scripts as
T("key", "English", ...). This finds them all and writes localization/site/en.json (key -> English, sorted), the file
the translators work from. It fails when one key has two different English texts, or a marked element has markup
inside it (only plain text can be swapped).

    python3 tools/localization/extract-site.py [path/to/creator.html]          write localization/site/en.json
    python3 tools/localization/extract-site.py --check [path/to/creator.html]  only check it is up to date

The page defaults to ../warpforge-revival-server/content/files/creator.html next to this repo.
"""
import html, json, os, re, sys
from html.parser import HTMLParser

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
OUT = os.path.join(ROOT, "localization", "site", "en.json")
PAGE = os.path.join(os.path.dirname(ROOT), "warpforge-revival-server", "content", "files", "creator.html")
CALL = re.compile(r'\bT\(\s*"([a-zA-Z0-9_.]+)"\s*,\s*"((?:[^"\\]|\\.)*)"')
KEY_ONLY = re.compile(r'\bT\(\s*"([a-zA-Z0-9_.]+)"')
VOID = {"br", "img", "input", "meta", "link", "hr", "wbr", "source", "col", "area", "base", "embed", "param", "track"}

class Marks(HTMLParser):
    def __init__(self):
        super().__init__(convert_charrefs=True)
        self.found, self.bad, self.open = [], [], []     # open: [key, text, depth] for data-t elements being read
        self.depth = 0
    def handle_starttag(self, tag, attrs):
        a = dict(attrs)
        for attr in ("title", "placeholder"):
            if a.get("data-t-" + attr):
                self.found.append((a["data-t-" + attr], a.get(attr) or ""))
        for o in self.open:
            o[3] = True                                   # markup inside a marked element
        if tag in VOID:
            return
        self.depth += 1
        if a.get("data-t"):
            self.open.append([a["data-t"], "", self.depth, False])
    def handle_startendtag(self, tag, attrs):
        a = dict(attrs)
        for attr in ("title", "placeholder"):
            if a.get("data-t-" + attr):
                self.found.append((a["data-t-" + attr], a.get(attr) or ""))
    def handle_data(self, data):
        for o in self.open:
            o[1] += data
    def handle_endtag(self, tag):
        if tag in VOID:
            return
        if self.open and self.open[-1][2] == self.depth:
            key, text, _, inner = self.open.pop()
            if inner:
                self.bad.append(f'data-t="{key}": the element has markup inside; mark a plain-text element instead')
            self.found.append((key, text))
        self.depth -= 1

def main():
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    page = args[0] if args else PAGE
    text = open(page, encoding="utf-8").read()
    p = Marks()
    p.feed(text)
    found, where, bad = {}, {}, list(p.bad)
    items = [(k, v, "data-t") for k, v in p.found]
    script = "\n".join(re.findall(r"<script>([\s\S]*?)</script>", text))
    good = {m.start() for m in CALL.finditer(script)}
    for m in KEY_ONLY.finditer(script):
        if m.start() not in good:
            bad.append(f'T("{m.group(1)}", ...) - the English must be a plain "..." string')
    for m in CALL.finditer(script):
        items.append((m.group(1), json.loads('"' + m.group(2) + '"'), "T()"))
    for key, english, how in items:
        english = english.strip()
        if key in found and found[key] != english:
            bad.append(f"{key}: two different English texts ({found[key]!r} and {english!r})")
        found[key] = english
    if bad:
        print("\n".join(bad))
        return 1
    out = json.dumps(dict(sorted(found.items())), ensure_ascii=False, indent=2) + "\n"
    if "--check" in sys.argv:
        old = open(OUT, encoding="utf-8").read() if os.path.exists(OUT) else ""
        if old != out:
            print("localization/site/en.json is out of date: run tools/localization/extract-site.py")
            return 1
        print(f"site en.json up to date ({len(found)} texts)")
        return 0
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    with open(OUT, "w", encoding="utf-8", newline="\n") as f:
        f.write(out)
    print(f"{len(found)} texts written to localization/site/en.json")
    return 0

if __name__ == "__main__":
    sys.exit(main())
