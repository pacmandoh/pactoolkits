namespace PacToolkits.Application.TextSearch;

/// <summary>
/// 保存原始关键字及其拼音展开得到的药品和规格检索范围
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
