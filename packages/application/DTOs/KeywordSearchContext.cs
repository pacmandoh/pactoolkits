namespace PacToolkits.Application.DTOs;

public sealed record KeywordSearchContext(
    string Keyword,
    IReadOnlyList<string> PinyinDrugIds,
    IReadOnlyList<string> PinyinSpecs)
{
    public static KeywordSearchContext Plain(string? keyword)
    {
        var kw = (keyword ?? string.Empty).Trim();
        return new KeywordSearchContext(kw, [], []);
    }
}
