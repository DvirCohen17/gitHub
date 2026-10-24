using System.Text;
using System.Text.Json.Nodes;

namespace GitHubDemo.Core;

// A change to a text document (operational transformation, same algorithm as
// ot.js and server/TextOperation.cpp). An operation walks the whole document:
// retain n characters, delete n characters, or insert text. Positions are UTF-16
// code units, the same on both sides.
// JSON form: a positive number retains, a negative number deletes, a string inserts.
public sealed class TextOperation
{
    private enum Kind { Retain, Insert, Delete }

    private struct Component
    {
        public Kind Kind;
        public int Count;     // Retain / Delete
        public string Text;   // Insert
    }

    private readonly List<Component> _ops = new();

    public int BaseLength { get; private set; }
    public int TargetLength { get; private set; }
    public bool IsNoop => _ops.Count == 0 || (_ops.Count == 1 && _ops[0].Kind == Kind.Retain);

    public TextOperation Retain(int n)
    {
        if (n <= 0) return this;
        BaseLength += n;
        TargetLength += n;
        if (_ops.Count > 0 && _ops[^1].Kind == Kind.Retain)
            _ops[^1] = new Component { Kind = Kind.Retain, Count = _ops[^1].Count + n };
        else
            _ops.Add(new Component { Kind = Kind.Retain, Count = n });
        return this;
    }

    public TextOperation Insert(string s)
    {
        if (string.IsNullOrEmpty(s)) return this;
        TargetLength += s.Length;
        if (_ops.Count > 0 && _ops[^1].Kind == Kind.Insert)
        {
            _ops[^1] = new Component { Kind = Kind.Insert, Text = _ops[^1].Text + s };
        }
        else if (_ops.Count > 0 && _ops[^1].Kind == Kind.Delete)
        {
            // canonical form: inserts come before deletes at the same position
            if (_ops.Count >= 2 && _ops[^2].Kind == Kind.Insert)
                _ops[^2] = new Component { Kind = Kind.Insert, Text = _ops[^2].Text + s };
            else
                _ops.Insert(_ops.Count - 1, new Component { Kind = Kind.Insert, Text = s });
        }
        else
        {
            _ops.Add(new Component { Kind = Kind.Insert, Text = s });
        }
        return this;
    }

    public TextOperation Delete(int n)
    {
        if (n <= 0) return this;
        BaseLength += n;
        if (_ops.Count > 0 && _ops[^1].Kind == Kind.Delete)
            _ops[^1] = new Component { Kind = Kind.Delete, Count = _ops[^1].Count + n };
        else
            _ops.Add(new Component { Kind = Kind.Delete, Count = n });
        return this;
    }

    // An editor change: replace `removed` characters at `offset` with `inserted`.
    public static TextOperation FromChange(int documentLengthBefore, int offset, int removed, string inserted) =>
        new TextOperation().Retain(offset).Delete(removed).Insert(inserted).Retain(documentLengthBefore - offset - removed);

    public string Apply(string document)
    {
        if (document.Length != BaseLength) throw new InvalidOperationException("Operation does not match the document.");
        var sb = new StringBuilder(TargetLength);
        int pos = 0;
        foreach (var c in _ops)
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

    // Applies the operation to an editor document as individual replacements, so
    // the local user's caret, selection and scroll position are kept.
    public void ApplyTo(ICSharpCode.AvalonEdit.Document.TextDocument document)
    {
        if (document.TextLength != BaseLength) throw new InvalidOperationException("Operation does not match the document.");
        document.BeginUpdate();
        try
        {
            int pos = 0;
            foreach (var c in _ops)
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

    // Where a position in the document before this operation ends up after it.
    public int TransformIndex(int index)
    {
        int newIndex = index, pos = 0;
        foreach (var c in _ops)
        {
            if (pos > index) break;
            switch (c.Kind)
            {
                case Kind.Retain: pos += c.Count; break;
                case Kind.Insert: newIndex += c.Text.Length; break;
                case Kind.Delete: newIndex -= Math.Min(c.Count, index - pos); pos += c.Count; break;
            }
        }
        return newIndex;
    }

    public JsonArray ToJson()
    {
        var array = new JsonArray();
        foreach (var c in _ops)
        {
            switch (c.Kind)
            {
                case Kind.Retain: array.Add(c.Count); break;
                case Kind.Delete: array.Add(-c.Count); break;
                case Kind.Insert: array.Add(c.Text); break;
            }
        }
        return array;
    }

    public static TextOperation FromJson(JsonNode? node)
    {
        if (node is not JsonArray array) throw new FormatException("Malformed operation.");
        var op = new TextOperation();
        foreach (var item in array)
        {
            if (item is JsonValue v && v.TryGetValue(out string? s)) op.Insert(s ?? "");
            else if (item is JsonValue n && n.TryGetValue(out long count))
            {
                if (count > 0) op.Retain((int)count);
                else if (count < 0) op.Delete((int)-count);
            }
            else throw new FormatException("Malformed operation.");
        }
        return op;
    }

    // The single operation that has the same effect as applying a, then b.
    public static TextOperation Compose(TextOperation a, TextOperation b)
    {
        if (a.TargetLength != b.BaseLength) throw new InvalidOperationException("Operations can't be composed.");
        var result = new TextOperation();
        int i = 0, j = 0;
        Component? x = a._ops.Count > 0 ? a._ops[0] : null;
        Component? y = b._ops.Count > 0 ? b._ops[0] : null;
        void NextX() => x = ++i < a._ops.Count ? a._ops[i] : null;
        void NextY() => y = ++j < b._ops.Count ? b._ops[j] : null;

        while (x != null || y != null)
        {
            if (x is { Kind: Kind.Delete } xd) { result.Delete(xd.Count); NextX(); continue; }
            if (y is { Kind: Kind.Insert } yi) { result.Insert(yi.Text); NextY(); continue; }
            if (x == null || y == null) throw new InvalidOperationException("Operations can't be composed.");

            Component cx = x.Value, cy = y.Value;
            int lengthX = cx.Kind == Kind.Insert ? cx.Text.Length : cx.Count;
            if (cx.Kind == Kind.Retain && cy.Kind == Kind.Retain)
            {
                int m = Math.Min(cx.Count, cy.Count);
                result.Retain(m);
                cx.Count -= m; cy.Count -= m;
                x = cx.Count > 0 ? cx : null; if (x == null) NextX();
                y = cy.Count > 0 ? cy : null; if (y == null) NextY();
            }
            else if (cx.Kind == Kind.Insert && cy.Kind == Kind.Delete)
            {
                // inserted, then deleted again: nothing remains
                int m = Math.Min(lengthX, cy.Count);
                cx.Text = cx.Text.Substring(m); cy.Count -= m;
                x = cx.Text.Length > 0 ? cx : null; if (x == null) NextX();
                y = cy.Count > 0 ? cy : null; if (y == null) NextY();
            }
            else if (cx.Kind == Kind.Insert && cy.Kind == Kind.Retain)
            {
                int m = Math.Min(lengthX, cy.Count);
                result.Insert(cx.Text.Substring(0, m));
                cx.Text = cx.Text.Substring(m); cy.Count -= m;
                x = cx.Text.Length > 0 ? cx : null; if (x == null) NextX();
                y = cy.Count > 0 ? cy : null; if (y == null) NextY();
            }
            else // retain, then delete
            {
                int m = Math.Min(cx.Count, cy.Count);
                result.Delete(m);
                cx.Count -= m; cy.Count -= m;
                x = cx.Count > 0 ? cx : null; if (x == null) NextX();
                y = cy.Count > 0 ? cy : null; if (y == null) NextY();
            }
        }
        return result;
    }

    // a and b were made against the same document. Returns (a', b') such that
    // applying a then b' equals applying b then a'. a's insertion wins ties.
    public static (TextOperation, TextOperation) Transform(TextOperation a, TextOperation b)
    {
        if (a.BaseLength != b.BaseLength) throw new InvalidOperationException("Operations were made against different documents.");

        var ap = new TextOperation();
        var bp = new TextOperation();
        int i = 0, j = 0;
        Component? x = a._ops.Count > 0 ? a._ops[0] : null;
        Component? y = b._ops.Count > 0 ? b._ops[0] : null;

        while (x != null || y != null)
        {
            if (x is { Kind: Kind.Insert } xi)
            {
                ap.Insert(xi.Text); bp.Retain(xi.Text.Length);
                x = ++i < a._ops.Count ? a._ops[i] : null;
                continue;
            }
            if (y is { Kind: Kind.Insert } yi)
            {
                ap.Retain(yi.Text.Length); bp.Insert(yi.Text);
                y = ++j < b._ops.Count ? b._ops[j] : null;
                continue;
            }
            if (x == null || y == null) throw new InvalidOperationException("Operations have different lengths.");

            Component cx = x.Value, cy = y.Value;
            int m = Math.Min(cx.Count, cy.Count);
            if (cx.Kind == Kind.Retain && cy.Kind == Kind.Retain) { ap.Retain(m); bp.Retain(m); }
            else if (cx.Kind == Kind.Delete && cy.Kind == Kind.Retain) { ap.Delete(m); }
            else if (cx.Kind == Kind.Retain && cy.Kind == Kind.Delete) { bp.Delete(m); }
            // both deleted the same text: nothing to do

            cx.Count -= m; cy.Count -= m;
            x = cx.Count > 0 ? cx : (++i < a._ops.Count ? a._ops[i] : null);
            y = cy.Count > 0 ? cy : (++j < b._ops.Count ? b._ops[j] : null);
        }
        return (ap, bp);
    }
}
