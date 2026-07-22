using PacToolkits.Application.Abstractions;
using PacToolkits.Application.TextSearch;

namespace PacToolkits.Application.Services.Msfx;

/// <summary>
/// MSFX 同步实现：委托 Repo，并兼任自动跑批 Store
/// </summary>
public sealed partial class SyncService : ISyncService, IMsfxAutoRunStore
{
    private readonly IMsfxSyncRepo _repo;
    private readonly IPinyinSearchCatalogCache _catalogCache;
    private readonly PinyinExpansionCache _expansionCache = new();

    public SyncService(IMsfxSyncRepo repo, IPinyinSearchCatalogCache catalogCache)
    {
        _repo = repo ?? throw new ArgumentNullException(nameof(repo));
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
