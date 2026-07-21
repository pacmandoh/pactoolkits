namespace PacToolkits.Application.TextSearch;

/// <summary>
/// 关键字检索上下文（原文 + 拼音展开出的药品/规格）
/// </summary>
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
