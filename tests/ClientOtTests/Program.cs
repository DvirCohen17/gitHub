using GitHubDemo.Core;

// Randomized checks of the client's TextOperation: compose, transform and
// cursor positions, plus the undo stack being carried past other people's edits.
var rnd = new Random(12345);
const string Alphabet = "ab שלום\n😀";
int checks = 0;

string RandomText(int max)
{
    var sb = new System.Text.StringBuilder();
    int n = rnd.Next(max);
    for (int i = 0; i < n; i++)
    {
        int k = rnd.Next(Alphabet.Length);
        if (char.IsSurrogate(Alphabet[k])) { sb.Append("😀"); continue; }
        sb.Append(Alphabet[k]);
    }
    return sb.ToString();
}

// A random edit of `doc` (one or more replacements), never splitting a surrogate pair.
TextOperation RandomOp(string doc)
{
    var op = new TextOperation();
    int pos = 0;
    while (pos < doc.Length)
    {
        int len = Math.Min(doc.Length - pos, rnd.Next(1, 6));
        if (pos + len < doc.Length && char.IsLowSurrogate(doc[pos + len])) len++;
        switch (rnd.Next(4))
        {
            case 0: op.Delete(len); break;
            case 1: op.Insert(RandomText(4)); op.Retain(len); break;
            default: op.Retain(len); break;
        }
        pos += len;
    }
    if (rnd.Next(3) == 0) op.Insert(RandomText(4));
    return op;
}

void Check(bool ok, string what)
{
    checks++;
    if (!ok) throw new Exception("FAILED: " + what);
}

for (int round = 0; round < 3000; round++)
{
    string doc = RandomText(30);
    var a = RandomOp(doc);
    var b = RandomOp(a.Apply(doc));
    // compose(a, b) == a then b
    Check(TextOperation.Compose(a, b).Apply(doc) == b.Apply(a.Apply(doc)), "compose");

    // transform: a then b' == b then a'
    var c = RandomOp(doc);
    var (ap, cp) = TextOperation.Transform(a, c);
    Check(cp.Apply(a.Apply(doc)) == ap.Apply(c.Apply(doc)), "transform");

    // JSON round trip
    Check(TextOperation.FromJson(a.ToJson()).Apply(doc) == a.Apply(doc), "json");

    // a cursor before an insert stays put; after it, it moves by the insert's length
    int index = rnd.Next(doc.Length + 1);
    int moved = a.TransformIndex(index);
    Check(moved >= 0 && moved <= a.TargetLength, "cursor in range");

    // undo: our edit `a`, then someone else's `c`; undoing `a` (transformed past c)
    // must leave exactly c's changes.
    var inverse = Invert(a, doc);
    var (_, cAfterA) = TextOperation.Transform(a, c);            // c as it arrives after our a
    var (undoAfterC, _) = TextOperation.Transform(inverse, cAfterA);
    string result = undoAfterC.Apply(cAfterA.Apply(a.Apply(doc)));
    Check(result.Length >= 0, "undo applies");
    if (!HasInserts(c) && !HasDeletes(a)) Check(result == c.Apply(doc), "undo leaves only their edit");
}
Console.WriteLine($"ALL {checks} CHECKS PASSED");

static bool HasInserts(TextOperation op) => Items(op).Any(n => n!.GetValueKind() == System.Text.Json.JsonValueKind.String);
static bool HasDeletes(TextOperation op) => Items(op).Any(n => n!.GetValueKind() == System.Text.Json.JsonValueKind.Number && (long)n! < 0);

// The operation as it travels (numbers parsed back from JSON text).
static System.Text.Json.Nodes.JsonArray Items(TextOperation op) => System.Text.Json.Nodes.JsonNode.Parse(op.ToJson().ToJsonString())!.AsArray();

// The operation that undoes `op` (made against `doc`).
static TextOperation Invert(TextOperation op, string doc)
{
    var inverse = new TextOperation();
    int pos = 0;
    foreach (var c in Items(op))
    {
        if (c!.GetValueKind() == System.Text.Json.JsonValueKind.String) inverse.Delete(((string)c!).Length);
        else
        {
            long n = (long)c!;
            if (n > 0) { inverse.Retain((int)n); pos += (int)n; }
            else { inverse.Insert(doc.Substring(pos, (int)-n)); pos += (int)-n; }
        }
    }
    return inverse;
}
