#!/usr/bin/env python3
"""Collects the install guides' texts for translation.

The guides (guides.html in the server repo, shown by the site's Guides tab) are not marked up for translation: every
text block (headings, paragraphs, list items, table cells, captions) that holds no other block is a text, and its key
is "<guide>.<fingerprint>", the fingerprint being the first 10 hex digits of the SHA-1 of its English. The site finds
the translation of a block by the same fingerprint, so the guides can be edited freely: a changed block simply has a
new key (shown in English until it is translated again). Inline <strong>, <em>, <code>, <b>, <i>, <br> and links <a>
stay in the text; translations may use those and nothing else (each <a> takes the address of the English one in the
same order). This writes localization/guides/en.json.

    python3 tools/localization/extract-guides.py [path/to/guides.html]
    python3 tools/localization/extract-guides.py --check [path/to/guides.html]

The page defaults to ../wf-revival-server/content/files/guides.html next to this repo. The English must come
out exactly as the site's own richEnglish() makes it (creator.html), or the fingerprints will not match.
"""
import hashlib, html, json, os, re, sys
from html.parser import HTMLParser

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
OUT = os.path.join(ROOT, "localization", "guides", "en.json")
PAGE = next((p for p in (os.path.join(os.path.dirname(ROOT), d, "content", "files", "guides.html") for d in ("wf-revival-server", "warpforge-revival-server")) if os.path.exists(p)), None) or os.path.join(os.path.dirname(ROOT), "wf-revival-server", "content", "files", "guides.html")
VOID = {"br", "img", "input", "meta", "link", "hr", "wbr", "source", "col", "area", "base", "embed", "param", "track"}
RICH = {"strong", "em", "code", "br", "b", "i"}
BLOCKS = {"h1", "h2", "h3", "h4", "p", "li", "td", "th", "figcaption", "summary", "dt", "dd"}
NOT_INSIDE = BLOCKS | {"ul", "ol", "table", "figure", "img", "pre", "div", "section"}   # a block holding one of these is not a text

class Node:
    def __init__(self, tag, attrs, parent):
        self.tag, self.attrs, self.parent, self.kids = tag, dict(attrs), parent, []

class Tree(HTMLParser):
    def __init__(self):
        super().__init__(convert_charrefs=True)
        self.root = Node("#root", [], None); self.cur = self.root
    def handle_starttag(self, tag, attrs):
        n = Node(tag, attrs, self.cur); self.cur.kids.append(n)
        if tag not in VOID: self.cur = n
    def handle_startendtag(self, tag, attrs):
        self.cur.kids.append(Node(tag, attrs, self.cur))
    def handle_endtag(self, tag):
        n = self.cur
        while n is not self.root and n.tag != tag: n = n.parent
        if n is not self.root: self.cur = n.parent
    def handle_data(self, data):
        self.cur.kids.append(data)

def text(n):
    return n if isinstance(n, str) else "".join(text(k) for k in n.kids)

def esc(t):
    return t.replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;")

def english(n):                                   # as creator.html's richEnglish(): keep in step with it
    out = []
    for k in n.kids:
        if isinstance(k, str): out.append(esc(k))
        elif k.tag == "br": out.append("<br>")
        elif k.tag in RICH or k.tag == "a": out.append("<%s>%s</%s>" % (k.tag, english(k), k.tag))
        else: out.append(esc(text(k)))
    return "".join(out)

def walk(n, guide, found):
    if isinstance(n, str) or n.tag in ("script", "style", "head", "header", "nav"):
        return
    if n.tag == "section" and n.attrs.get("id"):
        guide = n.attrs["id"]
    if n.tag in BLOCKS and not has_block(n):
        t = re.sub(r"\s+", " ", english(n)).strip()
        if t and re.search(r"[A-Za-z]", re.sub(r"<[^>]+>", "", t)):
            key = guide + "." + hashlib.sha1(t.encode("utf-8")).hexdigest()[:10]
            found[key] = t
        return
    for k in n.kids:
        walk(k, guide, found)

def has_block(n):
    return any(not isinstance(k, str) and (k.tag in NOT_INSIDE or has_block(k)) for k in n.kids)

def main():
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    t = Tree(); t.feed(open(args[0] if args else PAGE, encoding="utf-8").read())
    found = {}
    walk(t.root, "home", found)
    out = json.dumps(dict(sorted(found.items())), ensure_ascii=False, indent=2) + "\n"
    if "--check" in sys.argv:
        old = open(OUT, encoding="utf-8").read() if os.path.exists(OUT) else ""
        print("guides en.json up to date" if old == out else "localization/guides/en.json is out of date: run tools/localization/extract-guides.py")
        return 0 if old == out else 1
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    open(OUT, "w", encoding="utf-8", newline="\n").write(out)
    print(f"{len(found)} texts written to localization/guides/en.json ({sum(len(v.split()) for v in found.values())} words)")
    return 0

if __name__ == "__main__":
    sys.exit(main())
