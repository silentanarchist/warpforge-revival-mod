#!/usr/bin/env python3
"""Collects the website's texts for translation.

The site (creator.html in the server repo) marks its texts two ways: on elements as data-t="key" (the element's own
text is the English; data-t-title / data-t-placeholder for those attributes), and in scripts as
T("key", "English", ...). This finds them all and writes localization/site/en.json (key -> English, sorted), the file
the translators work from. It fails when one key has two different English texts, or a marked element has markup
inside it (only plain text can be swapped) other than children marked data-t-part="name", which stand in the English
as {name} (a link inside a sentence, say). Elements marked data-t-rich="key" keep <strong>, <em>, <code> and <br>
in their English (and translations may use those, nothing else). Runs of spaces and line breaks become one space.

A script call whose key is computed, T("creator.kind." + id, label), is allowed when the literal part ends in a dot;
its keys must then come from somewhere this script can see: either a function that lists them as plain calls
(creatorKindKeys() in the page) or the Card Creator's content files next to the page, which give the creatorHelp.*
keys: creator-help.json (creatorHelp.trigger.<id>.name / .text, and creatorHelp.keywordName.<id> / creatorHelp.keyword.<id>
for the keywords it names and describes), keywords.json (creatorHelp.keyword.<id> for the others, and creatorHelp.rulebook.<id or name> for
the cheat sheet) and stratagem-types.json (creatorHelp.stratagemType.<id>). Triggers the game never fires (status dead) are
left out. Only the creator's own wording is collected; a card's name and rules text stay as written.

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
RICH = {"strong", "em", "code", "br", "b", "i"}            # the tags a data-t-rich text (and its translations) may hold
VOID = {"br", "img", "input", "meta", "link", "hr", "wbr", "source", "col", "area", "base", "embed", "param", "track"}

class Marks(HTMLParser):
    def __init__(self):
        super().__init__(convert_charrefs=True)
        self.found, self.bad, self.open = [], [], []     # open: [key, text, depth, inner, skip] for data-t elements being read
        self.depth = 0
    def handle_starttag(self, tag, attrs):
        a = dict(attrs)
        for attr in ("title", "placeholder"):
            if a.get("data-t-" + attr):
                self.found.append((a["data-t-" + attr], a.get(attr) or ""))
        if tag in VOID:
            for o in self.open:
                if o[4]: continue
                if o[5] and tag == "br": o[1] += "<br>"
                else: o[3] = True
            return
        self.depth += 1
        for o in self.open:
            if o[4]:
                continue                                  # inside a part: that part's own business
            if a.get("data-t-part") and (o[5] or self.depth == o[2] + 1):
                o[1] += "{" + a["data-t-part"] + "}"; o[4] = self.depth
            elif o[5] and tag in RICH:
                o[1] += "<" + tag + ">"
            else:
                o[3] = True                               # markup inside a marked element
        if a.get("data-t"):
            self.open.append([a["data-t"], "", self.depth, False, 0, False, tag])
        if a.get("data-t-rich"):
            self.open.append([a["data-t-rich"], "", self.depth, False, 0, True, tag])
    def handle_startendtag(self, tag, attrs):
        a = dict(attrs)
        for attr in ("title", "placeholder"):
            if a.get("data-t-" + attr):
                self.found.append((a["data-t-" + attr], a.get(attr) or ""))
    def handle_data(self, data):
        for o in self.open:
            if not o[4]:
                o[1] += html.escape(data, quote=False) if o[5] else data
    def handle_endtag(self, tag):
        if tag in VOID:
            return
        for o in self.open:
            if o[4] == self.depth:
                o[4] = 0                                  # the part ends
            elif not o[4] and o[5] and tag in RICH and o[2] != self.depth:
                o[1] += "</" + tag + ">"
        if self.open and self.open[-1][2] == self.depth:
            key, text, _, inner, _, _, _ = self.open.pop()
            if inner:
                self.bad.append(f'data-t="{key}": the element has markup inside; mark a plain-text element instead')
            self.found.append((key, text))
        self.depth -= 1

def content_keys(folder):
    """The creatorHelp.* keys: texts the Card Creator shows from its content files (see the module doc)."""
    def load(name):
        path = os.path.join(folder, name)
        if not os.path.exists(path):
            print(f"note: {name} not found next to the page; its creatorHelp keys are left out")
            return None
        with open(path, encoding="utf-8") as f:
            return json.load(f)
    out = []
    help_ = load("creator-help.json") or {"triggers": {}, "keywords": {}}
    for tid, t in help_["triggers"].items():
        if t.get("status") == "dead":
            continue
        if t.get("name"): out.append((f"creatorHelp.trigger.{tid}.name", t["name"], "creator-help.json"))
        if t.get("text"): out.append((f"creatorHelp.trigger.{tid}.text", t["text"], "creator-help.json"))
    described = set()
    for kid, k in help_["keywords"].items():
        if k.get("name"): out.append((f"creatorHelp.keywordName.{kid}", k["name"], "creator-help.json"))
        if k.get("text"):
            out.append((f"creatorHelp.keyword.{kid}", k["text"], "creator-help.json")); described.add(kid)
    keywords = load("keywords.json") or {"keywords": []}
    for k in keywords["keywords"]:
        if not k.get("text") or not (k.get("id") or k.get("name")):
            continue
        out.append((f"creatorHelp.rulebook.{k.get('id') or k['name']}", k["text"], "keywords.json"))   # no id: keyed by its name
        if k.get("id") and k["id"] not in described:
            out.append((f"creatorHelp.keyword.{k['id']}", k["text"], "keywords.json"))
    types = load("stratagem-types.json") or {"types": []}
    for t in types["types"]:
        if t.get("id") and t.get("text"):
            out.append((f"creatorHelp.stratagemType.{t['id']}", t["text"], "stratagem-types.json"))
    return out

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
        if m.start() not in good and not m.group(1).endswith("."):
            bad.append(f'T("{m.group(1)}", ...) - the English must be a plain "..." string')
    for m in CALL.finditer(script):
        items.append((m.group(1), json.loads('"' + m.group(2) + '"'), "T()"))
    items += content_keys(os.path.dirname(os.path.abspath(page)))
    for key, english, how in items:
        english = re.sub(r"\s+", " ", english).strip()
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
