using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;
using PacToolkits.Application.Services;
using PacToolkits.Desktop.Avalonia.Common;
using PacToolkits.Desktop.Avalonia.Services.Application;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure;
using PacToolkits.Desktop.Avalonia.ViewModels.Pages;
using ShadUI;

namespace PacToolkits.Desktop.Avalonia.ViewModels.Dialogs;

public sealed partial class MsfxMappingBatchDialogViewModel(
    DialogManager dialogManager,
    ILookupCatalogService lookup,
    IMsfxSyncService syncService,
    IDbAccessGuard accessGuard) : FormDialogViewModelBase(dialogManager), IDisposable
{
    private static readonly string[] SearchScopes =
    [
        "全部字段",
        "最小包装码",
        "单据编码",
        "原始药/规",
        "校正药/规",
        "层级码",
        "映射目标",
        "原因信息"
    ];

    private static readonly TimeSpan LookupTimeout = TimeSpan.FromSeconds(8);

    private const string LogModule = "MsfxMappingBatchDialogVM";

    private static readonly MsfxMappingBatchDialogResult CancelResult = new(
        MsfxMappingBatchDialogAction.Cancel, null, string.Empty, string.Empty);

    private readonly SearchInputDebouncer _keywordDebouncer = new(450);
    private readonly CancellationTokenSource _sessionCts = new();
    private IReadOnlyList<OptionItem> _drugCatalog = [];
    private bool _initialized;
    private bool _isResettingFilters;
    private int _drugInputVersion;
    private readonly MsfxMappingBatchReloadGate _reloadGate = new();
    private Task? _inputCommitTask;
    private bool _isCompleting;

    public required MsfxMappingBatchDialogModel Model { get; init; }

    public MsfxMappingBatchDialogResult? Result { get; private set; }

    public IReadOnlyList<string> MapStatusFilters { get; } = ["ALL", "PENDING", "MAPPED", "NEED_REVIEW", "FAILED"];

    public IReadOnlyList<string> CodeStatusFilters { get; } = ["ALL", "NEW", "TASKED", "FAILED"];

    public IReadOnlyList<string> SearchScopeOptions { get; } = SearchScopes;

    public ObservableCollection<MsfxMappingBatchGroupRow> Groups { get; } = new();

    public ObservableCollection<OptionItem> DrugOptions { get; } = new();

    public ObservableCollection<string> SpecOptions { get; } = new();

    [ObservableProperty] private MsfxMappingBatchGroupRow? _selectedGroup;

    [ObservableProperty] private string _selectedSearchScope = "全部字段";

    [ObservableProperty] private string _selectedMapStatus = "ALL";

    [ObservableProperty] private string _selectedCodeStatus = "ALL";

    [ObservableProperty] private string _keyword = string.Empty;

    [ObservableProperty] private bool _isSearchPanelVisible;

    [ObservableProperty] private string _drugText = string.Empty;

    [ObservableProperty] private string? _selectedSpec;

    [ObservableProperty] private string _previewText = "请选择分组后自动预览";

    public bool SearchPanelClosedIconVisible => !IsSearchPanelVisible;

    public bool SearchPanelOpenIconVisible => IsSearchPanelVisible;

    public bool ShowSpecPlaceholder => string.IsNullOrWhiteSpace(SelectedSpec);

    public string ResolvedDrugId => NormalizeInput(DrugText) ?? string.Empty;

    public string ResolvedSpec => NormalizeInput(SelectedSpec) ?? string.Empty;

    partial void OnIsSearchPanelVisibleChanged(bool value)
    {
        OnPropertyChanged(nameof(SearchPanelClosedIconVisible));
        OnPropertyChanged(nameof(SearchPanelOpenIconVisible));
    }

    partial void OnSelectedSpecChanged(string? value)
    {
        OnPropertyChanged(nameof(ShowSpecPlaceholder));
        if (!_initialized || _isResettingFilters)
        {
            return;
        }

        _ = RefreshPreviewSafeAsync(_sessionCts.Token);
    }

    partial void OnSelectedGroupChanged(MsfxMappingBatchGroupRow? value)
    {
        if (!_initialized)
        {
            return;
        }

        _ = RefreshPreviewSafeAsync(_sessionCts.Token);
    }

    partial void OnSelectedSearchScopeChanged(string value)
        => ScheduleFilterReload();

    partial void OnSelectedMapStatusChanged(string value)
        => ScheduleFilterReload();

    partial void OnSelectedCodeStatusChanged(string value)
        => ScheduleFilterReload();

    partial void OnKeywordChanged(string value)
    {
        if (!_initialized || _isResettingFilters)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            _keywordDebouncer.Cancel();
            _ = ReloadGroupsSafeAsync(_sessionCts.Token);
            return;
        }

        _keywordDebouncer.Schedule(() => ReloadGroupsSafeAsync(_sessionCts.Token));
    }

    partial void OnDrugTextChanged(string value)
    {
        if (_drugCatalog.Count > 0)
        {
            AutoCompleteFilter.RefreshVisibleOptions(DrugOptions, _drugCatalog, value);
        }

        if (!_initialized)
        {
            return;
        }

        _ = OnDrugInputChangedAsync(value);
    }

    public async Task InitializeViewAsync()
    {
        if (_initialized)
        {
            return;
        }

        SelectedSearchScope = ResolveScopeLabel(Model.SearchScope);
        SelectedMapStatus = string.IsNullOrWhiteSpace(Model.MapStatusFilter) ? "ALL" : Model.MapStatusFilter;
        SelectedCodeStatus = string.IsNullOrWhiteSpace(Model.CodeStatusFilter) ? "ALL" : Model.CodeStatusFilter;
        Keyword = Model.Keyword ?? string.Empty;

        if (Model.Groups is { Count: > 0 } groups)
        {
            ReplaceGroups(groups);
        }
        else
        {
            await ReloadGroupsSafeAsync(_sessionCts.Token).ConfigureAwait(true);
        }

        await RefreshDrugCatalogAsync().ConfigureAwait(true);
        await RefreshPreviewAsync(_sessionCts.Token).ConfigureAwait(true);
        _initialized = true;
    }

    [RelayCommand]
    private void ToggleSearchPanel()
        => IsSearchPanelVisible = !IsSearchPanelVisible;

    [RelayCommand]
    private async Task SearchAsync()
    {
        _keywordDebouncer.Cancel();
        await ReloadGroupsSafeAsync(_sessionCts.Token).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task ResetFiltersAsync()
    {
        _isResettingFilters = true;
        try
        {
            _keywordDebouncer.Cancel();
            SelectedSearchScope = "全部字段";
            SelectedMapStatus = "ALL";
            SelectedCodeStatus = "ALL";
            Keyword = string.Empty;
        }
        finally
        {
            _isResettingFilters = false;
        }

        await ReloadGroupsSafeAsync(_sessionCts.Token).ConfigureAwait(true);
    }

    [RelayCommand]
    private Task ApplyDrugCommitAsync()
        => RunInputCommitAsync(async () =>
        {
            var input = NormalizeInput(DrugText);
            if (string.IsNullOrWhiteSpace(input))
            {
                await RefreshPreviewAsync(_sessionCts.Token).ConfigureAwait(true);
                return;
            }

            var version = Interlocked.Increment(ref _drugInputVersion);
            await ApplyDrugAsync(input, version).ConfigureAwait(true);

            var first = SpecOptions.FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(first))
            {
                SelectedSpec = first;
            }

            await RefreshPreviewAsync(_sessionCts.Token).ConfigureAwait(true);
        });

    [RelayCommand]
    private void Close()
    {
        CancelSessionWork();
        Result = CancelResult;
        CloseDialog();
    }

    [RelayCommand]
    private Task ApplyMapAsync()
        => SubmitAsync(MsfxMappingBatchDialogAction.ApplyMap);

    [RelayCommand]
    private Task DiscardTaskAsync()
        => SubmitAsync(MsfxMappingBatchDialogAction.DiscardTask);

    private async Task SubmitAsync(MsfxMappingBatchDialogAction action)
    {
        await FlushPendingInputAsync().ConfigureAwait(true);
        Complete(action);
    }

    private async Task FlushPendingInputAsync()
    {
        if (ApplyDrugCommitCommand.CanExecute(null))
        {
            await ApplyDrugCommitAsync().ConfigureAwait(true);
        }

        var pending = _inputCommitTask;
        if (pending is not null)
        {
            await pending.ConfigureAwait(true);
        }
    }

    public void Dispose()
    {
        CancelSessionWork();
        _sessionCts.Dispose();
        _keywordDebouncer.Dispose();
    }

    private void Complete(MsfxMappingBatchDialogAction action)
    {
        if (_isCompleting)
        {
            return;
        }

        _isCompleting = true;
        try
        {
            CancelSessionWork();
            Result = new MsfxMappingBatchDialogResult(
                action,
                SelectedGroup,
                ResolvedDrugId,
                ResolvedSpec);
            CloseDialog(success: true);
        }
        catch (Exception)
        {
            Result = CancelResult;
            CloseDialog();
        }
        finally
        {
            _isCompleting = false;
        }
    }

    private void ScheduleFilterReload()
    {
        if (!_initialized || _isResettingFilters)
        {
            return;
        }

        _keywordDebouncer.Cancel();
        _ = ReloadGroupsSafeAsync(_sessionCts.Token);
    }

    private void CancelSessionWork()
    {
        _keywordDebouncer.Cancel();
        _reloadGate.Invalidate();
        if (!_sessionCts.IsCancellationRequested)
        {
            _sessionCts.Cancel();
        }
    }

    private async Task ReloadGroupsSafeAsync(CancellationToken ct)
    {
        try
        {
            await ReloadGroupsAsync(ct).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            AppLog.Warn(
                LogModule,
                "msfx.map.batch.reload_fail",
                "Failed to reload mapping batch groups",
                ex);
        }
    }

    private async Task RefreshPreviewSafeAsync(CancellationToken ct)
    {
        try
        {
            await RefreshPreviewAsync(ct).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            AppLog.Warn(
                LogModule,
                "msfx.map.batch.preview_fail",
                "Failed to refresh mapping batch preview",
                ex);
        }
    }

    private async Task ReloadGroupsAsync(CancellationToken ct)
    {
        var epoch = _reloadGate.BeginReload();

        var groups = await syncService.LoadMappingBatchGroupsAsync(
            FilterInput.Norm(SelectedMapStatus),
            FilterInput.Norm(SelectedCodeStatus),
            ResolveSearchScope(SelectedSearchScope),
            NormalizeText(Keyword),
            limit: 500,
            ct: ct).ConfigureAwait(false);

        if (ct.IsCancellationRequested || !_reloadGate.IsCurrent(epoch))
        {
            return;
        }

        await UiThreadHelper.RunOnUiAsync(() => ReplaceGroups(groups)).ConfigureAwait(false);

        if (ct.IsCancellationRequested || !_reloadGate.IsCurrent(epoch))
        {
            return;
        }

        await RefreshPreviewAsync(ct).ConfigureAwait(false);
    }

    private void ReplaceGroups(IReadOnlyList<MsfxMappingBatchGroupRow> groups)
    {
        var previousKey = SelectedGroup is { } previous ? RowKey(previous) : null;

        Groups.Clear();
        foreach (var group in groups)
        {
            Groups.Add(group);
        }

        if (previousKey is null)
        {
            return;
        }

        SelectedGroup = Groups.FirstOrDefault(row => RowKey(row) == previousKey);
    }

    private async Task RefreshDrugCatalogAsync()
    {
        if (IsLookupCatalogSuspended())
        {
            _drugCatalog = [];
            await UiThreadHelper.RunOnUiAsync(() =>
            {
                DrugOptions.Clear();
                SpecOptions.Clear();
                SelectedSpec = null;
            }).ConfigureAwait(false);
            return;
        }

        using var cts = new CancellationTokenSource(LookupTimeout);
        _drugCatalog = await LookupOptionLoader.LoadDrugOptionsAsync(lookup, cts.Token).ConfigureAwait(false);
        await UiThreadHelper.RunOnUiAsync(() =>
        {
            AutoCompleteFilter.RefreshVisibleOptions(DrugOptions, _drugCatalog, DrugText);
        }).ConfigureAwait(false);
    }

    private Task OnDrugInputChangedAsync(string? text)
        => RunInputCommitAsync(async () =>
        {
            if (IsLookupCatalogSuspended())
            {
                await RefreshDrugCatalogAsync().ConfigureAwait(true);
                await RefreshPreviewAsync(_sessionCts.Token).ConfigureAwait(true);
                return;
            }

            var input = NormalizeInput(text);
            SpecOptions.Clear();
            SelectedSpec = null;

            if (string.IsNullOrWhiteSpace(input))
            {
                await RefreshPreviewAsync(_sessionCts.Token).ConfigureAwait(true);
                return;
            }

            var version = Interlocked.Increment(ref _drugInputVersion);
            await ApplyDrugAsync(input, version).ConfigureAwait(true);
            await RefreshPreviewAsync(_sessionCts.Token).ConfigureAwait(true);
        });

    private async Task ApplyDrugAsync(string drugInput, int? version = null)
    {
        if (IsLookupCatalogSuspended())
        {
            await RefreshDrugCatalogAsync().ConfigureAwait(true);
            return;
        }

        using var cts = new CancellationTokenSource(LookupTimeout);
        var (canonical, specs) = await LookupOptionLoader.ResolveDrugAndSpecsAsync(
            lookup,
            drugInput,
            cts.Token).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(canonical))
        {
            return;
        }

        if (version.HasValue && version.Value != _drugInputVersion)
        {
            return;
        }

        await UiThreadHelper.RunOnUiAsync(() =>
        {
            DrugText = canonical;
            SpecOptions.Clear();
            foreach (var spec in specs)
            {
                SpecOptions.Add(spec);
            }

            SelectedSpec = null;
        }).ConfigureAwait(false);
    }

    private async Task RefreshPreviewAsync(CancellationToken ct)
    {
        if (ct.IsCancellationRequested)
        {
            return;
        }

        if (SelectedGroup is not { } group)
        {
            PreviewText = "请选择分组后自动预览";
            return;
        }

        var preview = await syncService.PreviewMsfxMappingBatchAsync(
            mapStatus: FilterInput.Norm(SelectedMapStatus),
            codeStatus: FilterInput.Norm(SelectedCodeStatus),
            searchScope: ResolveSearchScope(SelectedSearchScope),
            keyword: NormalizeText(Keyword),
            groupSourceDrugNameRaw: group.SourceDrugNameRaw,
            groupSourceSpecRaw: group.SourceSpecRaw,
            groupSourceNameNorm: group.SourceNameNorm,
            groupSourceSpecNorm: group.SourceSpecNorm,
            action: "APPLY_MAP",
            drugId: ResolvedDrugId,
            spec: ResolvedSpec,
            ct: ct).ConfigureAwait(false);

        if (ct.IsCancellationRequested)
        {
            return;
        }

        PreviewText =
            $"将影响 {preview.CandidateCount} 条，可执行 {preview.EligibleCount} 条，阻塞 {preview.BlockedCount} 条";
    }

    private async Task RunInputCommitAsync(Func<Task> action)
    {
        var task = action();
        _inputCommitTask = task;
        try
        {
            await task.ConfigureAwait(false);
        }
        finally
        {
            if (ReferenceEquals(_inputCommitTask, task))
            {
                _inputCommitTask = null;
            }
        }
    }

    private bool IsLookupCatalogSuspended()
        => accessGuard.IsBlocked;

    private static string RowKey(MsfxMappingBatchGroupRow row)
        => $"{row.SourceDrugNameRaw}|{row.SourceSpecRaw}|{row.SourceNameNorm}|{row.SourceSpecNorm}";

    private static string? NormalizeInput(string? value)
    {
        var trimmed = (value ?? string.Empty).Trim();
        return trimmed.Length == 0 || trimmed == "--" ? null : trimmed;
    }

    private static string? NormalizeText(string? value)
    {
        var text = (value ?? string.Empty).Trim();
        return text.Length == 0 ? null : text;
    }

    private static string ResolveSearchScope(string? value)
    {
        var v = (value ?? string.Empty).Trim();
        return v switch
        {
            "最小包装码" => "TRACE",
            "单据编码" => "BILL",
            "原始药/规" or "药名/规格(原始)" => "SOURCE_RAW",
            "校正药/规" or "药名/规格(归一化)" => "SOURCE_NORM",
            "层级码" or "层级码(1-5)" => "LEVEL_CODE",
            "映射目标" => "TARGET",
            "原因信息" => "REASON",
            _ => "ALL"
        };
    }

    private static string ResolveScopeLabel(string? scope)
    {
        var text = (scope ?? string.Empty).Trim();
        return SearchScopes.Contains(text) ? text : "全部字段";
    }
}
