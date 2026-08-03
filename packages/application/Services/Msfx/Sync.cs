using PacToolkits.Application.Abstractions;
using PacToolkits.Application.TextSearch;

namespace PacToolkits.Application.Services.Msfx;

/// <summary>
/// MSFX 同步用例：游标/看板/映射批处理/注入任务（编排走 AutoRun + 窄 Repo）
/// </summary>
public sealed partial class SyncService : ISyncService
{
    private readonly IMsfxPullRepo _pull;
    private readonly IMsfxMappingRepo _mapping;
    private readonly IMsfxInjectRepo _inject;
    private readonly IMsfxQueryRepo _query;
    private readonly IPinyinSearchCatalogCache _catalogCache;
    private readonly PinyinExpansionCache _expansionCache = new();

    public SyncService(
        IMsfxPullRepo pull,
        IMsfxMappingRepo mapping,
        IMsfxInjectRepo inject,
        IMsfxQueryRepo query,
        IPinyinSearchCatalogCache catalogCache)
    {
        _pull = pull ?? throw new ArgumentNullException(nameof(pull));
        _mapping = mapping ?? throw new ArgumentNullException(nameof(mapping));
        _inject = inject ?? throw new ArgumentNullException(nameof(inject));
        _query = query ?? throw new ArgumentNullException(nameof(query));
        _catalogCache = catalogCache ?? throw new ArgumentNullException(nameof(catalogCache));
    }

    private static void RequireSourceApi(string sourceApi)
        => RequireText(sourceApi, nameof(sourceApi));

    private static void RequirePositiveId(long value, string parameterName)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "Identifier must be positive.");
        }
    }

    private static void RequireText(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value is required.", parameterName);
        }
    }

    private static int NormalizeLimit(int limit)
        => limit <= 0 ? 1 : Math.Min(limit, 50000);
}
