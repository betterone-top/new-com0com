# Extract the head revision of every RCS ,v file under SRC into DST.
import os, re, sys
src, dst = sys.argv[1], sys.argv[2]
def tok_at(data, pos):
    # parse @...@ string starting at pos (data[pos]=='@')
    out = bytearray(); i = pos + 1
    while True:
        j = data.index(b'@', i)
        out += data[i:j]
        if j + 1 < len(data) and data[j+1:j+2] == b'@':
            out += b'@'; i = j + 2
        else:
            return bytes(out), j + 1
for root, _, files in os.walk(src):
    for f in files:
        if not f.endswith(',v'): continue
        p = os.path.join(root, f); data = open(p, 'rb').read()
        head = re.search(rb'^head\s+([\d.]+);', data, re.M).group(1)
        # skip desc
        d = data.index(b'\ndesc\n'); _, pos = tok_at(data, data.index(b'@', d))
        m = re.compile(rb'\s*' + re.escape(head) + rb'\s+log\s+').match(data, pos)
        _, pos = tok_at(data, m.end())
        pos = data.index(b'text', pos); text, _ = tok_at(data, data.index(b'@', pos))
        rel = os.path.relpath(p, src)[:-2]
        out = os.path.join(dst, rel); os.makedirs(os.path.dirname(out), exist_ok=True)
        open(out, 'wb').write(text)
