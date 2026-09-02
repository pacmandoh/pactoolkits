using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PacToolkits.Application.Abstractions;
using PacToolkits.Application.Services;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure.Security;

namespace PacToolkits.Desktop.Avalonia.ViewModels.Pages;

public partial class Settings
{
    protected override void OnPageAvailabilityChanged()
    {
        ClearBarcodeAuditLogCommand.NotifyCanExecuteChanged();
        RefreshOpsUnlockCommands();
    }

    private readonly IBarcodeGenSettingsService _barcodeGenSettings;
    private readonly ITraceBarcodeService _traceBarcode;

    [ObservableProperty] private int _barcodeExcludeRecentDays = 7;
    [ObservableProperty] private int _barcodeWidthPx = 600;
    [ObservableProperty] private int _barcodeQuietZoneModules = 10;
    [ObservableProperty] private string _barcodeLabelUnit = string.Empty;
    [ObservableProperty] private string _barcodeFileNamePrefix = BarcodeGenExportOptions.DefaultPrefix;

    private Guid? _clearBarcodeAuditCommandId;

    private void OnBarcodeGenSettingsChanged()
    {
        PostUi(() =>
        {
            if (IsTabDirty((int)Tab.BarcodeGen))
            {
                _toast.Warn("条码生成", "配置文件已更新，当前未保存的设置未同步");
                return;
            }

            SyncBarcodeGen();
        }, "barcode_gen.changed.ui_fail");
    }

    private void SyncBarcodeGen()
    {
        var options = _barcodeGenSettings.Current;
        BarcodeExcludeRecentDays = options.ExcludeRecentDays;
        BarcodeWidthPx = options.Image.WidthPx;
        BarcodeQuietZoneModules = options.Image.QuietZoneModules;
        BarcodeLabelUnit = options.Image.Unit;
        BarcodeFileNamePrefix = options.Export.FileNamePrefix;
    }

    [RelayCommand]
    private Task SaveBarcodeGenAsync() => ApplyBarcodeGenAsync();

    private bool CanClearBarcodeAuditLog() => CanPage;

    [RelayCommand(CanExecute = nameof(CanClearBarcodeAuditLog))]
    private async Task ClearBarcodeAuditLogAsync()
    {
        if (SkipTrigger() || !CanPage)
        {
            return;
        }

        if (!await _unlockService.RequireUnlockAsync(
                UnlockScopes.SharedOps,
                scene: "settings.barcode_audit_clear",
                promptTitle: "敏感操作解锁",
                promptHint: UnlockScopes.SharedOpsHint,
                ct: _pageWorkCts.Token).ConfigureAwait(true))
        {
            return;
        }

        var confirmed = await _dialog.ConfirmDestructive(
            "清空审计记录",
            "将删除全部条码预览与导出审计记录，「排除近期」将重新计算。是否继续？");
        if (!confirmed)
        {
            return;
        }

        var commandId = _clearBarcodeAuditCommandId ??= Guid.NewGuid();
        try
        {
            var deleted = await _traceBarcode
                .ClearAuditLogAsync(_pageWorkCts.Token, commandId)
                .ConfigureAwait(true);
            _clearBarcodeAuditCommandId = null;
            _toast.Success("条码生成", $"已清空 {deleted} 条审计记录");
        }
        catch (OperationCanceledException) when (_pageWorkCts.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            if (CanToastError(ex))
            {
                _toast.Error("清空审计记录失败", ex.Message);
            }
        }
    }

    private async Task<bool> ApplyBarcodeGenAsync()
    {
        try
        {
            await _barcodeGenSettings.SaveAsync(BuildBarcodeGenDraft()).ConfigureAwait(true);
            _toast.Success("条码生成", "设置已保存并生效");
            RefreshUnsaved();
            return true;
        }
        catch (Exception ex)
        {
            _toast.Error("条码生成保存失败", ex.Message);
            return false;
        }
    }

    private BarcodeGenOptions BuildBarcodeGenDraft()
        => BarcodeGenSettingsService.Normalize(new BarcodeGenOptions
        {
            ExcludeRecentDays = BarcodeExcludeRecentDays,
            Export = new BarcodeGenExportOptions { FileNamePrefix = BarcodeFileNamePrefix },
            Image = new BarcodeGenImageOptions
            {
                WidthPx = BarcodeWidthPx,
                QuietZoneModules = BarcodeQuietZoneModules,
                Unit = BarcodeLabelUnit
            }
        });

    partial void OnBarcodeExcludeRecentDaysChanged(int value) => RefreshUnsaved();
    partial void OnBarcodeWidthPxChanged(int value) => RefreshUnsaved();
    partial void OnBarcodeQuietZoneModulesChanged(int value) => RefreshUnsaved();
    partial void OnBarcodeLabelUnitChanged(string value) => RefreshUnsaved();
    partial void OnBarcodeFileNamePrefixChanged(string value) => RefreshUnsaved();
}
