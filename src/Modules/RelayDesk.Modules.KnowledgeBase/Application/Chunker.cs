using System.Text;

namespace RelayDesk.Modules.KnowledgeBase.Application;

/// <summary>
/// Splits Markdown into chunks of roughly <see cref="TargetLength"/> characters along paragraph
/// boundaries, keeping the nearest heading with each chunk so it stays meaningful on its own.
/// </summary>
internal static class Chunker
{
    public const int TargetLength = 800;
    public const int MaxLength = 3_500;

    public static IReadOnlyList<string> Split(string markdown)
    {
        var paragraphs = markdown.Replace("\r\n", "\n")
            .Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var chunks = new List<string>();
        var current = new StringBuilder();
        string? heading = null;

        foreach (var paragraph in paragraphs)
        {
            if (paragraph.StartsWith('#'))
            {
                Flush();
                heading = paragraph.TrimStart('#', ' ');
                continue;
            }

            foreach (var piece in HardWrap(paragraph))
            {
                if (current.Length > 0 && current.Length + piece.Length > TargetLength) Flush();
                if (current.Length == 0 && heading is not null) current.Append(heading).Append("\n\n");
                else if (current.Length > 0) current.Append("\n\n");
                current.Append(piece);
            }
        }
        Flush();
        return chunks;

        void Flush()
        {
            if (current.Length > 0) chunks.Add(current.ToString());
            current.Clear();
        }
    }

    /// <summary>A single huge paragraph (e.g. pasted text) is cut at sentence ends so no chunk exceeds the limit.</summary>
    private static IEnumerable<string> HardWrap(string paragraph)
    {
        if (paragraph.Length <= MaxLength) { yield return paragraph; yield break; }
        var start = 0;
        while (start < paragraph.Length)
        {
            var length = Math.Min(TargetLength, paragraph.Length - start);
            var end = start + length;
            if (end < paragraph.Length)
            {
                var stop = paragraph.LastIndexOfAny(['.', '!', '?', '\n'], end - 1, length);
                if (stop > start + TargetLength / 2) end = stop + 1;
            }
            yield return paragraph[start..end].Trim();
            start = end;
        }
    }
}
