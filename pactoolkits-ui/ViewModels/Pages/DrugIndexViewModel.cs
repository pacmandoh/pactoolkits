using System;
using Avalonia.Collections;
using System.Collections.Specialized;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using pactoolkits_ui.Contracts;
using Material.Icons;
using pactoolkits_ui.Repositories;
using pactoolkits_ui.Services;
using Pactoolkits.ViewModels.Pages.DrugIndex;

namespace pactoolkits_ui.ViewModels.Pages;

public sealed partial class DrugIndexViewModel : AppPageBase
{
    public override string DisplayName => "药品信息维护";
    public override MaterialIconKind Icon => MaterialIconKind.Drugs;
    public override int Index => 2;
    public override ICommand RefreshCommand => _localRefreshCommand;

    public override ICommand ImportCommand => ImportDataCommand;
    public override ICommand ExportCommand => ExportDataCommand;

    private bool CanIo() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanIo))]
    private async Task ImportDataAsync()
    {
        if (ShouldSkipTrigger())
            return;

        await _dialog.Warn("未实现", "导入功能稍后接入 FilePicker/映射");
    }

    [RelayCommand(CanExecute = nameof(CanIo))]
    private async Task ExportDataAsync()
    {
        if (ShouldSkipTrigger())
            return;

        await _dialog.Warn("未实现", "导出功能稍后接入格式选择/保存路径");
    }

    public sealed partial class DrugRow : ObservableObject
    {
        public DrugRow(DrugIndexDto dto)
        {
            DrugId = dto.DrugId;
            Spec = dto.Spec;
            Qty = dto.Qty;
            RuleKey = dto.RuleKey;
            PreTc = dto.PreTc;
            Note = dto.Note;
            CreatedAt = dto.CreatedAt;
            UpdatedAt = dto.UpdatedAt;
            Version = dto.Version;
            RecalcFlags();
        }

        public string RowState
        {
            get
            {
                if (IsDeprecated && IsNoSplit) return "both";
                if (IsDeprecated) return "deprecated";
                if (IsNoSplit) return "nosplit";
                return "";
            }
        }

        public string DrugId { get; }
        public string Spec { get; }
        public int Qty { get; }
        public string? RuleKey { get; }
        public string? PreTc { get; }

        [ObservableProperty] private string? _note;
        [ObservableProperty] private string? _notePreview;

        public string EffectiveNote => NotePreview ?? Note ?? string.Empty;

        public DateTimeOffset CreatedAt { get; }
        public DateTimeOffset? UpdatedAt { get; }
        public long Version { get; }

        [ObservableProperty] private bool _isDeprecated;
        [ObservableProperty] private bool _isNoSplit;

        public bool IsHitBoth => IsDeprecated && IsNoSplit;

        partial void OnNoteChanged(string? value)
        {
            OnPropertyChanged(nameof(EffectiveNote));
            RecalcFlags();
        }

        partial void OnNotePreviewChanged(string? value)
        {
            OnPropertyChanged(nameof(EffectiveNote));
            RecalcFlags();
        }

        public void RecalcFlags()
        {
            var note = EffectiveNote;
            IsDeprecated = note.Contains("弃用", StringComparison.Ordinal);
            IsNoSplit = note.Contains("未拆零", StringComparison.Ordinal);
            OnPropertyChanged(nameof(IsHitBoth));
            OnPropertyChanged(nameof(RowState));
        }

        public DrugIndexDto ToDto() => new(
            DrugId: DrugId,
            Spec: Spec,
            Qty: Qty,
            RuleKey: RuleKey,
            PreTc: PreTc,
            Note: Note,
            CreatedAt: CreatedAt,
            UpdatedAt: UpdatedAt,
            Version: Version
        );
    }

    private readonly IDrugIndexRepo _repo;
    private readonly IToastService _toast;
    private readonly IDialogService _dialog;
    private readonly IClipboardService _clipboard;
    private readonly InventoryOverviewViewModel _inventoryOverview;
    private readonly ScanCodeViewModel _scanCode;
    private readonly AsyncRelayCommand _localRefreshCommand;
    private DrugIndexQuery _query = new(null);
    private int _reloadEpoch;
    private int _lastSuccessfulReloadEpoch;

    public AvaloniaList<DrugRow> Items { get; } = new();
    public bool IsItemsEmpty => Items.Count == 0;

    public string? Keyword
    {
        get => _query.Keyword;
        set
        {
            _query = new DrugIndexQuery(Keyword: value);
            OnPropertyChanged();
        }
    }

    [ObservableProperty] private DrugRow? _selected;
    [ObservableProperty] private bool _hasSelection;
    [ObservableProperty] private bool _hasEditor;
    [ObservableProperty] private bool _isListBusy;
    partial void OnIsListBusyChanged(bool value) => NotifyAllCommands();

    [ObservableProperty] private string _editDrugId = "";
    [ObservableProperty] private string _editSpec = "";
    [ObservableProperty] private int? _editQty;
    [ObservableProperty] private string? _editRuleKey;
    [ObservableProperty] private string? _editPreTc;
    [ObservableProperty] private string? _editNote;

    [ObservableProperty] private DateTimeOffset _createdAt;
    [ObservableProperty] private DateTimeOffset? _updatedAt;

    private string? _originDrugId;
    private string? _originSpec;

    [ObservableProperty] private bool _isDirty;
    public bool HasPendingChanges => IsDirty;
    partial void OnIsDirtyChanged(bool value) => OnPropertyChanged(nameof(HasPendingChanges));

    private DrugIndexDto? _loadedSnapshot;
    private bool _suppressSelectionGuard;
    private bool _preserveEditorOnSelectionRevert;
    private DrugRow? _selectionBeforeChange;

    [ObservableProperty] private bool _isDeprecated;
    [ObservableProperty] private bool _isNoSplit;

    public string CreatedAtLocalText => FormatChinaTime(CreatedAt);
    public string UpdatedAtLocalText => UpdatedAt is null ? "" : FormatChinaTime(UpdatedAt.Value);

    private static string FormatChinaTime(DateTimeOffset dt)
        => dt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.CurrentCulture);

    partial void OnCreatedAtChanged(DateTimeOffset value) => OnPropertyChanged(nameof(CreatedAtLocalText));
    partial void OnUpdatedAtChanged(DateTimeOffset? value) => OnPropertyChanged(nameof(UpdatedAtLocalText));



    public DrugIndexViewModel(
        IDrugIndexRepo repo,
        IToastService toast,
        IDialogService dialog,
        IClipboardService clipboard,
        InventoryOverviewViewModel inventoryOverview,
        ScanCodeViewModel scanCode)
    {
        _repo = repo;
        _toast = toast;
        _dialog = dialog;
        _clipboard = clipboard;
        _inventoryOverview = inventoryOverview;
        _scanCode = scanCode;
        _localRefreshCommand = new AsyncRelayCommand(ReloadAsync, CanRefreshLocal);
        Items.CollectionChanged += OnItemsCollectionChanged;

        // Initial data load is posted to UI loop to avoid blocking page activation.
        Dispatcher.UIThread.Post(() => _ = ReloadAsync());
    }

    private void OnItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => OnPropertyChanged(nameof(IsItemsEmpty));

    private bool CanRefreshLocal() => !IsBusy && !IsListBusy;

    partial void OnSelectedChanging(DrugRow? value)
    {
        _selectionBeforeChange = Selected;
    }

    private DrugRow? FindRow(string drugId, string spec)
        => Items.FirstOrDefault(x => x.DrugId == drugId && x.Spec == spec);

    private void ReplaceOrInsertRowInPlace(DrugIndexDto dto)
    {
        if (!string.IsNullOrWhiteSpace(_originDrugId) && !string.IsNullOrWhiteSpace(_originSpec))
        {
            for (var i = 0; i < Items.Count; i++)
            {
                var r = Items[i];
                if (r.DrugId == _originDrugId && r.Spec == _originSpec)
                {
                    Items[i] = new DrugRow(dto);
                    return;
                }
            }
        }

        Items.Insert(0, new DrugRow(dto));
    }

    partial void OnSelectedChanged(DrugRow? value)
    {
        if (_suppressSelectionGuard)
        {
            if (_preserveEditorOnSelectionRevert)
            {
                _preserveEditorOnSelectionRevert = false;
                return;
            }
            ApplySelection(value);
            return;
        }

        var next = value;
        var prev = _selectionBeforeChange;
        var nextDrugId = next?.DrugId;
        var nextSpec = next?.Spec;

        Dispatcher.UIThread.Post(() => _ = HandleSelectionChangeAsync(prev, next, nextDrugId, nextSpec));
    }

    private async Task HandleSelectionChangeAsync(DrugRow? prev, DrugRow? next, string? nextDrugId, string? nextSpec)
    {
        if (HasEditor && HasChanges())
        {
            var choice = await _dialog.Confirm3(
                "有未保存修改",
                "当前修改尚未保存，切换会丢失修改",
                primaryText: "保存并切换",
                secondaryText: "放弃修改",
                cancelText: "取消");

            switch (choice)
            {
                case 1:
                    {
                        var ok = await SaveInternalAsync(reselectSavedRow: false, refreshAfterSave: false);
                        if (!ok)
                        {
                            RevertSelection(prev);
                            return;
                        }

                        if (!string.IsNullOrWhiteSpace(nextDrugId) && !string.IsNullOrWhiteSpace(nextSpec))
                            next = FindRow(nextDrugId, nextSpec);
                        break;
                    }
                case 2:
                    DiscardDraft();
                    break;
                default:
                    RevertSelection(prev);
                    return;
            }
        }

        if (prev is not null)
            prev.NotePreview = null;

        _suppressSelectionGuard = true;
        try
        {
            Selected = next;
        }
        finally
        {
            _suppressSelectionGuard = false;
        }

        ApplySelection(next);
    }

    private void ApplySelection(DrugRow? value)
    {
        HasSelection = value is not null;
        HasEditor = HasSelection;

        if (value is null)
        {
            if (!HasEditor)
                ClearEditor(keepEditorVisible: false);
            return;
        }

        LoadToEditor(value);
    }

    private void RevertSelection(DrugRow? prev)
    {
        _preserveEditorOnSelectionRevert = true;
        _suppressSelectionGuard = true;
        try
        {
            Selected = prev;
        }
        finally
        {
            _suppressSelectionGuard = false;
        }
    }

    partial void OnEditDrugIdChanged(string value) => MarkDirty();
    partial void OnEditSpecChanged(string value) => MarkDirty();
    partial void OnEditQtyChanged(int? value) => MarkDirty();
    partial void OnEditRuleKeyChanged(string? value) => MarkDirty();
    partial void OnEditPreTcChanged(string? value) => MarkDirty();

    partial void OnEditNoteChanged(string? value)
    {
        RecalcEditorFlags(value);

        if (Selected is not null)
            Selected.NotePreview = value;

        MarkDirty();
    }

    private void RecalcEditorFlags(string? note)
    {
        var s = note ?? string.Empty;
        IsDeprecated = s.Contains("弃用", StringComparison.Ordinal);
        IsNoSplit = s.Contains("未拆零", StringComparison.Ordinal);
    }

    private void MarkDirty()
    {
        IsDirty = HasChanges();
        NotifyCommands(SaveCommand, DeleteCommand);
    }

    private void LoadToEditor(DrugRow row)
    {
        EditDrugId = row.DrugId;
        EditSpec = row.Spec;
        EditQty = row.Qty;
        EditRuleKey = row.RuleKey;
        EditPreTc = row.PreTc;
        EditNote = row.Note;
        CreatedAt = row.CreatedAt;
        UpdatedAt = row.UpdatedAt;

        row.NotePreview = null;

        _originDrugId = row.DrugId;
        _originSpec = row.Spec;

        _loadedSnapshot = row.ToDto();

        HasSelection = true;
        HasEditor = true;
        RecalcEditorFlags(EditNote);

        IsDirty = false;
        NotifyAllCommands();
    }

    private void ClearEditor(bool keepEditorVisible)
    {
        EditDrugId = "";
        EditSpec = "";
        EditQty = null;
        EditRuleKey = null;
        EditPreTc = null;
        EditNote = null;
        CreatedAt = default;
        UpdatedAt = null;

        _originDrugId = null;
        _originSpec = null;

        _loadedSnapshot = null;

        if (Selected is not null)
            Selected.NotePreview = null;

        HasSelection = false;
        HasEditor = keepEditorVisible;
        IsDeprecated = false;
        IsNoSplit = false;

        IsDirty = false;
        NotifyAllCommands();
    }

    private bool HasChanges()
    {
        if (_loadedSnapshot is null)
        {
            if (!string.IsNullOrWhiteSpace(EditDrugId)) return true;
            if (!string.IsNullOrWhiteSpace(EditSpec)) return true;
            if (EditQty is > 0) return true;
            if (!string.IsNullOrWhiteSpace(EditRuleKey)) return true;
            if (!string.IsNullOrWhiteSpace(EditPreTc)) return true;
            if (!string.IsNullOrWhiteSpace(EditNote)) return true;
            return false;
        }

        static string N(string? s) => (s ?? string.Empty).Trim();

        return N(_loadedSnapshot.DrugId) != N(EditDrugId)
               || N(_loadedSnapshot.Spec) != N(EditSpec)
               || _loadedSnapshot.Qty != (EditQty ?? 0)
               || N(_loadedSnapshot.RuleKey) != N(EditRuleKey)
               || N(_loadedSnapshot.PreTc) != N(EditPreTc)
               || N(_loadedSnapshot.Note) != N(EditNote);
    }

    private void DiscardDraft()
    {
        if (_loadedSnapshot is null)
        {
            ClearEditor(keepEditorVisible: HasEditor);
            return;
        }

        var dto = _loadedSnapshot;
        EditDrugId = dto.DrugId;
        EditSpec = dto.Spec;
        EditQty = dto.Qty;
        EditRuleKey = dto.RuleKey;
        EditPreTc = dto.PreTc;
        EditNote = dto.Note;
        CreatedAt = dto.CreatedAt;
        UpdatedAt = dto.UpdatedAt;

        RecalcEditorFlags(EditNote);

        if (Selected is not null)
            Selected.NotePreview = EditNote;

        IsDirty = false;
        NotifyAllCommands();
    }

    private bool CanSave()
        => !IsBusy
           && HasEditor
           && !string.IsNullOrWhiteSpace(EditDrugId)
           && !string.IsNullOrWhiteSpace(EditSpec)
           && EditQty is > 0
           && HasChanges();

    private bool CanDelete()
        => !IsBusy
           && !string.IsNullOrWhiteSpace(_originDrugId)
           && !string.IsNullOrWhiteSpace(_originSpec);

    private bool CanNewItem()
        => !IsBusy && !IsListBusy;

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        if (ShouldSkipTrigger(milliseconds: 800))
            return;

        await SaveInternalAsync(reselectSavedRow: true, refreshAfterSave: false);
    }

    private async Task<bool> SaveInternalAsync(bool reselectSavedRow = true, bool refreshAfterSave = true)
    {
        IsBusy = true;

        try
        {
            var inputDrugId = NormalizeInput(EditDrugId);
            var inputSpec = NormalizeInput(EditSpec);

            var drugId = inputDrugId ?? throw new InvalidOperationException("药品名不能为空");
            var spec = inputSpec ?? throw new InvalidOperationException("规格不能为空");

            var isNew = string.IsNullOrWhiteSpace(_originDrugId) && string.IsNullOrWhiteSpace(_originSpec);
            if (isNew)
            {
                var exists = await _repo.ExistsAsync(drugId, spec, default);
                if (exists)
                {
                    await _dialog.Warn("名称/规格重复",
                        $"已存在相同记录：\nDrugId = {drugId}\nSpec = {spec}\n\n请改成“编辑已有记录”或修改 DrugId/Spec");
                    return false;
                }
            }

            var note = NormalizeInput(EditNote);

            var dto = new DrugIndexDto(
                DrugId: drugId,
                Spec: spec,
                Qty: EditQty!.Value,
                RuleKey: NormalizeInput(EditRuleKey),
                PreTc: NormalizeInput(EditPreTc),
                Note: note,
                CreatedAt: CreatedAt == default ? DateTimeOffset.UtcNow : CreatedAt,
                UpdatedAt: DateTimeOffset.UtcNow,
                Version: _loadedSnapshot?.Version ?? 0
            );

            var expectedVersion = isNew ? null : _loadedSnapshot?.Version;
            DrugIndexDto saved;
            try
            {
                saved = await _repo.UpsertAsync(dto, expectedVersion, default);
            }
            catch (DrugIndexConcurrencyException cx)
            {
                LogWarn("drug_index.save.concurrency_conflict", "Detected optimistic concurrency conflict", cx);
                await _dialog.Warn("保存冲突", "该记录已被其他终端修改，请先刷新后再编辑");

                if (cx.Current is not null)
                    await ReloadAndReselectAsync(cx.Current.DrugId, cx.Current.Spec);
                else
                    await ReloadAsync();

                return false;
            }

            Dispatcher.UIThread.Post(() => _toast.Success("已保存", $"{drugId} / {spec}"));

            _originDrugId = drugId;
            _originSpec = spec;
            _loadedSnapshot = saved;

            IsDirty = false;
            NotifyAllCommands();

            var hasActiveKeyword = !string.IsNullOrWhiteSpace(NormalizeInput(_query.Keyword));
            if (!refreshAfterSave && !hasActiveKeyword)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    ReplaceOrInsertRowInPlace(saved);

                    if (reselectSavedRow)
                        Selected = FindRow(drugId, spec);
                }, DispatcherPriority.Normal);

                _inventoryOverview.NotifyDrugIndexChanged();
                _scanCode.NotifyDrugIndexChanged();
                return true;
            }

            if (reselectSavedRow)
                await ReloadAndReselectAsync(drugId, spec);
            else
                await ReloadAsync();

            _inventoryOverview.NotifyDrugIndexChanged();
            _scanCode.NotifyDrugIndexChanged();

            return true;
        }
        catch (Exception ex)
        {
            LogError("drug_index.save.fail", "Failed to save drug row", ex);
            var drug = NormalizeInput(EditDrugId) ?? (EditDrugId ?? string.Empty).Trim();
            var spec = NormalizeInput(EditSpec) ?? (EditSpec ?? string.Empty).Trim();
            var target = $"{drug} / {spec}".Trim();
            Dispatcher.UIThread.Post(() => _toast.Error("保存失败", $"{target}：{ex.Message}"));
            return false;
        }
        finally
        {
            IsBusy = false;
            NotifyAllCommands();
        }
    }

    private Task ReloadAsync()
        => RunLocalReloadAsync(
            setBusy: v => IsListBusy = v,
            action: ReloadCoreAsync,
            onFinished: NotifyAllCommands);

    private async Task ReloadAndReselectAsync(string drugId, string spec)
    {
        await ReloadAsync();
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            Selected = FindRow(drugId, spec);
        }, DispatcherPriority.Normal);
    }
    protected override async Task ReloadCoreAsync(CancellationToken ct)
    {
        // Epoch marks this reload attempt and helps suppress stale error toasts.
        var epoch = Interlocked.Increment(ref _reloadEpoch);

        try
        {
            // Reason: Capture query state before async work to avoid stale reads.
            var query = _query;
            var rows = await _repo.SearchAsync(query.Keyword, limit: 1000, ct);
            var newRows = rows.Select(dto => new DrugRow(dto)).ToList();
            var prevSelectedDrugId = Selected?.DrugId;
            var prevSelectedSpec = Selected?.Spec;

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _suppressSelectionGuard = true;
                try
                {
                    if (Selected is not null)
                        Selected.NotePreview = null;

                    Selected = null;
                    _selectionBeforeChange = null;

                    _originDrugId = null;
                    _originSpec = null;
                    _loadedSnapshot = null;

                    ClearEditor(keepEditorVisible: false);

                    Items.Clear();
                    Items.AddRange(newRows);

                    if (!string.IsNullOrWhiteSpace(prevSelectedDrugId) && !string.IsNullOrWhiteSpace(prevSelectedSpec))
                    {
                        Selected = Items.FirstOrDefault(x =>
                            x.DrugId == prevSelectedDrugId && x.Spec == prevSelectedSpec);
                        if (Selected is not null)
                            ApplySelection(Selected);
                    }
                }
                finally
                {
                    _suppressSelectionGuard = false;
                }
            }, DispatcherPriority.Normal);

            Interlocked.Exchange(ref _lastSuccessfulReloadEpoch, epoch);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            LogError("drug_index.reload.fail", "Failed to reload drug index", ex);
            if (IsDbTransportError(ex))
            {
                SignalDbDisconnected();
                await WaitForReconnectOrTimeoutAsync(ct).ConfigureAwait(false);
            }
            else
            {
                await Task.Delay(180, ct).ConfigureAwait(false);
            }

            if (ct.IsCancellationRequested)
                return;

            if (Volatile.Read(ref _lastSuccessfulReloadEpoch) > epoch)
                return;

            if (!IsDbConnected)
                return;

            Dispatcher.UIThread.Post(() =>
                _toast.Error("药品信息加载失败", ex.Message));
        }
    }

    protected override void OnReloadFinished()
        => NotifyAllCommands();

    [RelayCommand]
    private Task SearchAsync()
    {
        if (ShouldSkipTrigger(milliseconds: 300))
            return Task.CompletedTask;
        return ReloadAsync();
    }

    [RelayCommand]
    private async Task ClearSearchAsync()
    {
        if (ShouldSkipTrigger(milliseconds: 300))
            return;

        Keyword = null;
        await ReloadAsync();
    }

    [RelayCommand(CanExecute = nameof(CanNewItem))]
    private void NewItem()
    {
        if (ShouldSkipTrigger(milliseconds: 250))
            return;

        if (Selected is not null)
            Selected.NotePreview = null;

        _suppressSelectionGuard = true;
        try
        {
            Selected = null;
            _selectionBeforeChange = null;
        }
        finally
        {
            _suppressSelectionGuard = false;
        }
        _loadedSnapshot = null;

        ClearEditor(keepEditorVisible: true);
        HasEditor = true;

        IsDirty = false;
        NotifyCommands(SaveCommand);
    }

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private async Task DeleteAsync()
    {
        if (ShouldSkipTrigger(milliseconds: 800))
            return;

        if (string.IsNullOrWhiteSpace(_originDrugId) || string.IsNullOrWhiteSpace(_originSpec))
            return;

        var deleteDrugId = _originDrugId!;
        var deleteSpec = _originSpec!;

        var ok = await _dialog.Confirm("删除药品规格",
            $"确认删除？\n{deleteDrugId} / {deleteSpec}\n\n注意：trace_pool / trace_txn 外键会阻止删除正在引用的记录");

        if (!ok) return;

        IsBusy = true;
        try
        {
            await _repo.DeleteAsync(deleteDrugId, deleteSpec, default);
            Dispatcher.UIThread.Post(() => _toast.Success("已删除", $"{deleteDrugId} / {deleteSpec}"));
            Selected = null;
            ClearEditor(keepEditorVisible: false);
            await ReloadAsync();
            _inventoryOverview.NotifyDrugIndexChanged();
            _scanCode.NotifyDrugIndexChanged();
        }
        catch (Exception ex)
        {
            LogError("drug_index.delete.fail", "Failed to delete drug row", ex, new { deleteDrugId, deleteSpec });
            Dispatcher.UIThread.Post(() => _toast.Error("删除失败", ex.Message));
        }
        finally
        {
            IsBusy = false;
            NotifyAllCommands();
        }
    }

    [RelayCommand]
    private async Task CopyNameAsync(DrugRow? row)
    {
        if (ShouldSkipTrigger("drug.copy.name", 350))
            return;

        row ??= Selected;
        if (row is null) return;
        await _clipboard.SetTextAsync(row.DrugId);
        Dispatcher.UIThread.Post(() => _toast.Info("已复制", "名称(DrugId) 已复制到剪贴板"));
    }

    [RelayCommand]
    private async Task CopyCodeAsync(DrugRow? row)
    {
        if (ShouldSkipTrigger("drug.copy.code", 350))
            return;

        row ??= Selected;
        if (row is null) return;
        var code = row.RuleKey ?? row.PreTc ?? string.Empty;
        await _clipboard.SetTextAsync(code);
        Dispatcher.UIThread.Post(() => _toast.Info("已复制", "编码(RuleKey/PreTc) 已复制到剪贴板"));
    }

    [RelayCommand]
    private void ToggleDeprecated(object? arg)
    {
        if (arg is DrugRow row)
        {
            Selected = row;
        }

        ApplyToggleDeprecated();
    }

    [RelayCommand]
    private void ToggleNoSplit(object? arg)
    {
        if (arg is DrugRow row)
        {
            Selected = row;
        }

        ApplyToggleNoSplit();
    }

    private void ApplyToggleDeprecated()
    {
        if (!HasEditor) return;
        EditNote = ToggleToken(EditNote, "弃用");
    }

    private void ApplyToggleNoSplit()
    {
        if (!HasEditor) return;
        EditNote = ToggleToken(EditNote, "未拆零");
    }

    private static string? ToggleToken(string? note, string token)
    {
        var s = (note ?? string.Empty).Trim();
        if (s.Length == 0)
            return token;

        if (s.Contains(token, StringComparison.Ordinal))
        {
            s = s.Replace(token, string.Empty, StringComparison.Ordinal);
            s = string.Join(' ', s.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));
            return s.Length == 0 ? null : s;
        }

        return s + " " + token;
    }

    private void NotifyAllCommands()
    {
        NotifyCommands(_localRefreshCommand, NewItemCommand, SaveCommand, DeleteCommand, ImportDataCommand, ExportDataCommand);
    }

    public override void Dispose()
    {
        Items.CollectionChanged -= OnItemsCollectionChanged;
        base.Dispose();
    }

}
