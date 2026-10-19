#!/usr/bin/env python3
"""Collects the messages the Revival server sends to players, for translation.

Every message the server answers with goes through err(code, message, ...) in playfab_emu.py (server repo). This
reads that file (it does not run it) and writes localization/server/en.json: one entry per message, keyed
"server.<fingerprint>", the fingerprint being the first 10 hex digits of the SHA-1 of the English template.

Parts filled in when the message is made become numbered placeholders:
    err("X", "%s is already in an alliance" % name)        ->  "{p1} is already in an alliance"
    err("X", f"Wait {left} seconds")                       ->  "Wait {p1} seconds"
Translations must keep every {pN}; the server puts the same values back in (playfab_emu.py, _localize_reply).

The server translates a reply itself when the mod or the website says which language the player uses (the
X-Revival-Lang header), from content/files/localization/server/<lang>.json. A message that changes in the code
simply gets a new key and is sent in English until it is translated again.

    python3 tools/localization/extract-server.py [path/to/playfab_emu.py]
"""
import ast, hashlib, json, os, re, sys

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
OUT = os.path.join(ROOT, "localization", "server", "en.json")
SOURCE = os.path.join(os.path.dirname(ROOT), "warpforge-revival-server", "playfab_emu.py")


def key_of(template):
    """The key of an English template; playfab_emu.py computes it the same way."""
    return "server." + hashlib.sha1(template.encode("utf-8")).hexdigest()[:10]


def template_of(node):
    """The English template of an err() message argument, or None when it is not made from literal text."""
    if isinstance(node, ast.Constant) and isinstance(node.value, str):
        return node.value
    if isinstance(node, ast.JoinedStr):                       # f"..."
        out, n = [], 0
        for part in node.values:
            if isinstance(part, ast.Constant):
                out.append(str(part.value))
            else:
                n += 1
                out.append("{p%d}" % n)
        return "".join(out) if any(isinstance(p, ast.Constant) for p in node.values) else None
    if isinstance(node, ast.BinOp) and isinstance(node.op, ast.Mod) and isinstance(node.left, ast.Constant) \
            and isinstance(node.left.value, str):              # "..." % value(s)
        text, out, n, i = node.left.value, [], 0, 0
        while i < len(text):
            if text[i] == "%" and i + 1 < len(text):
                if text[i + 1] == "%":
                    out.append("%"); i += 2; continue
                j = i + 1
                while j < len(text) and text[j] in "0123456789.-+ #":
                    j += 1
                if j < len(text) and text[j] in "sdifr":
                    n += 1; out.append("{p%d}" % n); i = j + 1; continue
            out.append(text[i]); i += 1
        return "".join(out)
    return None


def main():
    src = sys.argv[1] if len(sys.argv) > 1 else SOURCE
    tree = ast.parse(open(src, encoding="utf-8").read())
    texts, skipped = {}, 0
    for node in ast.walk(tree):
        if isinstance(node, ast.Call) and isinstance(node.func, ast.Name) and node.func.id == "err" and len(node.args) >= 2:
            t = template_of(node.args[1])
            if t is None or sum(c.isalpha() for c in re.sub(r"\{p\d+\}", "", t)) < 3:     # nothing to translate
                skipped += 1
                continue
            texts[key_of(t)] = t
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    with open(OUT, "w", encoding="utf-8", newline="\n") as fh:
        json.dump(dict(sorted(texts.items())), fh, ensure_ascii=False, indent=2)
        fh.write("\n")
    print(f"{len(texts)} server messages written to localization/server/en.json ({skipped} not made from literal text, left out)")


if __name__ == "__main__":
    main()
