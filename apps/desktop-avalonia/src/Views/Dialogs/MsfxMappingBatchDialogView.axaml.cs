using Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Controls.Primitives;
using global::Avalonia.Input;
using global::Avalonia.Interactivity;
using global::Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using PacToolkits.Application.DTOs;
using PacToolkits.Application.Services;
using PacToolkits.Desktop.Avalonia.Common;
using PacToolkits.Application.Abstractions;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure;
using PacToolkits.Desktop.Avalonia.ViewModels.Pages;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PacToolkits.Desktop.Avalonia.Views.Dialogs;

public sealed record MsfxMappingBatchDialogModel(
    IReadOnlyList<MsfxMappingBatchGroupRow> Groups,
    string MapStatusFilter,
    string CodeStatusFilter,
    string SearchScope,
    string Keyword);

public partial class MsfxMappingBatchDialogView : UserControl
{
    private static readonly TimeSpan LookupTimeout = TimeSpan.FromSeconds(8);
    private readonly ILookupCatalogService? _lookup;
    private readonly IMsfxSyncService? _syncService;
    private IReadOnlyList<OptionItem> _allDrugIds = Array.Empty<OptionItem>();
    private bool _initialized;
    private int _drugInputVersion;
    private string _drugIdDraft = string.Empty;
    private string _specDraft = string.Empty;
    private MsfxMappingBatchGroupRow? _selectedGroup;

    public MsfxMappingBatchDialogView()
    {
        InitializeComponent();
        _lookup = (global::Avalonia.Application.Current as App)?.Services.GetService<ILookupCatalogService>();
        _syncService = (global::Avalonia.Application.Current as App)?.Services.GetService<IMsfxSyncService>();
        AttachedToVisualTree += OnAttachedToVisualTree;
        AutoCompleteHelper.AttachDrugOptionFilter(DrugIdBox);

        DrugIdBox.PropertyChanged += OnDrugBoxPropertyChanged;
        SpecBox.SelectionChanged += OnSpecSelectionChanged;
        GroupGrid.SelectionChanged += OnGroupSelectionChanged;
    }

    public MsfxMappingBatchGroupRow? SelectedGroup
    {
        get => _selectedGroup;
    }

    public string DrugId => NormalizeInput(_drugIdDraft) ?? string.Empty;
    public string Spec => NormalizeInput(_specDraft) ?? string.Empty;

    private async void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (_initialized)
            return;

        _initialized = true;
        await InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        if (_lookup is not null)
        {
            using var cts = new CancellationTokenSource(LookupTimeout);
            var ids = await _lookup.GetDrugIdsAsync(cts.Token).ConfigureAwait(false);
            _allDrugIds = ids.Select(x => new OptionItem(x, x)).ToArray();
            await RunOnUiAsync(() =>
            {
                DrugIdBox.ItemsSource = _allDrugIds;
                UpdateSpecPlaceholder();
            }).ConfigureAwait(false);
        }

        await RefreshPreviewAsync().ConfigureAwait(false);
    }

    private async void DrugIdBox_OnKeyDown(object? sender, KeyEventArgs e)
    {
        _ = AutoCompleteHelper.HandleEnterCommitAndApplyAsync(
            this,
            sender,
            e,
            "SpecBox",
            async box =>
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
    }

    private async Task OnDrugInputChangedAsync(string? text)
    {
        if (_lookup is null)
            return;

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
    }

    private async Task ApplyDrugAsync(string drugInput, int? version = null)
    {
        if (_lookup is null)
            return;

        using var cts = new CancellationTokenSource(LookupTimeout);
        var canonical = await _lookup.ResolveCanonicalDrugIdAsync(drugInput, cts.Token).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(canonical))
            return;

        var specs = await _lookup.GetSpecsByDrugAsync(canonical, cts.Token).ConfigureAwait(false);
        if (version.HasValue && version.Value != _drugInputVersion)
            return;

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
            return;

        MsfxMappingBatchDialogModel? model = null;
        MsfxMappingBatchGroupRow? group = null;
        string? drug = null;
        string? spec = null;
        await RunOnUiAsync(() =>
        {
            model = DataContext as MsfxMappingBatchDialogModel;
            group = SelectedGroup;
            drug = NormalizeInput(_drugIdDraft);
            spec = NormalizeInput(SpecBox.SelectedItem?.ToString() ?? SpecBox.Text);
            _drugIdDraft = drug ?? string.Empty;
            _specDraft = spec ?? string.Empty;
        }).ConfigureAwait(false);

        if (model is null || group is null)
        {
            await RunOnUiAsync(() => PreviewText.Text = "请选择分组后自动预览").ConfigureAwait(false);
            return;
        }
        var preview = await _syncService.PreviewMsfxMappingBatchAsync(
            mapStatus: NormalizeFilterValue(model.MapStatusFilter),
            codeStatus: NormalizeFilterValue(model.CodeStatusFilter),
            searchScope: ResolveSearchScope(model.SearchScope),
            keyword: NormalizeText(model.Keyword),
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
            PreviewText.Text = $"将影响 {preview.CandidateCount} 条，可执行 {preview.EligibleCount} 条，阻塞 {preview.BlockedCount} 条";
        }).ConfigureAwait(false);

    }

    private async void OnGroupSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        _selectedGroup = GroupGrid.SelectedItem as MsfxMappingBatchGroupRow;
        await RefreshPreviewAsync();
    }

    private void GroupGrid_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not DataGrid grid)
            return;

        if (DataGridInteractionHelper.TrySelectRowFromPointer(
                grid,
                e.Source,
                requireRowHeader: false,
                out var rowData,
                out _)
            && rowData is MsfxMappingBatchGroupRow item)
        {
            _selectedGroup = item;
        }
    }

    private async void OnSpecSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        _specDraft = SpecBox.SelectedItem?.ToString() ?? SpecBox.Text ?? string.Empty;
        UpdateSpecPlaceholder();
        await RefreshPreviewAsync();
    }

    private async void OnDrugBoxPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != AutoCompleteBox.TextProperty)
            return;

        var text = (e.NewValue as OptionItem)?.Raw ?? e.NewValue?.ToString();
        _drugIdDraft = text ?? string.Empty;
        await OnDrugInputChangedAsync(text);
    }

    private void UpdateSpecPlaceholder()
    {
        if (SpecPlaceholder is null || SpecBox is null)
            return;

        var hasValue = !string.IsNullOrWhiteSpace(SpecBox.SelectedItem?.ToString()) ||
                       !string.IsNullOrWhiteSpace(SpecBox.Text);
        SpecPlaceholder.IsVisible = !hasValue;
    }

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
            "原始药/规" => "SOURCE_RAW",
            "校正药/规" => "SOURCE_NORM",
            "层级码" => "LEVEL_CODE",
            "映射目标" => "TARGET",
            "原因信息" => "REASON",
            _ => "ALL"
        };
    }

    private static Task RunOnUiAsync(Action action) => UiThreadHelper.RunOnUiAsync(action);

}
