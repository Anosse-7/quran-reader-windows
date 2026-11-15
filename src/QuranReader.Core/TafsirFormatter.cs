using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace QuranReader.Core;

public sealed record TafsirSpan(string Text, bool IsAyah);

/// <summary>Only span class=aya is styled. Other tags remain literal text; nothing executes.</summary>
public static partial class TafsirFormatter
{
    [GeneratedRegex("<span\\s+class=[\"']aya[\"']\\s*>(.*?)</span>", RegexOptions.Singleline | RegexOptions.CultureInvariant, 100)]
    private static partial Regex AyahSpan();

    public static IReadOnlyList<TafsirSpan> Parse(string markup)
    {
        var result = new List<TafsirSpan>();
        var start = 0;
        foreach (Match match in AyahSpan().Matches(markup))
        {
            if (match.Index > start)
                result.Add(new(WebUtility.HtmlDecode(markup[start..match.Index]), false));
            result.Add(new(WebUtility.HtmlDecode(match.Groups[1].Value), true));
            start = match.Index + match.Length;
        }
        if (start < markup.Length)
            result.Add(new(WebUtility.HtmlDecode(markup[start..]), false));
        return result;
    }
}
