using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using PacToolkits.Application.Services;
using PacToolkits.Desktop.Avalonia.Ui.Collections;
using PacToolkits.Desktop.Avalonia.ViewModels.Support.Catalog;

namespace PacToolkits.Desktop.Avalonia.ViewModels.Pages;

public sealed partial class BarcodeGen
{
    partial void OnDrugTextChanged(string? value)
    {
        RefreshDrugOptionsOrder(value);

        var drug = NormalizeInput(value);
        IsDrugSuggestOpen = !string.IsNullOrWhiteSpace(drug);
        if (string.IsNullOrWhiteSpace(drug))
        {
            ResetSpecSelection();
        }

        ClearDrugSpecFilterCommand.NotifyCanExecuteChanged();
        AddPoolPickCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedSpecChanged(OptionItem? value)
    {
        IsSpecSelected = value is not null && !string.IsNullOrWhiteSpace(value.Raw);
        AddPoolPickCommand.NotifyCanExecuteChanged();
    }

    partial void OnPoolPickCountChanged(int value)
        => AddPoolPickCommand.NotifyCanExecuteChanged();

    [RelayCommand]
    private async Task ApplyDrugFilterAsync()
    {
        if (SkipTrigger() || IsBarcodeOperating)
        {
            return;
        }

        IsDrugSuggestOpen = false;
        var drug = NormalizeInput(DrugText);
        if (string.IsNullOrWhiteSpace(drug))
        {
            ResetSpecSelection();
            return;
        }

        try
        {
            using var cts = new CancellationTokenSource(LookupTimeout);
            var canonical = await _lookup.ResolveCanonicalDrugIdAsync(drug, cts.Token).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(canonical))
            {
                var isDeprecated = await _lookup.IsDeprecatedDrugIdAsync(drug, cts.Token).ConfigureAwait(false);
                await RunOnUiAsync(() =>
                {
                    ResetSpecSelection();
                    _toast.Warn(
                        "条码生成",
                        isDeprecated ? "药品已被弃用" : "药品信息中无该药品");
                }).ConfigureAwait(true);
                return;
            }

            await RunOnUiAsync(() => DrugText = canonical).ConfigureAwait(true);
            await ReloadSpecsByDrugAsync(canonical, cts.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            if (CanToastError(ex))
            {
                await RunOnUiAsync(() => _toast.Error("条码生成", $"加载规格失败：{ex.Message}")).ConfigureAwait(true);
            }
        }
    }

    private bool CanClearDrugSpecFilter()
        => CanPage && AutoCompleteFilter.HasDrugText(DrugText);

    [RelayCommand(CanExecute = nameof(CanClearDrugSpecFilter))]
    private void ClearDrugSpecFilter()
    {
        if (SkipTrigger() || IsBarcodeOperating)
        {
            return;
        }

        IsDrugSuggestOpen = false;
        DrugText = null;
        ResetSpecSelection();
        ClearDrugSpecFilterCommand.NotifyCanExecuteChanged();
        AddPoolPickCommand.NotifyCanExecuteChanged();
    }

    private bool CanAddPoolPick()
        => CanPage
           && !IsBarcodeOperating
           && !string.IsNullOrWhiteSpace(NormalizeInput(DrugText))
           && SelectedSpec is not null
           && PoolPickCount > 0
           && PoolRows.Count < TraceBarcodeService.MaxPickItems;

    [RelayCommand(CanExecute = nameof(CanAddPoolPick))]
    private void AddPoolPick()
    {
        if (SkipTrigger() || IsBarcodeOperating)
        {
            return;
        }

        var drug = NormalizeInput(DrugText)!;
        var spec = NormalizeInput(SelectedSpec!.Raw)!;
        var count = Math.Max(1, PoolPickCount);
        var existing = PoolRows.FirstOrDefault(row =>
            string.Equals(row.DrugId, drug, StringComparison.Ordinal)
            && string.Equals(row.Spec, spec, StringComparison.Ordinal));
        if (existing is not null)
        {
            existing.Count = Math.Min(TraceBarcodeService.MaxCountPerItem, existing.Count + count);
            OnPropertyChanged(nameof(IsPoolPickEmpty));
            return;
        }

        if (PoolRows.Count >= TraceBarcodeService.MaxPickItems)
        {
            _toast.Warn("条码生成", $"取码队列最多 {TraceBarcodeService.MaxPickItems} 项");
            return;
        }

        PoolRows.Add(new PoolRow(drug, spec, count));
        OnPropertyChanged(nameof(IsPoolPickEmpty));
    }

    private void RefreshDrugOptionsOrder(string? searchText)
    {
        if (_drugCatalog.Count == 0)
        {
            return;
        }

        AutoCompleteFilter.RefreshVisibleOptions(DrugOptions, _drugCatalog, searchText);
    }

    private void ResetSpecSelection()
    {
        SpecOptions.Clear();
        SelectedSpec = null;
        IsSpecSelected = false;
    }

    private async Task ReloadSpecsByDrugAsync(string drugId, CancellationToken ct)
    {
        var specs = await LookupOptions.GetSpecsAsync(_lookup, drugId, ct).ConfigureAwait(false);
        await RunOnUiAsync(() =>
        {
            OptionCollectionHelper.ReplaceRaw(SpecOptions, specs, StringComparison.Ordinal);
            if (SpecOptions.Count == 0)
            {
                ResetSpecSelection();
                _toast.Warn("条码生成", "该药品暂无可用规格");
                return;
            }

            SelectedSpec = SpecOptions[0];
            IsSpecSelected = true;
        }).ConfigureAwait(true);
    }

    private void ResetDrugFilterState()
    {
        IsDrugSuggestOpen = false;
        DrugText = null;
        ResetSpecSelection();
        ClearDrugSpecFilterCommand.NotifyCanExecuteChanged();
        AddPoolPickCommand.NotifyCanExecuteChanged();
    }
}
