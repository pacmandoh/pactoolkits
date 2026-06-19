using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;

namespace PacToolkits.Application.TextSearch;

public static class PinyinCatalogExpander
{
    public static async Task<KeywordSearchContext> ExpandDrugKeywordAsync(
        IPinyinSearchCatalogCache catalogCache,
        string? keyword,
        CancellationToken ct)
    {
        var kw = (keyword ?? string.Empty).Trim();
        if (kw.Length == 0 || !TextSearchHelper.LooksLikePinyinQuery(kw))
        {
            return KeywordSearchContext.Plain(kw);
        }

        var rows = await catalogCache.GetCatalogRowsAsync(ct).ConfigureAwait(false);
        return ExpandDrugKeyword(rows, kw);
    }

    public static KeywordSearchContext ExpandDrugKeyword(
        IReadOnlyList<DrugIndexDto> catalog,
        string? keyword)
    {
        var kw = (keyword ?? string.Empty).Trim();
        if (kw.Length == 0 || !TextSearchHelper.LooksLikePinyinQuery(kw))
        {
            return KeywordSearchContext.Plain(kw);
        }

        var drugIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var specs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in catalog)
        {
            if (PinyinInitialMatcher.IsMatch(kw, row.DrugId))
            {
                drugIds.Add(row.DrugId);
            }

            if (PinyinInitialMatcher.IsMatch(kw, row.Spec))
            {
                specs.Add(row.Spec);
            }
        }

        return new KeywordSearchContext(kw, drugIds.ToArray(), specs.ToArray());
    }
}
