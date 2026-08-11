using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using global::Avalonia.Threading;
using PacToolkits.Application.Abstractions;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure.Navigation;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure.Notifications;
using PacToolkits.Desktop.Avalonia.Services.Workspace.Refresh;
using PacToolkits.Desktop.Avalonia.Ui.State;

namespace PacToolkits.Desktop.Avalonia.ViewModels.Pages;

public sealed partial class Dashboard
{
    // 单测构造：不触发自动 Initialize
    internal Dashboard(IDashboardService dashboard, ILookupCatalogService? lookup = null)
    {
        _dashboard = dashboard;
        _lookup = lookup ?? NoopLookup.Instance;
        _toast = NoopToast.Instance;
        _clientAlias = NoopClientAlias.Instance;
        _nav = new PageNavigationService();
        _inventoryOverview = null!;
        _dirtyRefresh = new WorkspaceDirtyRefresh();
        _dateRangeController = new RollingDateRangeController(() =>
            PostOnUi(HandleDateRangeDayChanged, DispatcherPriority.Background));

        Clients.Clear();
        Clients.Add(AllClients);

        DrugOptions.Clear();
        _drugCatalog = [];

        SpecOptions.Clear();
        SpecOptions.Add(AllSpec);

        using (SuppressReload())
        {
            var normalized = RollingDateRangeController.Normalize(FromDate, ToDate);
            FromDate = normalized.From;
            ToDate = normalized.To;
            DrugText = null;
            SelectedSpec = AllSpec;
            SelectedClient = AllClients;
            TrendMode = TrendModes.FirstOrDefault();
            ClientMetricMode = ClientMetricModes.FirstOrDefault();
            TxnPanelMode = TxnPanelModes.FirstOrDefault();
        }
    }

    internal Task TestInitializeAsync() => InitializeAsync();

    private sealed class NoopToast : IToastService
    {
        public static readonly NoopToast Instance = new();

        public void Success(string title, string message)
        {
        }

        public void Error(string title, string message)
        {
        }

        public void Warn(string title, string message)
        {
        }

        public void Info(string title, string message)
        {
        }
    }

    private sealed class NoopClientAlias : IClientAliasService
    {
        public static readonly NoopClientAlias Instance = new();

        public event Action? Changed
        {
            add { }
            remove { }
        }

        public IReadOnlyDictionary<string, string> GetAll() => new Dictionary<string, string>();

        public string Resolve(string? machine) => machine ?? string.Empty;

        public void ReplaceAll(IEnumerable<KeyValuePair<string, string>> items)
        {
        }

        public void Apply(IReadOnlyDictionary<string, string> aliases)
        {
        }

        public void Reload()
        {
        }
    }

    private sealed class NoopLookup : ILookupCatalogService
    {
        public static readonly NoopLookup Instance = new();

        public Task<IReadOnlyList<string>> GetDrugIdsAsync(CancellationToken ct, bool forceRefresh = false)
            => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());

        public Task<IReadOnlyList<string>> GetSpecsByDrugAsync(
            string drugId,
            CancellationToken ct,
            bool forceRefresh = false)
            => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());

        public Task<string?> ResolveCanonicalDrugIdAsync(
            string? input,
            CancellationToken ct,
            bool forceRefresh = false)
            => Task.FromResult<string?>(null);

        public Task<int?> GetQtyAsync(
            string? drugId,
            string? spec,
            CancellationToken ct,
            bool forceRefresh = false)
            => Task.FromResult<int?>(null);

        public Task<bool> IsDeprecatedDrugIdAsync(
            string? drugId,
            CancellationToken ct,
            bool forceRefresh = false)
            => Task.FromResult(false);

        public void InvalidateDrugCatalog()
        {
        }
    }
}
