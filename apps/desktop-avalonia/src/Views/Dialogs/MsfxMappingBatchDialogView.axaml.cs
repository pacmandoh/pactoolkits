using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Input;
using global::Avalonia.Interactivity;
using Microsoft.Extensions.DependencyInjection;
using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;
using PacToolkits.Application.Services;
using PacToolkits.Desktop.Avalonia.Common;
using PacToolkits.Desktop.Avalonia.Controls;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure;
using PacToolkits.Desktop.Avalonia.ViewModels.Pages;

namespace PacToolkits.Desktop.Avalonia.Views.Dialogs;

public partial class MsfxMappingBatchDialogView : UserControl
{
    private static readonly string[] MapStatusFilters = ["ALL", "PENDING", "MAPPED", "NEED_REVIEW", "FAILED"];
    private static readonly string[] CodeStatusFilters = ["ALL", "NEW", "TASKED", "FAILED"];
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
    private readonly ILookupCatalogService? _lookup;
    private readonly IMsfxSyncService? _syncService;
    private readonly IDatabaseAccessGuard? _accessGuard;
    private readonly SearchInputDebouncer _searchDebouncer = new(450);
    private readonly ObservableCollection<MsfxMappingBatchGroupRow> _groups = new();
    private IReadOnlyList<OptionItem> _allDrugIds = Array.Empty<OptionItem>();
    private bool _initialized;
    private bool _isResettingFilters;
    private bool _isSearchPanelVisible;
    private int _drugInputVersion;
    private string _drugIdDraft = string.Empty;
    private string _specDraft = string.Empty;
    private Task? _inputCommitTask;

    public MsfxMappingBatchDialogView()
    {
        InitializeComponent();
        _lookup = (global::Avalonia.Application.Current as App)?.Services.GetService<ILookupCatalogService>();
        _syncService = (global::Avalonia.Application.Current as App)?.Services.GetService<IMsfxSyncService>();
        _accessGuard = (global::Avalonia.Application.Current as App)?.Services.GetService<IDatabaseAccessGuard>();
        GroupGrid.ItemsSource = _groups;
        UpdateSearchPanelVisibility();
        AttachedToVisualTree += OnAttachedToVisualTree;
        AutoCompleteHelper.AttachDrugOptionFilter(DrugIdBox);
        AutoCompleteHelper.AttachCandidateCommitApplyAsync(DrugIdBox, this, "SpecBox", ApplyDrugBoxCommitAsync);

        DrugIdBox.BoxPropertyChanged += OnDrugBoxPropertyChanged;
        SpecBox.SelectionChanged += OnSpecSelectionChanged;
        GroupGrid.SelectionChanged += OnGroupSelectionChanged;
        SearchScopeBox.SelectionChanged += OnFilterSelectionChanged;
        MapStatusBox.SelectionChanged += OnFilterSelectionChanged;
        CodeStatusBox.SelectionChanged += OnFilterSelectionChanged;
        KeywordBox.TextChanged += OnKeywordTextChanged;
    }

    public MsfxMappingBatchGroupRow? SelectedGroup
        => GroupGrid.SelectedItem as MsfxMappingBatchGroupRow;

    public async Task PrepareForActionAsync()
    {
        SyncInputDraftsFromControls();

        var pending = _inputCommitTask;
        if (pending is not null)
        {
            try
            {
                await pending.ConfigureAwait(true);
            }
            catch
            {
                // Drug lookup failures are surfaced by preview/apply guards.
            }
        }

        SyncInputDraftsFromControls();
    }

    public string DrugId => NormalizeInput(_drugIdDraft) ?? string.Empty;

    public string Spec => NormalizeInput(_specDraft) ?? string.Empty;

    private async void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        InitializeFilterControls();
        await InitializeAsync();
    }

    private void InitializeFilterControls()
    {
        SearchScopeBox.ItemsSource = SearchScopes;
        MapStatusBox.ItemsSource = MapStatusFilters;
        CodeStatusBox.ItemsSource = CodeStatusFilters;

        var model = DataContext as MsfxMappingBatchDialogModel;
        SearchScopeBox.SelectedItem = ResolveScopeLabel(model?.SearchScope) ?? "全部字段";
        MapStatusBox.SelectedItem = string.IsNullOrWhiteSpace(model?.MapStatusFilter) ? "ALL" : model.MapStatusFilter;
        CodeStatusBox.SelectedItem = string.IsNullOrWhiteSpace(model?.CodeStatusFilter) ? "ALL" : model.CodeStatusFilter;
        KeywordBox.Text = model?.Keyword ?? string.Empty;
    }

    private async Task InitializeAsync()
    {
        var model = DataContext as MsfxMappingBatchDialogModel;
        if (model?.Groups is { Count: > 0 } groups)
        {
            await RunOnUiAsync(() => ReplaceGroups(groups)).ConfigureAwait(false);
        }
        else
        {
            await ReloadGroupsAsync().ConfigureAwait(false);
        }

        await RefreshDrugCatalogAsync().ConfigureAwait(false);
        await RefreshPreviewAsync().ConfigureAwait(false);
    }

    private bool IsLookupCatalogSuspended()
        => _accessGuard?.IsBlocked == true;

    private async Task ReloadGroupsAsync()
    {
        if (_syncService is null)
        {
            return;
        }

        string? mapStatus = null;
        string? codeStatus = null;
        string? searchScope = null;
        string? keyword = null;
        await RunOnUiAsync(() =>
        {
            mapStatus = NormalizeFilterValue(MapStatusBox.SelectedItem?.ToString());
            codeStatus = NormalizeFilterValue(CodeStatusBox.SelectedItem?.ToString());
            searchScope = ResolveSearchScope(SearchScopeBox.SelectedItem?.ToString());
            keyword = NormalizeText(KeywordBox.Text);
        }).ConfigureAwait(false);

        var groups = await _syncService.LoadMappingBatchGroupsAsync(
            mapStatus,
            codeStatus,
            searchScope,
            keyword,
            limit: 500,
            ct: CancellationToken.None).ConfigureAwait(false);

        await RunOnUiAsync(() =>
        {
            ReplaceGroups(groups);
        }).ConfigureAwait(false);

        await RefreshPreviewAsync().ConfigureAwait(false);
    }

    private void ReplaceGroups(IReadOnlyList<MsfxMappingBatchGroupRow> groups)
    {
        var previousKey = GroupGrid.SelectedItem is MsfxMappingBatchGroupRow previous
            ? RowKey(previous)
            : null;

        _groups.Clear();
        foreach (var group in groups)
        {
            _groups.Add(group);
        }

        if (previousKey is null)
        {
            return;
        }

        GroupGrid.SelectedItem = _groups.FirstOrDefault(row => RowKey(row) == previousKey);
    }

    private void ToggleSearchPanel_OnClick(object? sender, RoutedEventArgs e)
    {
        _isSearchPanelVisible = !_isSearchPanelVisible;
        UpdateSearchPanelVisibility();
    }

    private void UpdateSearchPanelVisibility()
    {
        SearchPanel.IsVisible = _isSearchPanelVisible;
        SearchPanelClosedIcon.IsVisible = !_isSearchPanelVisible;
        SearchPanelOpenIcon.IsVisible = _isSearchPanelVisible;
    }

    private async void OnFilterSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!_initialized || _isResettingFilters)
        {
            return;
        }

        _searchDebouncer.Cancel();
        await ReloadGroupsAsync().ConfigureAwait(false);
    }

    private void OnKeywordTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (!_initialized || _isResettingFilters)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(KeywordBox.Text))
        {
            _searchDebouncer.Cancel();
            _ = ReloadGroupsAsync();
            return;
        }

        _searchDebouncer.Schedule(ReloadGroupsAsync);
    }

    private async void KeywordBox_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        _searchDebouncer.Cancel();
        await ReloadGroupsAsync().ConfigureAwait(false);
    }

    private async void SearchButton_OnClick(object? sender, RoutedEventArgs e)
    {
        _searchDebouncer.Cancel();
        await ReloadGroupsAsync().ConfigureAwait(false);
    }

    private async void ResetFiltersButton_OnClick(object? sender, RoutedEventArgs e)
    {
        _isResettingFilters = true;
        try
        {
            _searchDebouncer.Cancel();
            SearchScopeBox.SelectedItem = "全部字段";
            MapStatusBox.SelectedItem = "ALL";
            CodeStatusBox.SelectedItem = "ALL";
            KeywordBox.Text = string.Empty;
        }
        finally
        {
            _isResettingFilters = false;
        }

        await ReloadGroupsAsync().ConfigureAwait(false);
    }

    private async Task RefreshDrugCatalogAsync()
    {
        if (_lookup is null || IsLookupCatalogSuspended())
        {
            _allDrugIds = Array.Empty<OptionItem>();
            await RunOnUiAsync(() =>
            {
                DrugIdBox.ItemsSource = _allDrugIds;
                SpecBox.ItemsSource = null;
                SpecBox.SelectedItem = null;
                _specDraft = string.Empty;
                UpdateSpecPlaceholder();
            }).ConfigureAwait(false);
            return;
        }

        using var cts = new CancellationTokenSource(LookupTimeout);
        _allDrugIds = await LookupOptionLoader.LoadDrugOptionsAsync(_lookup, cts.Token).ConfigureAwait(false);
        await RunOnUiAsync(() =>
        {
            DrugIdBox.ItemsSource = _allDrugIds;
            UpdateSpecPlaceholder();
        }).ConfigureAwait(false);
    }

    private async void DrugIdBox_OnKeyDown(object? sender, KeyEventArgs e)
    {
        _ = AutoCompleteHelper.HandleEnterCommitAndApplyAsync(
            this,
            sender,
            e,
            "SpecBox",
            ApplyDrugBoxCommitAsync);
    }

    private Task ApplyDrugBoxCommitAsync(PlainAutoCompleteBox box)
        => RunInputCommitAsync(async () =>
        {
            var input = NormalizeInput(box.Text);
            if (string.IsNullOrWhiteSpace(input))
            {
                await RefreshPreviewAsync().ConfigureAwait(false);
                return;
            }

            var version = Interlocked.Increment(ref _drugInputVersion);
            await ApplyDrugAsync(input, version).ConfigureAwait(false);
            await RunOnUiAsync(() =>
            {
                if (SpecBox.ItemsSource is IEnumerable<string> specs)
                {
                    var first = specs.FirstOrDefault();
                    if (!string.IsNullOrWhiteSpace(first))
                    {
                        SpecBox.SelectedItem = first;
                        _specDraft = first;
                    }
                }

                UpdateSpecPlaceholder();
            }).ConfigureAwait(false);
            await RefreshPreviewAsync().ConfigureAwait(false);
        });

    private Task OnDrugInputChangedAsync(string? text)
    {
        if (_lookup is null)
        {
            return Task.CompletedTask;
        }

        return RunInputCommitAsync(async () =>
        {
            if (IsLookupCatalogSuspended())
            {
                await RefreshDrugCatalogAsync().ConfigureAwait(false);
                await RefreshPreviewAsync().ConfigureAwait(false);
                return;
            }

            var input = NormalizeInput(text);
            await RunOnUiAsync(() =>
            {
                _drugIdDraft = DrugIdBox.Text ?? string.Empty;
                SpecBox.ItemsSource = null;
                SpecBox.SelectedItem = null;
                _specDraft = string.Empty;
                UpdateSpecPlaceholder();
            }).ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(input))
            {
                await RefreshPreviewAsync().ConfigureAwait(false);
                return;
            }

            var version = Interlocked.Increment(ref _drugInputVersion);
            await ApplyDrugAsync(input, version).ConfigureAwait(false);
            await RefreshPreviewAsync().ConfigureAwait(false);
        });
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

    private void SyncInputDraftsFromControls()
    {
        _drugIdDraft = DrugIdBox.Text ?? string.Empty;
        _specDraft = SpecBox.SelectedItem?.ToString() ?? SpecBox.Text ?? string.Empty;
    }

    private async Task ApplyDrugAsync(string drugInput, int? version = null)
    {
        if (_lookup is null || IsLookupCatalogSuspended())
        {
            await RefreshDrugCatalogAsync().ConfigureAwait(false);
            return;
        }

        using var cts = new CancellationTokenSource(LookupTimeout);
        var (canonical, specs) = await LookupOptionLoader.ResolveDrugAndSpecsAsync(
            _lookup,
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

        await RunOnUiAsync(() =>
        {
            DrugIdBox.Text = canonical;
            _drugIdDraft = canonical;
            SpecBox.ItemsSource = specs;
            SpecBox.SelectedItem = null;
            _specDraft = string.Empty;
            UpdateSpecPlaceholder();
        }).ConfigureAwait(false);
    }

    private async Task RefreshPreviewAsync()
    {
        if (_syncService is null)
        {
            return;
        }

        MsfxMappingBatchGroupRow? group = null;
        string? mapStatus = null;
        string? codeStatus = null;
        string? searchScope = null;
        string? keyword = null;
        string? drug = null;
        string? spec = null;
        await RunOnUiAsync(() =>
        {
            group = SelectedGroup;
            mapStatus = NormalizeFilterValue(MapStatusBox.SelectedItem?.ToString());
            codeStatus = NormalizeFilterValue(CodeStatusBox.SelectedItem?.ToString());
            searchScope = ResolveSearchScope(SearchScopeBox.SelectedItem?.ToString());
            keyword = NormalizeText(KeywordBox.Text);
            drug = NormalizeInput(_drugIdDraft);
            spec = NormalizeInput(SpecBox.SelectedItem?.ToString() ?? SpecBox.Text);
            _drugIdDraft = drug ?? string.Empty;
            _specDraft = spec ?? string.Empty;
        }).ConfigureAwait(false);

        if (group is null)
        {
            await RunOnUiAsync(() => PreviewText.Text = "请选择分组后自动预览").ConfigureAwait(false);
            return;
        }

        var preview = await _syncService.PreviewMsfxMappingBatchAsync(
            mapStatus: mapStatus,
            codeStatus: codeStatus,
            searchScope: searchScope,
            keyword: keyword,
            groupSourceDrugNameRaw: group.SourceDrugNameRaw,
            groupSourceSpecRaw: group.SourceSpecRaw,
            groupSourceNameNorm: group.SourceNameNorm,
            groupSourceSpecNorm: group.SourceSpecNorm,
            action: "APPLY_MAP",
            drugId: drug,
            spec: spec,
            ct: CancellationToken.None).ConfigureAwait(false);

        await RunOnUiAsync(() =>
        {
            PreviewText.Text =
                $"将影响 {preview.CandidateCount} 条，可执行 {preview.EligibleCount} 条，阻塞 {preview.BlockedCount} 条";
        }).ConfigureAwait(false);
    }

    private async void OnGroupSelectionChanged(object? sender, SelectionChangedEventArgs e)
        => await RefreshPreviewAsync();

    private void GroupGrid_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not DataGrid grid)
        {
            return;
        }

        DataGridInteractionHelper.TrySelectRowFromPointer(
            grid,
            e.Source,
            requireRowHeader: false,
            out _,
            out _);
    }

    private async void OnSpecSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        _specDraft = SpecBox.SelectedItem?.ToString() ?? SpecBox.Text ?? string.Empty;
        UpdateSpecPlaceholder();
        await RefreshPreviewAsync();
    }

    private async void OnDrugBoxPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != PlainAutoCompleteBox.TextProperty && e.Property != TextBox.TextProperty)
        {
            return;
        }

        var text = (e.NewValue as OptionItem)?.Raw ?? e.NewValue?.ToString();
        _drugIdDraft = text ?? string.Empty;
        await OnDrugInputChangedAsync(text);
    }

    private void UpdateSpecPlaceholder()
    {
        if (SpecPlaceholder is null || SpecBox is null)
        {
            return;
        }

        var hasValue = !string.IsNullOrWhiteSpace(SpecBox.SelectedItem?.ToString()) ||
                       !string.IsNullOrWhiteSpace(SpecBox.Text);
        SpecPlaceholder.IsVisible = !hasValue;
    }

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

    private static string? NormalizeFilterValue(string? value)
    {
        var text = (value ?? string.Empty).Trim();
        return text.Length == 0 || string.Equals(text, "ALL", StringComparison.OrdinalIgnoreCase)
            ? null
            : text;
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

    private static Task RunOnUiAsync(Action action) => UiThreadHelper.RunOnUiAsync(action);
}
