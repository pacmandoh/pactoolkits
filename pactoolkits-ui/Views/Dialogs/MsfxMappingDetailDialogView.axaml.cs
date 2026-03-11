using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using pactoolkits_ui.Common;
using pactoolkits_ui.Services;
using pactoolkits_ui.ViewModels.Pages;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace pactoolkits_ui.Views.Dialogs;

public sealed record MsfxMappingDetailDialogModel(
    string TraceCode,
    string SourceBillCode,
    string SourceDrug,
    string SourceSpec,
    string Normalized,
    string MappedTarget,
    string MapStatus,
    string CodeStatus,
    string UpdatedAt,
    bool IsReadOnly);

public partial class MsfxMappingDetailDialogView : UserControl
{
    private static readonly TimeSpan LookupTimeout = TimeSpan.FromSeconds(8);
    private readonly ILookupCatalogService? _lookup;
    private IReadOnlyList<OptionItem> _allDrugIds = Array.Empty<OptionItem>();
    private bool _initialized;
    private int _drugInputVersion;

    public MsfxMappingDetailDialogView()
    {
        InitializeComponent();
        _lookup = (Application.Current as App)?.Services.GetService<ILookupCatalogService>();
        AttachedToVisualTree += OnAttachedToVisualTree;
        AutoCompleteHelper.AttachDrugOptionFilter(DrugIdBox);

        DrugIdBox.PropertyChanged += OnDrugBoxPropertyChanged;
        SpecBox.SelectionChanged += OnSpecSelectionChanged;
    }

    public string DrugId => (DrugIdBox.Text ?? string.Empty).Trim();
    public string Spec => (SpecBox.SelectedItem?.ToString() ?? SpecBox.Text ?? string.Empty).Trim();

    private async void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (_initialized)
            return;

        _initialized = true;
        await InitializeLookupAsync();
    }

    private async Task InitializeLookupAsync()
    {
        if (_lookup is null)
            return;

        using var cts = new CancellationTokenSource(LookupTimeout);
        var ids = await _lookup.GetDrugIdsAsync(cts.Token).ConfigureAwait(false);
        _allDrugIds = ids.Select(x => new OptionItem(x, x)).ToArray();
        await RunOnUiAsync(() =>
        {
            DrugIdBox.ItemsSource = _allDrugIds;
            UpdateSpecPlaceholder();
        }).ConfigureAwait(false);

        MsfxMappingDetailDialogModel? model = null;
        await RunOnUiAsync(() =>
        {
            model = DataContext as MsfxMappingDetailDialogModel;
        }).ConfigureAwait(false);

        if (model is null)
            return;

        var initialDrug = NormalizeInput(ParseFirst(model.MappedTarget))
                          ?? NormalizeInput(model.SourceDrug);
        if (string.IsNullOrWhiteSpace(initialDrug))
            return;

        await RunOnUiAsync(() => DrugIdBox.Text = initialDrug).ConfigureAwait(false);
        await ApplyDrugAsync(initialDrug).ConfigureAwait(false);

        var initialSpec = NormalizeInput(ParseSecond(model.MappedTarget))
                          ?? NormalizeInput(model.SourceSpec);
        if (!string.IsNullOrWhiteSpace(initialSpec))
        {
            await RunOnUiAsync(() =>
            {
                if (SpecBox.ItemsSource is IEnumerable<string> specs &&
                    specs.Any(x => string.Equals(x, initialSpec, StringComparison.OrdinalIgnoreCase)))
                {
                    SpecBox.SelectedItem = specs.First(x => string.Equals(x, initialSpec, StringComparison.OrdinalIgnoreCase));
                }
            }).ConfigureAwait(false);
            await RefreshQtyAsync().ConfigureAwait(false);
        }
    }

    private async Task OnDrugInputChangedAsync(string? text)
    {
        if (_lookup is null)
            return;

        var input = NormalizeInput(text);
        await RunOnUiAsync(() =>
        {
            SpecBox.ItemsSource = null;
            SpecBox.SelectedItem = null;
            QtyText.Text = "--";
            UpdateSpecPlaceholder();
        }).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(input))
            return;

        var version = Interlocked.Increment(ref _drugInputVersion);
        await ApplyDrugAsync(input, version).ConfigureAwait(false);
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
            SpecBox.ItemsSource = specs;
            SpecBox.SelectedItem = null;
            QtyText.Text = "--";
            UpdateSpecPlaceholder();
        }).ConfigureAwait(false);
    }

    private async void OnSpecSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        UpdateSpecPlaceholder();
        await RefreshQtyAsync();
    }

    private async void OnDrugBoxPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != AutoCompleteBox.TextProperty)
            return;

        var text = (e.NewValue as OptionItem)?.Raw ?? e.NewValue?.ToString();
        await OnDrugInputChangedAsync(text);
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
                    return;

                var version = Interlocked.Increment(ref _drugInputVersion);
                await ApplyDrugAsync(input, version).ConfigureAwait(false);
                await RunOnUiAsync(UpdateSpecPlaceholder).ConfigureAwait(false);
                await RefreshQtyAsync().ConfigureAwait(false);
            });
    }

    private async Task RefreshQtyAsync()
    {
        if (_lookup is null)
            return;

        string? drug = null;
        string? spec = null;
        await RunOnUiAsync(() =>
        {
            drug = NormalizeInput(DrugIdBox.Text);
            spec = NormalizeInput(SpecBox.SelectedItem?.ToString() ?? SpecBox.Text);
        }).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(drug) || string.IsNullOrWhiteSpace(spec))
        {
            await RunOnUiAsync(() => QtyText.Text = "--").ConfigureAwait(false);
            return;
        }

        using var cts = new CancellationTokenSource(LookupTimeout);
        var qty = await _lookup.GetQtyAsync(drug, spec, cts.Token).ConfigureAwait(false);
        await RunOnUiAsync(() => QtyText.Text = qty?.ToString() ?? "--").ConfigureAwait(false);
    }

    private void UpdateSpecPlaceholder()
    {
        if (SpecPlaceholder is null || SpecBox is null)
            return;

        var hasValue = !string.IsNullOrWhiteSpace(SpecBox.SelectedItem?.ToString()) ||
                       !string.IsNullOrWhiteSpace(SpecBox.Text);
        SpecPlaceholder.IsVisible = !hasValue;
    }

    private static string? ParseFirst(string value)
    {
        var idx = value.IndexOf('/');
        return idx <= 0 ? null : value[..idx].Trim();
    }

    private static string? ParseSecond(string value)
    {
        var idx = value.IndexOf('/');
        return idx < 0 || idx + 1 >= value.Length ? null : value[(idx + 1)..].Trim();
    }

    private static string? NormalizeInput(string? value)
    {
        var trimmed = (value ?? string.Empty).Trim();
        return trimmed.Length == 0 || trimmed == "--" ? null : trimmed;
    }

    private static Task RunOnUiAsync(Action action) => UiThreadHelper.RunOnUiAsync(action);
}
