using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;
using PacToolkits.Application.Services;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure.Dialogs;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure.Notifications;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure.Platform;
using PacToolkits.Desktop.Avalonia.Services.Presentation.Barcode;
using PacToolkits.Desktop.Avalonia.Ui.Formatting;
using PacToolkits.Desktop.Avalonia.ViewModels.Support.Catalog;

namespace PacToolkits.Desktop.Avalonia.ViewModels.Pages;

/// <summary>展示库存预留或手动输入生成的追溯码条码，并记录预览与导出结果</summary>
public sealed partial class BarcodeGen : AppPageBase
{
    private static readonly TimeSpan LookupTimeout = TimeSpan.FromSeconds(8);

    public override string DisplayName => "条码生成";
    public override string Icon => "QrCode";
    public override int Index => 4;

    private readonly ITraceBarcodeService _traceBarcode;
    private readonly ILookupCatalogService _lookup;
    private readonly ITraceCodeRuleService _traceCodeRule;
    private readonly IBarcodeGenSettingsService _barcodeSettings;
    private readonly ITraceCodeBarcodeService _barcodeRender;
    private readonly IFolderPickerService _folderPicker;
    private readonly IToastService _toast;
    private readonly IDialogService _dialog;

    private readonly List<string> _sessionCodes = new();
    private readonly HashSet<string> _sessionCodeSet = new(StringComparer.Ordinal);
    private CancellationTokenSource _operateCts = new();
    private Guid? _pendingExportAuditBatchId;
    private Guid? _pendingExportAuditCommandId;
    private IReadOnlyList<TraceBarcodeAuditItem>? _pendingExportAuditItems;
    private IReadOnlyList<PreviewCard>? _pendingExportAuditCards;
    private IReadOnlyList<OptionItem> _drugCatalog = [];

    public ObservableCollection<PoolRow> PoolRows { get; } = new();
    public ObservableCollection<PreviewCard> PreviewCards { get; } = new();
    public ObservableCollection<OptionItem> DrugOptions { get; } = new();
    public ObservableCollection<OptionItem> SpecOptions { get; } = new();

    [ObservableProperty] private string? _drugText;
    [ObservableProperty] private OptionItem? _selectedSpec;
    [ObservableProperty] private bool _isDrugSuggestOpen;
    [ObservableProperty] private bool _isSpecSelected;
    [ObservableProperty] private int _poolPickCount = 1;

    [ObservableProperty] private bool _excludeRecent = true;
    [ObservableProperty] private bool _manualCodeOnlyMode;
    [ObservableProperty] private string _manualTraceCodesText = string.Empty;
    [ObservableProperty] private string? _generateSummary;
    [ObservableProperty] private bool _isBarcodeOperating;
    [ObservableProperty] private int _selectedTabIndex;

    public bool IsPoolTab => SelectedTabIndex == 0;
    public bool IsManualTab => SelectedTabIndex == 1;
    public bool IsDrugSpecFilterEnabled => IsPoolTab || !ManualCodeOnlyMode;
    public bool IsPoolPickEmpty => PoolRows.Count == 0;
    public bool IsPreviewEmpty => PreviewCards.Count == 0;
    public bool IsManualCodesEmpty => ManualLineCount == 0;
    public int ManualLineCount => EnumerateManualLines(ManualTraceCodesText).Count();

    private static IEnumerable<string> EnumerateManualLines(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            yield break;
        }

        foreach (var line in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!string.IsNullOrWhiteSpace(line))
            {
                yield return line;
            }
        }
    }

    public BarcodeGen(
        ITraceBarcodeService traceBarcode,
        ILookupCatalogService lookup,
        ITraceCodeRuleService traceCodeRule,
        IBarcodeGenSettingsService barcodeSettings,
        ITraceCodeBarcodeService barcodeRender,
        IFolderPickerService folderPicker,
        IToastService toast,
        IDialogService dialog)
    {
        _traceBarcode = traceBarcode;
        _lookup = lookup;
        _traceCodeRule = traceCodeRule;
        _barcodeSettings = barcodeSettings;
        _barcodeRender = barcodeRender;
        _folderPicker = folderPicker;
        _toast = toast;
        _dialog = dialog;
        _traceBarcode.AuditCleared += OnAuditCleared;
    }

    partial void OnSelectedTabIndexChanged(int value)
    {
        IsDrugSuggestOpen = false;
        OnPropertyChanged(nameof(IsPoolTab));
        OnPropertyChanged(nameof(IsManualTab));
        OnPropertyChanged(nameof(IsDrugSpecFilterEnabled));
    }

    partial void OnManualCodeOnlyModeChanged(bool value)
        => OnPropertyChanged(nameof(IsDrugSpecFilterEnabled));

    partial void OnManualTraceCodesTextChanged(string value)
    {
        OnPropertyChanged(nameof(IsManualCodesEmpty));
        OnPropertyChanged(nameof(ManualLineCount));
    }

    protected override Task ReloadCoreAsync(CancellationToken ct)
        => ReloadLookupAsync(ct);

    public override Task OnPageDeactivatedAsync(CancellationToken ct = default)
    {
        CancelOperateWork();
        return base.OnPageDeactivatedAsync(ct);
    }

    protected override void DisposeCore()
    {
        _traceBarcode.AuditCleared -= OnAuditCleared;
        CancelOperateWork();
        _operateCts.Dispose();
        DisposePreviewCards();
        base.DisposeCore();
    }

    private CancellationToken BeginOperateWork()
    {
        _operateCts.Cancel();
        _operateCts.Dispose();
        _operateCts = new CancellationTokenSource();
        return _operateCts.Token;
    }

    private void CancelOperateWork()
    {
        if (!_operateCts.IsCancellationRequested)
        {
            _operateCts.Cancel();
        }
    }

    private void OnAuditCleared()
    {
        ResetSessionCodes();
        ClearPendingExportAudit();
    }

    private void ResetSessionCodes()
    {
        _sessionCodes.Clear();
        _sessionCodeSet.Clear();
    }

    private async Task ReloadLookupAsync(CancellationToken ct)
    {
        if (IsLookupCatalogSuspended())
        {
            await RunOnUiAsync(OnLookupCatalogSuspended, DispatcherPriority.Background);
            return;
        }

        var drugs = await DrugCatalogRefresh.LoadAsync(_lookup, forceRefresh: false, ct).ConfigureAwait(false);
        await RunOnUiAsync(() =>
        {
            _drugCatalog = drugs;
            AutoCompleteFilter.RefreshVisibleOptions(DrugOptions, _drugCatalog, DrugText);
            if (DrugCatalogRefresh.IsMissing(drugs, NormalizeInput(DrugText)))
            {
                ResetDrugFilterState();
            }
        }, DispatcherPriority.Background);
    }

    protected override void OnLookupCatalogSuspended()
    {
        DrugOptions.Clear();
        _drugCatalog = [];
        ResetDrugFilterState();
    }

    [RelayCommand]
    private void RemovePoolRow(PoolRow? row)
    {
        if (row is null || IsBarcodeOperating)
        {
            return;
        }

        PoolRows.Remove(row);
        OnPropertyChanged(nameof(IsPoolPickEmpty));
    }

    [RelayCommand]
    private void ClearPoolRows()
    {
        if (SkipTrigger() || PoolRows.Count == 0 || IsBarcodeOperating)
        {
            return;
        }

        PoolRows.Clear();
        OnPropertyChanged(nameof(IsPoolPickEmpty));
    }

    [RelayCommand]
    private void ClearManualCodes()
    {
        if (SkipTrigger() || IsBarcodeOperating)
        {
            return;
        }

        ManualTraceCodesText = string.Empty;
    }

    [RelayCommand]
    private void ClearPreview()
    {
        if (SkipTrigger() || PreviewCards.Count == 0 || IsBarcodeOperating)
        {
            return;
        }

        if (!CanReplacePreview())
        {
            return;
        }

        IsBarcodeOperating = true;
        try
        {
            DisposePreviewCards();
            PreviewCards.Clear();
            _sessionCodes.Clear();
            _sessionCodeSet.Clear();
            GenerateSummary = null;
            OnPropertyChanged(nameof(IsPreviewEmpty));
        }
        finally
        {
            IsBarcodeOperating = false;
        }
    }

    [RelayCommand]
    private async Task OpenPreviewDetail(PreviewCard? card)
    {
        if (card is null)
        {
            return;
        }

        await _dialog.ShowBarcodePreviewDetail(new BarcodePreviewDetailArgs(
            card.Title,
            card.TraceCode,
            card.PngBytes,
            card.DetailItems)).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task GenerateAsync()
    {
        if (!CanPage || IsBarcodeOperating || !CanReplacePreview())
        {
            return;
        }

        IsBarcodeOperating = true;
        var operateCt = BeginOperateWork();
        try
        {
            var batchId = Guid.NewGuid();
            var options = _barcodeSettings.Current;
            var generatedAt = DateTimeOffset.Now;
            IReadOnlyList<TraceBarcodeLabelInput> labels;
            TraceBarcodePickResult? pickResult = null;
            var pickGap = 0;
            if (SelectedTabIndex == 0)
            {
                (labels, pickResult) = await BuildFromPoolAsync(batchId, options, operateCt).ConfigureAwait(true);
                pickGap = pickResult.Groups.Sum(static group => group.Gap);
                if (labels.Count == 0)
                {
                    DisposePreviewCards();
                    PreviewCards.Clear();
                    OnPropertyChanged(nameof(IsPreviewEmpty));
                    _toast.Warn("条码生成", "没有可用追溯码，请根据队列提示调整数量");
                    return;
                }

            }
            else
            {
                var manual = BuildFromManual();
                labels = manual.Labels;
                if (manual.SkippedDuplicates > 0)
                {
                    _toast.Warn("条码生成", $"跳过重复行 {manual.SkippedDuplicates} 条");
                }
            }

            if (labels.Count == 0)
            {
                _toast.Warn("条码生成", "没有可生成的条码");
                return;
            }

            var (cards, sessionAdds, skippedDup) = await RenderPreviewCardsAsync(
                    batchId,
                    generatedAt,
                    options,
                    labels,
                    pickResult,
                    operateCt)
                .ConfigureAwait(true);

            if (cards.Count == 0)
            {
                _toast.Warn(
                    "条码生成",
                    skippedDup > 0
                        ? "本次结果均在当前会话中生成过"
                        : "没有可生成的条码");
                return;
            }

            if (skippedDup > 0)
            {
                _toast.Warn("条码生成", $"跳过重复 {skippedDup} 条");
            }

            if (SelectedTabIndex != 0)
            {
                try
                {
                    await WriteAuditAsync(
                        batchId,
                        "preview",
                        cards.Select(static card => new TraceBarcodeAuditItem(
                            card.TraceCode,
                            card.DrugId,
                            card.Spec,
                            null,
                            true,
                            null))
                        .ToArray(),
                        operateCt).ConfigureAwait(true);
                }
                catch
                {
                    DisposeCards(cards);
                    throw;
                }
            }

            foreach (var code in sessionAdds)
            {
                TrackSessionCode(code);
            }

            DisposePreviewCards();
            PreviewCards.Clear();
            foreach (var card in cards)
            {
                PreviewCards.Add(card);
            }

            GenerateSummary = pickGap > 0
                ? $"已生成 {cards.Count} 张条码，缺口 {pickGap} 张"
                : $"已生成 {cards.Count} 张条码";
            OnPropertyChanged(nameof(IsPreviewEmpty));
            if (pickGap > 0)
            {
                _toast.Warn("条码生成", GenerateSummary);
            }
            else
            {
                _toast.Success("条码生成", GenerateSummary);
            }
        }
        catch (OperationCanceledException) when (operateCt.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            if (CanToastError(ex))
            {
                _toast.Error("条码生成失败", ex.Message);
            }
        }
        finally
        {
            IsBarcodeOperating = false;
        }
    }

    [RelayCommand]
    private async Task ExportAllAsync()
    {
        if (!CanPage)
        {
            return;
        }

        if (PreviewCards.Count == 0)
        {
            _toast.Warn("导出", "请先生成预览");
            return;
        }

        if (IsBarcodeOperating)
        {
            return;
        }

        IsBarcodeOperating = true;
        var operateCt = BeginOperateWork();
        try
        {
            if (_pendingExportAuditItems is { Count: > 0 } pendingItems
                && _pendingExportAuditBatchId is Guid pendingBatch)
            {
                if (!MatchesPendingExportAudit(PreviewCards))
                {
                    _toast.Error("导出审计", "预览已变化，无法补记先前导出审计。请重新导出文件。");
                    return;
                }

                try
                {
                    var retryCommandId = _pendingExportAuditCommandId ??= Guid.NewGuid();
                    await WriteAuditAsync(
                            pendingBatch,
                            "export",
                            pendingItems,
                            operateCt,
                            retryCommandId)
                        .ConfigureAwait(true);
                    ClearPendingExportAudit();
                    _toast.Success("导出审计", "已补记导出审计");
                    return;
                }
                catch (Exception ex)
                {
                    if (CanToastError(ex))
                    {
                        _toast.Error("导出审计失败", ex.Message);
                    }

                    return;
                }
            }

            var folder = await _folderPicker.PickFolderAsync(operateCt).ConfigureAwait(true);
            if (string.IsNullOrWhiteSpace(folder))
            {
                return;
            }

            var batchId = Guid.NewGuid();
            var auditItems = new List<TraceBarcodeAuditItem>(PreviewCards.Count);
            var successCount = 0;
            foreach (var card in PreviewCards.ToArray())
            {
                string? error = null;
                var success = false;
                try
                {
                    var path = ResolveExportPath(folder, card.FileName);
                    await File.WriteAllBytesAsync(path, card.PngBytes, operateCt).ConfigureAwait(true);
                    success = true;
                    successCount++;
                }
                catch (OperationCanceledException) when (operateCt.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                }

                auditItems.Add(new TraceBarcodeAuditItem(
                    card.TraceCode,
                    card.DrugId,
                    card.Spec,
                    card.FileName,
                    success,
                    error));
            }

            var exportCommandId = Guid.NewGuid();
            try
            {
                await WriteAuditAsync(batchId, "export", auditItems, operateCt, exportCommandId).ConfigureAwait(true);
                ClearPendingExportAudit();
            }
            catch (OperationCanceledException) when (operateCt.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _pendingExportAuditBatchId = batchId;
                _pendingExportAuditItems = auditItems;
                _pendingExportAuditCards = PreviewCards.ToArray();
                _pendingExportAuditCommandId = exportCommandId;
                if (CanToastError(ex))
                {
                    _toast.Error("导出审计失败", $"文件已写入，再次点击「全部导出」可重试审计：{ex.Message}");
                }

                return;
            }

            if (successCount == auditItems.Count)
            {
                _toast.Success("导出完成", $"已写入 {successCount} 个文件到\n{folder}");
                return;
            }

            if (successCount == 0)
            {
                _toast.Error("导出失败", "所有文件写入失败");
                return;
            }

            _toast.Warn(
                "部分导出成功",
                $"成功 {successCount}/{auditItems.Count} 个文件，详情见审计记录\n{folder}");
        }
        catch (OperationCanceledException) when (operateCt.IsCancellationRequested)
        {
        }
        finally
        {
            IsBarcodeOperating = false;
        }
    }

    private async Task<(IReadOnlyList<TraceBarcodeLabelInput> Labels, TraceBarcodePickResult Pick)> BuildFromPoolAsync(
        Guid batchId,
        BarcodeGenOptions options,
        CancellationToken ct)
    {
        if (PoolRows.Count == 0)
        {
            throw new InvalidOperationException("请先添加取码药品");
        }

        if (PoolRows.Count > TraceBarcodeService.MaxPickItems)
        {
            throw new InvalidOperationException($"取码队列最多 {TraceBarcodeService.MaxPickItems} 项");
        }

        var items = PoolRows
            .Select(row => new TraceBarcodePickItem(row.DrugId, row.Spec, Math.Max(1, row.Count)))
            .ToArray();

        var totalRequested = items.Sum(static item => item.Count);
        EnsureWithinPreviewLimit(totalRequested);

        var excludeDays = ExcludeRecent ? options.ExcludeRecentDays : 0;
        var pick = await _traceBarcode.PickAsync(
                new TraceBarcodePickRequest(
                    batchId,
                    GetOperatorName(),
                    items,
                    excludeDays,
                    _sessionCodes.ToArray()),
                ct)
            .ConfigureAwait(true);

        var labels = new List<TraceBarcodeLabelInput>();
        foreach (var row in PoolRows)
        {
            row.ResetPickStatus();
        }

        foreach (var group in pick.Groups)
        {
            var row = PoolRows.FirstOrDefault(r =>
                string.Equals(r.DrugId, group.DrugId, StringComparison.Ordinal)
                && string.Equals(r.Spec, group.Spec, StringComparison.Ordinal));
            if (row is not null)
            {
                row.PickStatus = group.Gap > 0 ? PoolPickStatus.Gap : PoolPickStatus.Ok;
                row.StatusHint = group.Gap > 0
                    ? $"请求 {group.Requested}，可用 {group.Picked}（缺口 {group.Gap}）"
                    : $"已选取 {group.Picked} 条";
            }

            foreach (var code in group.Codes)
            {
                labels.Add(new TraceBarcodeLabelInput(
                    code.TraceCode,
                    code.DrugId,
                    code.Spec,
                    code.Qty,
                    code.Remain,
                    code.InDate));
            }
        }

        return (labels, pick);
    }

    private (IReadOnlyList<TraceBarcodeLabelInput> Labels, int SkippedDuplicates) BuildFromManual()
    {
        var rule = new TraceCodeValidationRule(
            _traceCodeRule.Current.RequiredLength,
            _traceCodeRule.Current.Pattern);
        var codes = EnumerateManualLines(ManualTraceCodesText).ToArray();
        if (codes.Length == 0)
        {
            throw new InvalidOperationException("请填写追溯码");
        }

        string? drug = null;
        string? spec = null;
        if (!ManualCodeOnlyMode)
        {
            drug = NormalizeInput(DrugText);
            spec = NormalizeInput(SelectedSpec?.Raw);
            if (drug is null || spec is null)
            {
                throw new InvalidOperationException("请填写药品与规格，或启用仅追溯码条码");
            }
        }

        var labels = new List<TraceBarcodeLabelInput>(codes.Length);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var skippedDuplicates = 0;
        foreach (var code in codes)
        {
            if (!seen.Add(code))
            {
                skippedDuplicates++;
                continue;
            }

            if (!TraceCodeAnalyzer.TryValidateFormat(code, rule, out var error))
            {
                throw new InvalidOperationException(error ?? $"追溯码格式不正确：{code}");
            }

            labels.Add(new TraceBarcodeLabelInput(code, drug, spec, null, null, null));
        }

        EnsureWithinPreviewLimit(labels.Count);
        return (labels, skippedDuplicates);
    }

    private static void EnsureWithinPreviewLimit(int count)
    {
        if (count > TraceBarcodeService.MaxPreviewLabels)
        {
            throw new InvalidOperationException($"单次最多生成 {TraceBarcodeService.MaxPreviewLabels} 张条码");
        }
    }

    internal static string ResolveExportPath(string folder, string fileName)
    {
        var root = Path.GetFullPath(
            folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar);
        var fullPath = Path.GetFullPath(Path.Combine(folder, fileName));
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!fullPath.StartsWith(root, comparison))
        {
            throw new InvalidOperationException($"非法导出路径：{fileName}");
        }

        return fullPath;
    }

    private async Task<(
        List<PreviewCard> Cards,
        List<string> SessionAdds,
        int SkippedDup)> RenderPreviewCardsAsync(
        Guid batchId,
        DateTimeOffset generatedAt,
        BarcodeGenOptions options,
        IReadOnlyList<TraceBarcodeLabelInput> labels,
        TraceBarcodePickResult? pickResult,
        CancellationToken ct)
    {
        var batchCodes = new HashSet<string>(_sessionCodeSet, StringComparer.Ordinal);
        var uniqueLabels = new List<TraceBarcodeLabelInput>(labels.Count);
        var skippedDup = 0;
        foreach (var label in labels)
        {
            if (!batchCodes.Add(label.TraceCode))
            {
                skippedDup++;
                continue;
            }

            uniqueLabels.Add(label);
        }

        if (uniqueLabels.Count == 0)
        {
            return ([], [], skippedDup);
        }

        var drafts = await Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            var rendered = new List<RenderedPreviewDraft>(uniqueLabels.Count);
            foreach (var label in uniqueLabels)
            {
                ct.ThrowIfCancellationRequested();
                var png = _barcodeRender.RenderPng(label, options, generatedAt);
                var fileName = _barcodeRender.BuildFileName(label, options.Export);
                var title = string.IsNullOrWhiteSpace(label.DrugId)
                    ? label.TraceCode
                    : DrugLabel.Format(label.DrugId, label.Spec ?? string.Empty);
                var detailItems = BuildDetailItems(batchId, generatedAt, label, pickResult);
                rendered.Add(new RenderedPreviewDraft(
                    label.TraceCode,
                    label.DrugId,
                    label.Spec,
                    title,
                    png,
                    fileName,
                    detailItems));
            }

            return rendered;
        }, ct).ConfigureAwait(true);

        var cards = new List<PreviewCard>(drafts.Count);
        var sessionAdds = new List<string>(drafts.Count);
        try
        {
            foreach (var draft in drafts)
            {
                cards.Add(new PreviewCard(
                    draft.TraceCode,
                    draft.DrugId,
                    draft.Spec,
                    draft.Title,
                    draft.Png,
                    draft.FileName,
                    draft.DetailItems)
                {
                    PreviewImage = LoadBitmap(draft.Png)
                });
                sessionAdds.Add(draft.TraceCode);
            }
        }
        catch
        {
            DisposeCards(cards);
            throw;
        }

        return (cards, sessionAdds, skippedDup);
    }

    private bool CanReplacePreview()
    {
        if (_pendingExportAuditItems is null)
        {
            return true;
        }

        _toast.Warn("导出审计", "请先再次点击「全部导出」补记审计，再更新或清空预览");
        return false;
    }

    private bool MatchesPendingExportAudit(IReadOnlyList<PreviewCard> cards)
    {
        if (_pendingExportAuditCards is null || _pendingExportAuditCards.Count != cards.Count)
        {
            return false;
        }

        for (var i = 0; i < cards.Count; i++)
        {
            if (!string.Equals(cards[i].TraceCode, _pendingExportAuditCards[i].TraceCode, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private void ClearPendingExportAudit()
    {
        _pendingExportAuditBatchId = null;
        _pendingExportAuditCommandId = null;
        _pendingExportAuditItems = null;
        _pendingExportAuditCards = null;
    }

    private sealed record RenderedPreviewDraft(
        string TraceCode,
        string? DrugId,
        string? Spec,
        string Title,
        byte[] Png,
        string FileName,
        IReadOnlyList<InfoDetailItem> DetailItems);

    private static IReadOnlyList<InfoDetailItem> BuildDetailItems(
        Guid batchId,
        DateTimeOffset generatedAt,
        TraceBarcodeLabelInput label,
        TraceBarcodePickResult? pickResult)
    {
        var items = new List<InfoDetailItem>
        {
            new("追溯码", label.TraceCode),
            new("批次", batchId.ToString()),
            new("生成时间", generatedAt.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)),
            new("来源", pickResult is null ? "手动生成" : "从库存取码")
        };

        if (!string.IsNullOrWhiteSpace(label.DrugId))
        {
            items.Add(new InfoDetailItem("药品", label.DrugId));
        }

        if (!string.IsNullOrWhiteSpace(label.Spec))
        {
            items.Add(new InfoDetailItem("规格", label.Spec));
        }

        if (label.Qty is > 0)
        {
            items.Add(new InfoDetailItem("单盒数量", label.Qty.Value.ToString(CultureInfo.InvariantCulture)));
        }

        if (label.Remain is not null)
        {
            items.Add(new InfoDetailItem("余量", label.Remain.Value.ToString(CultureInfo.InvariantCulture)));
        }

        if (label.InDate is not null)
        {
            items.Add(new InfoDetailItem(
                "入库时间",
                label.InDate.Value.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)));
        }

        if (pickResult is not null)
        {
            var group = pickResult.Groups.FirstOrDefault(g =>
                string.Equals(g.DrugId, label.DrugId, StringComparison.Ordinal)
                && string.Equals(g.Spec, label.Spec, StringComparison.Ordinal));
            if (group is not null)
            {
                items.Add(new InfoDetailItem("请求数量", group.Requested.ToString(CultureInfo.InvariantCulture)));
                items.Add(new InfoDetailItem("实际选取", group.Picked.ToString(CultureInfo.InvariantCulture)));
                items.Add(new InfoDetailItem("缺口", group.Gap.ToString(CultureInfo.InvariantCulture)));
            }
        }

        return items;
    }

    private async Task WriteAuditAsync(
        Guid batchId,
        string action,
        IReadOnlyList<TraceBarcodeAuditItem> items,
        CancellationToken ct,
        Guid? commandId = null)
    {
        if (items.Count == 0)
        {
            return;
        }

        await _traceBarcode.AuditAsync(
                new TraceBarcodeAuditRequest(batchId, action, GetOperatorName(), items, commandId),
                ct)
            .ConfigureAwait(true);
    }

    private static string GetOperatorName()
        => $"{Environment.UserName}@{Environment.MachineName}";

    private void TrackSessionCode(string code)
    {
        if (!_sessionCodeSet.Add(code))
        {
            return;
        }

        _sessionCodes.Add(code);
        while (_sessionCodes.Count > TraceBarcodeService.MaxTotalPickCount)
        {
            var removed = _sessionCodes[0];
            _sessionCodes.RemoveAt(0);
            _sessionCodeSet.Remove(removed);
        }
    }

    private void DisposePreviewCards() => DisposeCards(PreviewCards);

    private static void DisposeCards(IEnumerable<PreviewCard> cards)
    {
        foreach (var card in cards)
        {
            card.Dispose();
        }
    }

    private static Bitmap LoadBitmap(byte[] png)
    {
        using var ms = new MemoryStream(png);
        return new Bitmap(ms);
    }
}
