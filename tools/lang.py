"""The Japanese and Chinese tables (src/Bloom/Assets/Lang/<code>.tsv) against the texts in the code.

  python tools/lang.py check          keys missing from each table, stale ones, and Tr calls the tables can't reach
  python tools/lang.py todo ja [n]    the next n (default all) missing keys for a table: "key<TAB>Korean", in code order
  python tools/lang.py tidy           rewrite each table in code order, stale keys dropped

Every text is written `Tr("한국어", "English")`; the English text is the key. An interpolated English text is keyed
by its pattern, the holes numbered as the code writes them: $"{n} files" -> "{0} files" (Lang.EnText builds the same
pattern at run time). Table lines are `key<TAB>translation`, with \\n, \\t and \\\\ escaped.
"""
import io
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC = os.path.join(ROOT, 'src', 'Bloom')
TABLES = os.path.join(SRC, 'Assets', 'Lang')
CODES = ('ja', 'zh')
SIMPLE = {'n': '\n', 't': '\t', 'r': '\r', '0': '\0', '\\': '\\', '"': '"', "'": "'", 'a': '\a', 'b': '\b', 'f': '\f', 'v': '\v'}


class Lit:
    """A string literal: its text (plain) or its pattern (interpolated, holes numbered from 0)."""

    def __init__(self, text, holes):
        self.text, self.holes = text, holes


def escape_seq(s, i):
    """The character of the escape at s[i] ('\\'), and the index after it."""
    c = s[i + 1]
    if c in SIMPLE:
        return SIMPLE[c], i + 2
    if c == 'u':
        return chr(int(s[i + 2:i + 6], 16)), i + 6
    if c == 'U':
        return chr(int(s[i + 2:i + 10], 16)), i + 10
    if c == 'x':
        j = i + 2
        while j < len(s) and j < i + 6 and s[j] in '0123456789abcdefABCDEF':
            j += 1
        return chr(int(s[i + 2:j], 16)), j
    raise ValueError('escape ' + s[i:i + 2])


def skip_string(s, i):
    """Index after the string or char literal starting at s[i] (any prefix: $ @ $@ @$, raw ones too)."""
    j = i
    while s[j] in '$@':
        j += 1
    verbatim = '@' in s[i:j]
    if s[j] == "'":
        k = j + 1
        while s[k] != "'":
            k += 2 if s[k] == '\\' else 1
        return k + 1
    if s.startswith('"""', j):
        q = j
        while s[q] == '"':
            q += 1
        n = q - j
        end = s.index('"' * n, q)
        return end + n
    interp = '$' in s[i:j]
    k = j + 1
    while True:
        c = s[k]
        if c == '\\' and not verbatim:
            k += 2
        elif c == '"':
            if verbatim and s[k + 1:k + 2] == '"':
                k += 2
            else:
                return k + 1
        elif interp and c == '{':
            if s[k + 1] == '{':
                k += 2
            else:
                k = skip_expr(s, k + 1, '}') + 1
        else:
            k += 1


def skip_expr(s, i, close):
    """Index of the top-level `close` (or ',' when close is ',)') from s[i], skipping strings, comments, brackets."""
    depth = 0
    k = i
    while k < len(s):
        c = s[k]
        if c in '"\'' or (c in '$@' and s[k + 1:k + 2] in ('"', '$', '@')):
            k = skip_string(s, k)
            continue
        if s.startswith('//', k):
            k = s.index('\n', k)
            continue
        if s.startswith('/*', k):
            k = s.index('*/', k) + 2
            continue
        if c in '([{':
            depth += 1
        elif c in ')]}':
            if depth == 0:
                if c in close:
                    return k
                raise ValueError('unbalanced at %d' % k)
            depth -= 1
        elif depth == 0 and c in close:
            return k
        k += 1
    raise ValueError('no end')


def parse_literal(s):
    """A Lit when `s` is one string literal or plain literals joined by +, else None."""
    parts = []
    k = 0
    s = s.strip()
    while k < len(s):
        if s[k].isspace():
            k += 1
            continue
        if parts and s[k] == '+':
            k += 1
            continue
        if s[k] not in '"$@':
            return None
        end = skip_string(s, k)
        parts.append(s[k:end])
        k = end
    if not parts:
        return None
    lits = [one_literal(p) for p in parts]
    if any(l is None for l in lits):
        return None
    if len(lits) > 1 and any(l.holes for l in lits):
        return None  # $"..{x}" + "..": a plain string at run time, the table can't key it
    if len(lits) == 1:
        return lits[0]
    return Lit(''.join(l.text for l in lits), 0)


def one_literal(p):
    j = 0
    while p[j] in '$@':
        j += 1
    prefix = p[:j]
    if p.startswith('"""', j):
        return None
    interp, verbatim = '$' in prefix, '@' in prefix
    body = p[j + 1:-1]
    out = io.StringIO()
    holes = 0
    k = 0
    while k < len(body):
        c = body[k]
        if c == '\\' and not verbatim:
            ch, k = escape_seq(body, k)
            out.write('{{' if interp and ch == '{' else '}}' if interp and ch == '}' else ch)
        elif verbatim and c == '"':
            out.write('"')
            k += 2
        elif interp and c == '{' and body[k + 1] == '{':
            out.write('{{')
            k += 2
        elif interp and c == '}' and body[k + 1:k + 2] == '}':
            out.write('}}')
            k += 2
        elif interp and c == '{':
            end = skip_expr(body, k + 1, '}')
            hole = body[k + 1:end]
            # alignment and format: the first top-level ',' and ':' inside the hole
            fmt = align = None
            colon = top_level(hole, ':')
            if colon is not None:
                fmt = hole[colon + 1:]
                hole = hole[:colon]
            comma = top_level(hole, ',')
            if comma is not None:
                align = hole[comma + 1:].strip()
            out.write('{%d%s%s}' % (holes, ',' + align if align else '', ':' + fmt if fmt else ''))
            holes += 1
            k = end + 1
        else:
            out.write(c)
            k += 1
    text = out.getvalue()
    if interp and holes == 0:
        text = text.replace('{{', '{').replace('}}', '}')  # a constant: the plain overload
    return Lit(text, holes)


def top_level(s, ch):
    depth = 0
    k = 0
    while k < len(s):
        c = s[k]
        if c in '"\'':
            k = skip_string(s, k)
            continue
        if c in '([{':
            depth += 1
        elif c in ')]}':
            depth -= 1
        elif c == ch and depth == 0:
            return k
        k += 1
    return None


CALL = re.compile(r'(?<![\w.])(?:Lang\.)?Tr\(')
# names kept as data, shown through Tr(x.Ko, x.En): rows written new("key", "한국어", "English", ...)
DATA = ('Ai/QuickActions.cs', 'App/Engine/Engine.System.cs', 'Search/WindowLayout.cs')
ROW = re.compile(r'\bnew\(\s*"[^"]*",\s*"')


def code_calls():
    """(file, line, Korean Lit or None, English Lit or None, English source) for every Tr call, in code order."""
    calls = []
    for dirpath, dirs, files in os.walk(SRC):
        dirs[:] = sorted(d for d in dirs if d not in ('obj', 'bin'))
        for name in sorted(files):
            if not name.endswith('.cs') or name == 'Lang.cs':
                continue
            path = os.path.join(dirpath, name)
            s = open(path, encoding='utf-8-sig').read()
            k = 0
            while True:
                m = find_call(s, k)
                if m is None:
                    break
                start = m
                a = s.index('(', start) + 1
                comma = skip_expr(s, a, ',)')
                if s[comma] != ',':
                    k = comma
                    continue
                end = skip_expr(s, comma + 1, ')')
                ko_src, en_src = s[a:comma], s[comma + 1:end]
                line = s.count('\n', 0, start) + 1
                rel = rel_of(path)
                calls.append((rel, line, parse_literal(ko_src), parse_literal(en_src), en_src.strip()))
                k = a
            if rel_of(path) in DATA:
                for m in ROW.finditer(s):
                    a = m.end() - 1
                    c1 = skip_expr(s, a, ',)')
                    c2 = skip_expr(s, c1 + 1, ',)')
                    ko, en = parse_literal(s[a:c1]), parse_literal(s[c1 + 1:c2])
                    if ko and en and re.search('[가-힣]', ko.text):
                        calls.append((rel_of(path), s.count('\n', 0, a) + 1, ko, en, ''))
    return calls


def rel_of(path):
    return os.path.relpath(path, SRC).replace('\\', '/')


def find_call(s, k):
    """The next Tr( outside strings and comments, from k."""
    while k < len(s):
        c = s[k]
        if c in '"\'' or (c in '$@' and s[k + 1:k + 2] in ('"', '$', '@')):
            k = skip_string(s, k)
            continue
        if s.startswith('//', k):
            k = s.find('\n', k)
            if k < 0:
                return None
            continue
        if s.startswith('/*', k):
            k = s.index('*/', k) + 2
            continue
        m = CALL.match(s, k)
        if m and (k == 0 or not (s[k - 1].isalnum() or s[k - 1] in '_.')):
            return k
        k += 1
    return None


def keys():
    """The English keys in code order, each with its Korean text and first place."""
    seen = {}
    dynamic = []
    for rel, line, ko, en, en_src in code_calls():
        if en is None:
            dynamic.append('%s:%d  %s' % (rel, line, en_src[:90]))
            continue
        if en.text not in seen:
            seen[en.text] = (ko.text if ko else '?', '%s:%d' % (rel, line))
    return seen, dynamic


def esc(s):
    return s.replace('\\', '\\\\').replace('\t', '\\t').replace('\n', '\\n').replace('\r', '\\r')


def unesc(s):
    out = io.StringIO()
    k = 0
    while k < len(s):
        if s[k] == '\\' and k + 1 < len(s):
            out.write({'n': '\n', 't': '\t', 'r': '\r', '\\': '\\'}.get(s[k + 1], s[k + 1]))
            k += 2
        else:
            out.write(s[k])
            k += 1
    return out.getvalue()


def table(code):
    path = os.path.join(TABLES, code + '.tsv')
    rows = {}
    if os.path.exists(path):
        for raw in open(path, encoding='utf-8').read().split('\n'):
            if not raw or raw.startswith('#'):
                continue
            key, _, value = raw.partition('\t')
            rows[unesc(key)] = unesc(value)
    return rows


def holes(s):
    return sorted(re.findall(r'(?<!\{)\{(\d+)', s.replace('{{', '')))


def main(argv):
    sys.stdout.reconfigure(encoding='utf-8')
    cmd = argv[1] if len(argv) > 1 else 'check'
    found, dynamic = keys()
    if cmd == 'check':
        print('%d keys in the code' % len(found))
        for code in CODES:
            t = table(code)
            missing = [k for k in found if k not in t]
            stale = [k for k in t if k not in found]
            bad = [k for k in t if k in found and not set(holes(t[k])) <= set(holes(k))]  # a translation may drop a hole (an English plural 's')
            print('%s: %d translated, %d missing, %d stale, %d with holes the key lacks' % (code, len(t) - len(stale), len(missing), len(stale), len(bad)))
            for k in bad[:20]:
                print('  holes differ: %r -> %r' % (k, t[k]))
        print('%d Tr calls with an English text the tables cannot key:' % len(dynamic))
        for d in dynamic:
            print('  ' + d)
    elif cmd == 'todo':
        code = argv[2]
        n = int(argv[3]) if len(argv) > 3 else None
        t = table(code)
        todo = [(k, v) for k, v in found.items() if k not in t]
        for k, (ko, where) in todo[:n]:
            print('%s\t%s' % (esc(k), esc(ko)))
    elif cmd == 'tidy':
        for code in CODES:
            t = table(code)
            lines = ['%s\t%s' % (esc(k), esc(t[k])) for k in found if k in t]
            open(os.path.join(TABLES, code + '.tsv'), 'w', encoding='utf-8', newline='\n').write('\n'.join(lines) + '\n')
            print('%s: %d rows' % (code, len(lines)))


if __name__ == '__main__':
    main(sys.argv)
