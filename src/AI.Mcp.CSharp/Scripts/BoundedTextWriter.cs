namespace AI.Mcp.CSharp.Scripts;

using System.Text;

/// <summary>
/// Collects what the script writes to a standard stream, keeping at most <c>limit</c> characters.
/// The rest of the stream is written off rather than remembered, so a runaway <c>while (true)
/// Console.WriteLine()</c> does not turn into an out-of-memory failure.
/// </summary>
internal sealed class BoundedTextWriter(int limit) : TextWriter
{
    private readonly StringBuilder _text = new();

    public bool Truncated { get; private set; }

    public override Encoding Encoding => Encoding.UTF8;

    public override string ToString() => _text.ToString();

    public override void Write(char value)
    {
        if (_text.Length < limit) _text.Append(value);
        else Truncated = true;
    }

    public override void Write(string? value)
    {
        if (string.IsNullOrEmpty(value)) return;
        var keep = Math.Min(value.Length, limit - _text.Length);
        _text.Append(value, 0, keep);
        Truncated |= keep != value.Length;
    }

    public override void Write(ReadOnlySpan<char> buffer)
    {
        var keep = Math.Min(buffer.Length, limit - _text.Length);
        _text.Append(buffer[..keep]);
        Truncated |= keep != buffer.Length;
    }

    public override void Write(char[] buffer, int index, int count) =>
        Write(buffer.AsSpan(index, count));
}
