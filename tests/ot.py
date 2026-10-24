"""Text operations (ot.js algorithm) on UTF-16 code units, for the tests."""


def normalize(ops):
    out = []
    for c in ops:
        if c == 0 or c == "":
            continue
        if out and isinstance(c, str) and isinstance(out[-1], str):
            out[-1] += c
        elif out and isinstance(c, int) and isinstance(out[-1], int) and (c > 0) == (out[-1] > 0):
            out[-1] += c
        else:
            out.append(c)
    return out


def u16(s):
    return len(s.encode("utf-16-le")) // 2


def base_length(op):
    return sum(abs(c) for c in op if isinstance(c, int))


def apply(text, op):
    units = text.encode("utf-16-le")
    out, pos = [], 0
    for c in op:
        if isinstance(c, str):
            out.append(c.encode("utf-16-le"))
        elif c > 0:
            out.append(units[2 * pos:2 * (pos + c)])
            pos += c
        else:
            pos -= c
    assert 2 * pos == len(units), (pos, len(units) // 2, op)
    return b"".join(out).decode("utf-16-le")


def transform(a, b):
    """(a', b') with apply(apply(d, a), b') == apply(apply(d, b), a'). a's inserts go first."""
    assert base_length(a) == base_length(b)
    ap, bp = [], []
    a, b = list(a), list(b)
    i = j = 0
    x = a[0] if a else None
    y = b[0] if b else None
    while x is not None or y is not None:
        if isinstance(x, str):
            ap.append(x); bp.append(u16(x))
            i += 1; x = a[i] if i < len(a) else None
            continue
        if isinstance(y, str):
            ap.append(u16(y)); bp.append(y)
            j += 1; y = b[j] if j < len(b) else None
            continue
        m = min(abs(x), abs(y))
        if x > 0 and y > 0:
            ap.append(m); bp.append(m)
        elif x < 0 and y > 0:
            ap.append(-m)
        elif x > 0 and y < 0:
            bp.append(-m)
        x = x - m if x > 0 else x + m
        y = y - m if y > 0 else y + m
        if x == 0:
            i += 1; x = a[i] if i < len(a) else None
        if y == 0:
            j += 1; y = b[j] if j < len(b) else None
    return normalize(ap), normalize(bp)


class OtDocument:
    """Client side of one open file: one edit in flight, later ones pending."""

    def __init__(self, text, rev):
        self.text = text
        self.rev = rev
        self.in_flight = None
        self.pending = []

    def local_edit(self, op):
        self.text = apply(self.text, op)
        if self.in_flight is None:
            self.in_flight = op
            return op      # send now
        self.pending.append(op)
        return None

    def ack(self, rev):
        self.rev = rev
        self.in_flight = None
        if self.pending:
            self.in_flight = self.pending.pop(0)
            return self.in_flight
        return None

    def remote(self, op, rev):
        self.rev = rev
        if self.in_flight is not None:
            self.in_flight, op = transform(self.in_flight, op)
            for k in range(len(self.pending)):
                self.pending[k], op = transform(self.pending[k], op)
        self.text = apply(self.text, op)
