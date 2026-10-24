import io, collections, sys

path = sys.argv[1] if len(sys.argv) > 1 else 'sample.log'
sent = {}
dur = collections.defaultdict(list)

def sec(t):
    h, m, s = t.split(':')
    return int(h) * 3600 + int(m) * 60 + float(s)

for line in io.open(path, encoding='utf-8', errors='ignore'):
    parts = line.split(' ', 3)
    if len(parts) < 4:
        continue
    ts = parts[0]
    rest = line
    if 'CommandTransport.Submit' not in rest:
        continue
    try:
        op = rest.split('op=', 1)[1].split(',', 1)[0].split(' ', 1)[0]
    except IndexError:
        continue
    if 'type=' in rest:
        typ = rest.split('type=', 1)[1].split(',', 1)[0].strip()
        sent[op] = (sec(ts), typ)
    elif op in sent:
        t0, typ = sent.pop(op)
        dur[typ].append((sec(ts) - t0) * 1000.0)

rows = []
for typ, v in dur.items():
    v.sort()
    n = len(v)
    q = lambda p: v[min(n - 1, int(p * n))]
    rows.append((q(.5), typ, n, q(.5), q(.95), q(.99), v[-1]))

for p50, typ, n, a, b, c, mx in sorted(rows, reverse=True):
    print(f'{typ:32s} n={n:5d}  p50={a:7.0f}  p95={b:7.0f}  p99={c:7.0f}  max={mx:7.0f}')
