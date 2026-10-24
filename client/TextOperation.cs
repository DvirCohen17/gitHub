using System;
using System.Collections.Generic;
using System.Text;

namespace client_side
{
    // A change to a text document, used to keep everyone editing a file in sync
    // (operational transformation, same algorithm as ot.js and server/TextOperation.cpp).
    // An operation walks the whole document: retain n chars, delete n chars, or insert text.
    // Wire format: "r<n>," retain, "d<n>," delete, "i<utf8 bytes>:<text>" insert.
    public sealed class TextOperation
    {
        private enum Kind { Retain, Insert, Delete }

        private struct Component
        {
            public Kind Kind;
            public int Count;     // Retain / Delete
            public string Text;   // Insert
        }

        private readonly List<Component> ops = new List<Component>();

        public int BaseLength { get; private set; }
        public int TargetLength { get; private set; }

        public TextOperation Retain(int n)
        {
            if (n <= 0) return this;
            BaseLength += n;
            TargetLength += n;
            if (ops.Count > 0 && ops[^1].Kind == Kind.Retain)
                ops[^1] = new Component { Kind = Kind.Retain, Count = ops[^1].Count + n };
            else
                ops.Add(new Component { Kind = Kind.Retain, Count = n });
            return this;
        }

        public TextOperation Insert(string s)
        {
            if (string.IsNullOrEmpty(s)) return this;
            TargetLength += s.Length;
            if (ops.Count > 0 && ops[^1].Kind == Kind.Insert)
            {
                ops[^1] = new Component { Kind = Kind.Insert, Text = ops[^1].Text + s };
            }
            else if (ops.Count > 0 && ops[^1].Kind == Kind.Delete)
            {
                // canonical form: inserts come before deletes at the same position
                if (ops.Count >= 2 && ops[^2].Kind == Kind.Insert)
                    ops[^2] = new Component { Kind = Kind.Insert, Text = ops[^2].Text + s };
                else
                    ops.Insert(ops.Count - 1, new Component { Kind = Kind.Insert, Text = s });
            }
            else
            {
                ops.Add(new Component { Kind = Kind.Insert, Text = s });
            }
            return this;
        }

        public TextOperation Delete(int n)
        {
            if (n <= 0) return this;
            BaseLength += n;
            if (ops.Count > 0 && ops[^1].Kind == Kind.Delete)
                ops[^1] = new Component { Kind = Kind.Delete, Count = ops[^1].Count + n };
            else
                ops.Add(new Component { Kind = Kind.Delete, Count = n });
            return this;
        }

        // An editor change: replace `removed` chars at `offset` with `inserted`.
        public static TextOperation FromChange(int documentLengthBefore, int offset, int removed, string inserted)
        {
            return new TextOperation()
                .Retain(offset)
                .Delete(removed)
                .Insert(inserted)
                .Retain(documentLengthBefore - offset - removed);
        }

        public string Apply(string document)
        {
            if (document.Length != BaseLength)
                throw new InvalidOperationException("Operation does not match the document.");
            var sb = new StringBuilder(TargetLength);
            int pos = 0;
            foreach (var c in ops)
            {
                switch (c.Kind)
                {
                    case Kind.Retain: sb.Append(document, pos, c.Count); pos += c.Count; break;
                    case Kind.Insert: sb.Append(c.Text); break;
                    case Kind.Delete: pos += c.Count; break;
                }
            }
            return sb.ToString();
        }

        // Applies the operation to an editor document as individual replacements,
        // so carets, selections and scroll position of the local user are kept.
        public void ApplyTo(ICSharpCode.AvalonEdit.Document.TextDocument document)
        {
            if (document.TextLength != BaseLength)
                throw new InvalidOperationException("Operation does not match the document.");
            document.BeginUpdate();
            try
            {
                int pos = 0;
                foreach (var c in ops)
                {
                    switch (c.Kind)
                    {
                        case Kind.Retain: pos += c.Count; break;
                        case Kind.Insert: document.Insert(pos, c.Text); pos += c.Text.Length; break;
                        case Kind.Delete: document.Remove(pos, c.Count); break;
                    }
                }
            }
            finally
            {
                document.EndUpdate();
            }
        }

        public string Encode()
        {
            var sb = new StringBuilder();
            foreach (var c in ops)
            {
                switch (c.Kind)
                {
                    case Kind.Retain: sb.Append('r').Append(c.Count).Append(','); break;
                    case Kind.Delete: sb.Append('d').Append(c.Count).Append(','); break;
                    case Kind.Insert: sb.Append('i').Append(Encoding.UTF8.GetByteCount(c.Text)).Append(':').Append(c.Text); break;
                }
            }
            return sb.ToString();
        }

        public static TextOperation Decode(string wire)
        {
            byte[] b = Encoding.UTF8.GetBytes(wire);
            var op = new TextOperation();
            int i = 0;
            while (i < b.Length)
            {
                char kind = (char)b[i++];
                if (kind == 'i')
                {
                    int colon = Array.IndexOf(b, (byte)':', i);
                    int n = int.Parse(Encoding.ASCII.GetString(b, i, colon - i));
                    op.Insert(Encoding.UTF8.GetString(b, colon + 1, n));
                    i = colon + 1 + n;
                }
                else
                {
                    int comma = Array.IndexOf(b, (byte)',', i);
                    int n = int.Parse(Encoding.ASCII.GetString(b, i, comma - i));
                    i = comma + 1;
                    if (kind == 'r') op.Retain(n);
                    else if (kind == 'd') op.Delete(n);
                    else throw new FormatException("Malformed operation.");
                }
            }
            return op;
        }

        // a and b were made against the same document. Returns (a', b') such that
        // applying a then b' equals applying b then a'. a's insertion wins ties.
        public static (TextOperation, TextOperation) Transform(TextOperation a, TextOperation b)
        {
            if (a.BaseLength != b.BaseLength)
                throw new InvalidOperationException("Operations were made against different documents.");

            var ap = new TextOperation();
            var bp = new TextOperation();
            int i = 0, j = 0;
            Component? x = a.ops.Count > 0 ? a.ops[0] : null;
            Component? y = b.ops.Count > 0 ? b.ops[0] : null;

            while (x != null || y != null)
            {
                if (x is { Kind: Kind.Insert } xi)
                {
                    ap.Insert(xi.Text); bp.Retain(xi.Text.Length);
                    x = ++i < a.ops.Count ? a.ops[i] : null;
                    continue;
                }
                if (y is { Kind: Kind.Insert } yi)
                {
                    ap.Retain(yi.Text.Length); bp.Insert(yi.Text);
                    y = ++j < b.ops.Count ? b.ops[j] : null;
                    continue;
                }
                if (x == null || y == null)
                    throw new InvalidOperationException("Operations have different lengths.");

                Component cx = x.Value, cy = y.Value;
                int m = Math.Min(cx.Count, cy.Count);
                if (cx.Kind == Kind.Retain && cy.Kind == Kind.Retain) { ap.Retain(m); bp.Retain(m); }
                else if (cx.Kind == Kind.Delete && cy.Kind == Kind.Retain) { ap.Delete(m); }
                else if (cx.Kind == Kind.Retain && cy.Kind == Kind.Delete) { bp.Delete(m); }
                // both deleted the same text: nothing to do

                cx.Count -= m; cy.Count -= m;
                x = cx.Count > 0 ? cx : (++i < a.ops.Count ? a.ops[i] : null);
                y = cy.Count > 0 ? cy : (++j < b.ops.Count ? b.ops[j] : null);
            }
            return (ap, bp);
        }
    }
}
