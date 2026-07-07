using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using global::Avalonia.Threading;
using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;
using PacToolkits.Application.Services;
using PacToolkits.Desktop.Avalonia.Common;
using PacToolkits.Desktop.Avalonia.Contracts;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure.Dialogs;

namespace PacToolkits.Desktop.Avalonia.ViewModels.Pages;

public sealed partial class DrugIndex : AppPageBase, IDrugIndexRefreshPage
{
    private const string OpsScope = UnlockScopes.SharedOps;
    private const int SearchLimit = 1000;
    private static readonly string[] ClipboardLineSeparators = ["\r\n", "\n", "\r"];
    private static readonly Regex QtyAsteriskRegex = new(@"\*\s*(\d{1,5})", RegexOptions.Compiled);
    private static readonly Regex QtySuffixRegex = new(@"(\d{1,5})\s*(支|片|瓶|盒|袋|包|粒|枚|贴|丸)$", RegexOptions.Compiled);

    public override string DisplayName => "药品信息维护";
    public override string Icon => "Tablets";
    public override int Index => 2;
    public override ICommand RefreshCommand => _localRefreshCommand;
    public override ICommand ImportCommand => _importCommand;
    public override ICommand ExportCommand => _exportCommand;
    protected override bool AutoRefreshOnDbDisconnected => true;
    protected override bool AutoRefreshOnDbReconnected => true;

    private bool CanOperateUi() => !IsBusy;
    private bool CanIo() => CanOperateUi();

    private async Task ImportAsync()
    {
        if (SkipTrigger())
        {
            return;
        }

        if (!IsOpsUnlocked)
        {
            var unlocked = await _unlockService.RequireUnlockAsync(
                OpsScope,
                "药品信息维护",
                "身份验证",
                UnlockScopes.SharedOpsHint);
            RefreshOpsUnlock();
            if (!unlocked)
            {
                _toast.Warn("药品信息维护", "当前未解锁，无法填充剪贴板内容");
                return;
            }
        }

        IsBusy = true;
        try
        {
            var clip = (await _clipboard.GetTextAsync() ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(clip))
            {
                _toast.Warn("药品信息维护", "剪贴板为空，请先复制包含“物资名称/规格”的表格数据");
                return;
            }

            var parse = ParseClipboardRows(clip);
            if (parse.Rows.Count == 0)
            {
                _toast.Warn("药品信息维护", "未识别到可导入数据，请确认表头包含“物资名称(或药品名称)”和“规格”");
                return;
            }

            var first = parse.Rows[0];

            _suppressSelectionGuard = true;
            try
            {
                Selected?.NotePreview = null;

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

            EditDrugId = first.DrugId;
            EditSpec = first.Spec;
            EditQty = first.Qty;

            IsDirty = true;
            RefreshPageCommands();

            var msg = parse.Rows.Count > 1
                ? $"已填充第 1 条（共识别 {parse.Rows.Count} 条），请审计后手动保存"
                : "已填充到新建编辑区，请审计后手动保存";
            _toast.Success("药品信息维护", msg);
        }
        catch (Exception ex)
        {
            LogError("drug_index.import_clipboard.fail", "Failed importing drug rows from clipboard", ex);
            _toast.Error("药品信息维护", $"剪贴板导入失败：{ex.Message}");
        }
        finally
        {
            IsBusy = false;
            RefreshPageCommands();
        }
    }

    private async Task ExportAsync()
    {
        if (SkipTrigger())
        {
            return;
        }

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

        public string DrugId { get; }
        public string Spec { get; }
        public int Qty { get; private set; }
        public string? RuleKey { get; private set; }
        public string? PreTc { get; private set; }

        [ObservableProperty] private string? _note;
        [ObservableProperty] private string? _notePreview;

        public string EffectiveNote => NotePreview ?? Note ?? string.Empty;

        public DateTimeOffset CreatedAt { get; }
        public DateTimeOffset? UpdatedAt { get; }
        public long Version { get; private set; }

        [ObservableProperty] private bool _isDeprecated;
        [ObservableProperty] private bool _isNoSplit;

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
        }

        public bool ApplySaved(DrugIndexDto dto)
        {
            if (!string.Equals(DrugId, dto.DrugId, StringComparison.Ordinal)
                || !string.Equals(Spec, dto.Spec, StringComparison.Ordinal))
            {
                return false;
            }

            var changed = false;
            if (Qty != dto.Qty)
            {
                Qty = dto.Qty;
                OnPropertyChanged(nameof(Qty));
                changed = true;
            }

            if (!string.Equals(RuleKey, dto.RuleKey, StringComparison.Ordinal))
            {
                RuleKey = dto.RuleKey;
                OnPropertyChanged(nameof(RuleKey));
                changed = true;
            }

            if (!string.Equals(PreTc, dto.PreTc, StringComparison.Ordinal))
            {
                PreTc = dto.PreTc;
                OnPropertyChanged(nameof(PreTc));
                changed = true;
            }

            if (!string.Equals(Note, dto.Note, StringComparison.Ordinal))
            {
                Note = dto.Note;
                changed = true;
            }

            if (Version != dto.Version)
            {
                Version = dto.Version;
                changed = true;
            }

            if (changed)
            {
                RecalcFlags();
            }

            return true;
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

    private readonly IDrugIndexService _drugIndex;
    private readonly IToastService _toast;
    private readonly IDialogService _dialog;
    private readonly ISensitiveUnlockService _unlockService;
    private readonly IClipboardService _clipboard;
    private readonly InventoryOverview _inventoryOverview;
    private readonly ScanCode _scanCode;
    private readonly Dashboard _dashboard;
    private readonly ILookupCatalogService _lookup;
    private readonly AsyncRelayCommand _localRefreshCommand;
    private readonly AsyncRelayCommand _importCommand;
    private readonly AsyncRelayCommand _exportCommand;
    private readonly SearchInputDebouncer _keywordSearchDebouncer = new(450);
    private readonly DispatcherTimer _unlockStatusTimer;
    private IRelayCommand?[]? _notifiableCommands;
    private DrugIndexQuery _query = new(null);
    private int _reloadEpoch;
    private int _lastSuccessfulReloadEpoch;
    private bool _forceFullReload;
    private bool _clearListFocusAfterReload;

    public ObservableCollection<DrugRow> Items { get; } = new();
    protected override void OnPageAvailabilityChanged()
    {
        OnPropertyChanged(nameof(IsItemsEmpty));
        OnPropertyChanged(nameof(ItemsEmptyText));
        OnPropertyChanged(nameof(ItemsEmptyHint));
        OnPropertyChanged(nameof(IsListSectionPending));
    }

    public bool IsItemsEmpty => ShowSectionEmpty(Items.Count == 0);

    public string ItemsEmptyText => GetSectionEmptyTitle("暂无药品数据");
    public string ItemsEmptyHint => GetSectionEmptyHint("当前筛选条件下没有药品信息");

    public string? Keyword
    {
        get => _query.Keyword;
        set
        {
            _query = new DrugIndexQuery(Keyword: value);
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasActiveKeyword));
            ScheduleKeywordSearch(value);
        }
    }

    private void ScheduleKeywordSearch(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            _keywordSearchDebouncer.Cancel();
            ObserveDetached(ReloadAsync(confirmIfDirty: true, clearListFocus: true), "reload.detached.fail");
            return;
        }

        _keywordSearchDebouncer.Schedule(async () =>
        {
            if (!await TryConfirmDirtyBeforeActionAsync("继续搜索将丢失未保存内容"))
            {
                return;
            }

            await ReloadAsync(clearListFocus: true);
        });
    }
    [ObservableProperty] private DrugRow? _selected;
    [ObservableProperty] private bool _hasSelection;
    [ObservableProperty] private bool _hasEditor;
    [ObservableProperty] private bool _isOpsUnlocked;
    private DateTimeOffset _opsCooldownUntilUtc;
    [ObservableProperty] private bool _isListBusy;
    [ObservableProperty] private bool _isDrugGridMounted;

    public bool HasActiveKeyword => !string.IsNullOrWhiteSpace(NormalizeInput(_query.Keyword));

    public bool IsListSectionPending => IsSectionPending || IsListBusy || !IsDrugGridMounted;

    partial void OnIsListBusyChanged(bool value)
        => OnPropertyChanged(nameof(IsListSectionPending));

    partial void OnIsDrugGridMountedChanged(bool value)
        => OnPropertyChanged(nameof(IsListSectionPending));
    partial void OnHasEditorChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowEditState));
        OnPropertyChanged(nameof(EditStateText));
        OnPropertyChanged(nameof(ShowUnlock));
        OnPropertyChanged(nameof(ShowLock));
        OnPropertyChanged(nameof(EditColSpan));
        RefreshOpsUnlock();
    }

    partial void OnIsOpsUnlockedChanged(bool value)
    {
        RefreshOpsUnlock();
    }

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
    public string EditStateText => HasPendingChanges ? "编辑中未保存" : "已保存";
    public bool ShowEditState => HasEditor;
    public bool IsResultTruncated => _totalCount > Items.Count;

    public string ItemCountText =>
        IsResultTruncated
            ? $"已显示 {Items.Count}/共 {_totalCount} 条"
            : $"{Items.Count} 条";

    partial void OnIsDirtyChanged(bool value)
    {
        OnPropertyChanged(nameof(HasPendingChanges));
        OnPropertyChanged(nameof(EditStateText));
    }

    private DrugIndexDto? _loadedSnapshot;
    private int _totalCount;
    private bool _suppressSelectionGuard;
    private bool _preserveEditorOnSelectionRevert;
    private bool _suppressDirtyDuringSync;
    private DrugRow? _selectionBeforeChange;

    [ObservableProperty] private bool _isDeprecated;
    [ObservableProperty] private bool _isNoSplit;

    public string CreatedAtLocalText => FormatChinaTime(CreatedAt);
    public string UpdatedAtLocalText => UpdatedAt is null ? "" : FormatChinaTime(UpdatedAt.Value);
    public bool CanUnlock => !IsOpsUnlocked && CanOperateUi();
    public bool CanLock => IsOpsUnlocked && CanOperateUi();
    public bool CanEdit => HasEditor && IsOpsUnlocked;
    public bool CanEditActions => CanEdit && CanOperateUi();
    public bool ShowUnlock => !IsOpsUnlocked;
    public bool ShowLock => IsOpsUnlocked;
    public int EditColSpan => HasEditor ? 1 : 2;

    private static string FormatChinaTime(DateTimeOffset dt)
        => dt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.CurrentCulture);

    partial void OnCreatedAtChanged(DateTimeOffset value) => OnPropertyChanged(nameof(CreatedAtLocalText));
    partial void OnUpdatedAtChanged(DateTimeOffset? value) => OnPropertyChanged(nameof(UpdatedAtLocalText));



    public DrugIndex(
        IDrugIndexService drugIndex,
        IToastService toast,
        IDialogService dialog,
        ISensitiveUnlockService unlockService,
        IClipboardService clipboard,
        InventoryOverview inventoryOverview,
        ScanCode scanCode,
        Dashboard dashboard,
        ILookupCatalogService lookup)
    {
        _drugIndex = drugIndex;
        _toast = toast;
        _dialog = dialog;
        _unlockService = unlockService;
        _clipboard = clipboard;
        _inventoryOverview = inventoryOverview;
        _scanCode = scanCode;
        _dashboard = dashboard;
        _lookup = lookup;
        _localRefreshCommand = new AsyncRelayCommand(
            () => ReloadAsync(confirmIfDirty: true, clearListFocus: true),
            CanRefreshLocal);
        _importCommand = new AsyncRelayCommand(ImportAsync, CanIo);
        _exportCommand = new AsyncRelayCommand(ExportAsync, CanIo);
        _unlockStatusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _unlockStatusTimer.Tick += OnUnlockTimerTick;
        _unlockService.StateChanged += OnUnlockChanged;
        Items.CollectionChanged += OnItemsCollectionChanged;
        RefreshOpsUnlock();

        // Initial data load is posted to UI loop to avoid blocking page activation.
        PostOnUi(() => ObserveDetached(ReloadAsync(), "reload.detached.fail"), DispatcherPriority.Background);
    }

    private void OnItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(IsItemsEmpty));
        OnPropertyChanged(nameof(ItemsEmptyText));
        OnPropertyChanged(nameof(ItemsEmptyHint));
        OnPropertyChanged(nameof(ItemCountText));
        OnPropertyChanged(nameof(IsResultTruncated));
    }

    private bool CanRefreshLocal() => CanOperateUi();

    partial void OnSelectedChanging(DrugRow? value)
    {
        _selectionBeforeChange = Selected;
    }

    private DrugRow? FindRow(string drugId, string spec)
        => Items.FirstOrDefault(x => x.DrugId == drugId && x.Spec == spec);

    private void CommitPostWrite(DrugIndexDto saved)
    {
        _originDrugId = saved.DrugId;
        _originSpec = saved.Spec;
        _loadedSnapshot = saved;
        UpdatedAt = saved.UpdatedAt;
        IsDirty = false;
        ClearRemoteEditBaseline();
    }

    private void ApplySavedRowToGrid(DrugIndexDto saved)
    {
        if (FindRow(saved.DrugId, saved.Spec) is { } row && row.ApplySaved(saved))
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(_originDrugId) && !string.IsNullOrWhiteSpace(_originSpec))
        {
            for (var i = 0; i < Items.Count; i++)
            {
                var existing = Items[i];
                if (existing.DrugId == _originDrugId && existing.Spec == _originSpec)
                {
                    Items[i] = new DrugRow(saved);
                    return;
                }
            }
        }

        Items.Insert(0, new DrugRow(saved));
    }

    private async Task<bool> TryConfirmDirtyBeforeActionAsync(string actionHint)
    {
        if (!HasPendingChanges)
        {
            return true;
        }

        var choice = await _dialog.Alert(
            AlertBuilder<bool?>.Create("有未保存修改", $"当前修改尚未保存，{actionHint}")
                .DiscardOrSave("保存并继续", "放弃修改"));

        if (choice is null)
        {
            return false;
        }

        if (choice == true)
        {
            return await SaveRowAsync(reselectSavedRow: true);
        }

        DiscardDraft();
        return true;
    }

    partial void OnSelectedChanged(DrugRow? value)
    {
        if (_suppressSelectionGuard)
        {
            if (_preserveEditorOnSelectionRevert)
            {
                _preserveEditorOnSelectionRevert = false;
                RefreshPageCommands();
                return;
            }

            if (!HasPendingChanges)
            {
                ApplySelection(value);
            }

            return;
        }

        var next = value;
        var prev = _selectionBeforeChange;
        var nextDrugId = next?.DrugId;
        var nextSpec = next?.Spec;

        Dispatcher.UIThread.Post(() => ObserveDetached(
            OnSelectionChangedAsync(prev, next, nextDrugId, nextSpec),
            "selection.change.detached.fail"));
    }

    private async Task OnSelectionChangedAsync(DrugRow? prev, DrugRow? next, string? nextDrugId, string? nextSpec)
    {
        if (HasEditor && HasChanges())
        {
            var choice = await _dialog.Alert(
                AlertBuilder<bool?>.Create("有未保存修改", "当前修改尚未保存，切换会丢失修改")
                    .DiscardOrSave("保存并切换", "放弃修改"));

            if (choice is null)
            {
                RevertSelection(prev);
                return;
            }

            if (choice == true)
            {
                var ok = await SaveRowAsync(reselectSavedRow: false);
                if (!ok)
                {
                    RevertSelection(prev);
                    return;
                }

                if (!string.IsNullOrWhiteSpace(nextDrugId) && !string.IsNullOrWhiteSpace(nextSpec))
                {
                    next = FindRow(nextDrugId, nextSpec);
                }
            }
            else
            {
                DiscardDraft();
            }
        }

        prev?.NotePreview = null;

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
            {
                ClearEditor(keepEditorVisible: false);
            }

            return;
        }

        SyncEditorFrom(value);
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

        RefreshPageCommands();
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
        if (_suppressDirtyDuringSync)
        {
            return;
        }

        IsDirty = HasChanges();
        RefreshCommands(SaveCommand, DeleteCommand, FixDrugKeyCommand);
    }

    private static string Field(string? s) => (s ?? string.Empty).Trim();

    private bool IsEditingRow(DrugRow row)
        => string.Equals(_originDrugId, row.DrugId, StringComparison.Ordinal)
           && string.Equals(_originSpec, row.Spec, StringComparison.Ordinal);

    private void SyncEditorFrom(DrugRow row)
    {
        if (!HasPendingChanges && IsEditingRow(row))
        {
            row.NotePreview = null;
            HasSelection = true;
            HasEditor = true;

            _suppressDirtyDuringSync = true;
            try
            {
                UpdatedAt = row.UpdatedAt;
                _loadedSnapshot = row.ToDto();
                IsDirty = false;
            }
            finally
            {
                _suppressDirtyDuringSync = false;
            }

            RefreshPageCommands();
            return;
        }

        _suppressDirtyDuringSync = true;
        try
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
        }
        finally
        {
            _suppressDirtyDuringSync = false;
        }

        RefreshPageCommands();
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

        Selected?.NotePreview = null;

        HasSelection = false;
        HasEditor = keepEditorVisible;
        IsDeprecated = false;
        IsNoSplit = false;

        IsDirty = false;
        RefreshPageCommands();
    }

    private bool HasChanges()
    {
        if (_loadedSnapshot is null)
        {
            if (!string.IsNullOrWhiteSpace(EditDrugId))
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(EditSpec))
            {
                return true;
            }

            if (EditQty is > 0)
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(EditRuleKey))
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(EditPreTc))
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(EditNote))
            {
                return true;
            }

            return false;
        }

        return Field(_loadedSnapshot.DrugId) != Field(EditDrugId)
               || Field(_loadedSnapshot.Spec) != Field(EditSpec)
               || _loadedSnapshot.Qty != (EditQty ?? 0)
               || Field(_loadedSnapshot.RuleKey) != Field(EditRuleKey)
               || Field(_loadedSnapshot.PreTc) != Field(EditPreTc)
               || Field(_loadedSnapshot.Note) != Field(EditNote);
    }

    private void DiscardDraft()
    {
        if (_remoteEditBaseline is not null)
        {
            var dto = _remoteEditBaseline;
            _suppressDirtyDuringSync = true;
            try
            {
                EditDrugId = dto.DrugId;
                EditSpec = dto.Spec;
                EditQty = dto.Qty;
                EditRuleKey = dto.RuleKey;
                EditPreTc = dto.PreTc;
                EditNote = dto.Note;
                CreatedAt = dto.CreatedAt;
                UpdatedAt = dto.UpdatedAt;
                _loadedSnapshot = dto;
                _originDrugId = dto.DrugId;
                _originSpec = dto.Spec;
                RecalcEditorFlags(EditNote);
                Selected?.NotePreview = null;
                IsDirty = false;
            }
            finally
            {
                _suppressDirtyDuringSync = false;
            }

            if (FindRow(dto.DrugId, dto.Spec) is { } row)
            {
                row.ApplySaved(dto);
            }

            ClearRemoteEditBaseline();
            RefreshPageCommands();
            return;
        }

        if (_loadedSnapshot is null)
        {
            ClearEditor(keepEditorVisible: HasEditor);
            return;
        }

        var snapshot = _loadedSnapshot;
        EditDrugId = snapshot.DrugId;
        EditSpec = snapshot.Spec;
        EditQty = snapshot.Qty;
        EditRuleKey = snapshot.RuleKey;
        EditPreTc = snapshot.PreTc;
        EditNote = snapshot.Note;
        CreatedAt = snapshot.CreatedAt;
        UpdatedAt = snapshot.UpdatedAt;

        RecalcEditorFlags(EditNote);

        Selected?.NotePreview = EditNote;

        IsDirty = false;
        RefreshPageCommands();
    }

    private bool CanSave()
        => CanOperateUi()
           && HasEditor
           && IsOpsUnlocked
           && !string.IsNullOrWhiteSpace(EditDrugId)
           && !string.IsNullOrWhiteSpace(EditSpec)
           && EditQty is > 0
           && HasChanges();

    private bool CanDelete()
        => CanOperateUi()
           && IsOpsUnlocked
           && !string.IsNullOrWhiteSpace(_originDrugId)
           && !string.IsNullOrWhiteSpace(_originSpec);

    private bool CanFixDrugKey()
        => CanOperateUi()
           && HasEditor
           && IsOpsUnlocked
           && Selected is not null
           && !string.IsNullOrWhiteSpace(_originDrugId)
           && !string.IsNullOrWhiteSpace(_originSpec)
           && !string.IsNullOrWhiteSpace(NormalizeInput(EditDrugId))
           && !string.IsNullOrWhiteSpace(NormalizeInput(EditSpec))
           && EditQty is > 0
           && HasMigrationKeyChanges();

    private bool CanNewItem()
        => CanOperateUi();

    [RelayCommand(CanExecute = nameof(CanUnlock))]
    private async Task UnlockAsync()
    {
        await _unlockService.RequireUnlockAsync(
            OpsScope,
            "药品信息维护",
            "身份验证",
            UnlockScopes.SharedOpsHint);

        RefreshOpsUnlock();
    }

    [RelayCommand(CanExecute = nameof(CanLock))]
    private Task LockAsync()
    {
        _unlockService.Lock(OpsScope);
        RefreshOpsUnlock();
        _toast.Info("药品信息维护", "已锁定编辑");
        return Task.CompletedTask;
    }

    private void RefreshOpsUnlock()
    {
        _unlockService.Refresh(OpsScope);
        var snap = _unlockService.GetSnapshot(OpsScope);
        IsOpsUnlocked = snap.IsUnlocked;
        _opsCooldownUntilUtc = snap.CooldownUntilUtc;

        if (IsOpsUnlocked || _opsCooldownUntilUtc > DateTimeOffset.UtcNow)
        {
            StartUnlockTimer();
        }
        else
        {
            StopUnlockTimer();
        }

        RefreshPageCommands();
        OnPropertyChanged(nameof(CanUnlock));
        OnPropertyChanged(nameof(CanLock));
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(CanEditActions));
        OnPropertyChanged(nameof(ShowUnlock));
        OnPropertyChanged(nameof(ShowLock));
    }

    protected override void OnBusyChanged(bool isBusy)
    {
        OnPropertyChanged(nameof(CanEditActions));
        OnPropertyChanged(nameof(CanUnlock));
        OnPropertyChanged(nameof(CanLock));
    }

    private void StartUnlockTimer()
    {
        if (!_unlockStatusTimer.IsEnabled)
        {
            _unlockStatusTimer.Start();
        }
    }

    private void StopUnlockTimer()
    {
        if (_unlockStatusTimer.IsEnabled)
        {
            _unlockStatusTimer.Stop();
        }
    }

    private void OnUnlockChanged(string scopeKey)
    {
        if (!string.Equals(scopeKey, OpsScope, StringComparison.Ordinal))
        {
            return;
        }

        PostOnUi(RefreshOpsUnlock, DispatcherPriority.Background);
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        if (SkipTrigger(milliseconds: 800))
        {
            return;
        }

        await SaveRowAsync(reselectSavedRow: true);
    }

    private async Task<bool> SaveRowAsync(bool reselectSavedRow = true)
    {
        IsBusy = true;

        try
        {
            var inputDrugId = NormalizeInput(EditDrugId);
            var inputSpec = NormalizeInput(EditSpec);

            var drugId = inputDrugId ?? throw new InvalidOperationException("药品名不能为空");
            var spec = inputSpec ?? throw new InvalidOperationException("规格不能为空");

            var isNew = string.IsNullOrWhiteSpace(_originDrugId) && string.IsNullOrWhiteSpace(_originSpec);
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
                Version: _loadedSnapshot?.Version ?? 0);

            var saveResult = await _drugIndex.SaveAsync(
                new DrugIndexSaveRequest(
                    Dto: dto,
                    OriginDrugId: _originDrugId,
                    OriginSpec: _originSpec,
                    ExpectedVersion: isNew ? null : _loadedSnapshot?.Version,
                    IsNew: isNew,
                    HasPrimaryKeyChanges: !isNew && HasPrimaryKeyChanges(),
                    HasQtyChanged: !isNew && HasQtyChanged()),
                default);

            switch (saveResult.Outcome)
            {
                case DrugSaveOutcome.BlockedPrimaryKeyChange:
                    await _dialog.Warn("主键已变更", "药品名/规格变更请使用“纠错迁移”按钮执行");
                    return false;
                case DrugSaveOutcome.BlockedQtyChange:
                    Dispatcher.UIThread.Post(() =>
                        _toast.Warn("保存已拦截", "当前药品规格已被库存或事务引用，单盒数量变更请使用纠错迁移"));
                    return false;
                case DrugSaveOutcome.BlockedDuplicate:
                    await _dialog.Warn("名称/规格重复",
                        $"已存在相同记录：\n{DrugLabel.Format(drugId, spec)}\n\n请改成“编辑已有记录”或修改药品名/规格");
                    return false;
                case DrugSaveOutcome.ConcurrencyConflict:
                    LogWarn("drug_index.save.concurrency_conflict", "Detected optimistic concurrency conflict", saveResult.Concurrency);
                    return await HandleSaveConflictAsync(saveResult.Saved, drugId, spec, reselectSavedRow);
            }

            var saved = saveResult.Saved ?? throw new InvalidOperationException("保存成功但未返回记录");

            Dispatcher.UIThread.Post(() => _toast.Success("已保存", DrugLabel.Format(drugId, spec)));

            CommitPostWrite(saved);
            RefreshPageCommands();

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                ApplySavedRowToGrid(saved);

                if (reselectSavedRow)
                {
                    FocusSavedRow(drugId, spec);
                }
            }, DispatcherPriority.Normal);

            NotifyDrugCatalogChanged();

            return true;
        }
        catch (Exception ex)
        {
            LogError("drug_index.save.fail", "Failed to save drug row", ex);
            var target = DrugLabel.Format(
                NormalizeInput(EditDrugId) ?? (EditDrugId ?? string.Empty).Trim(),
                NormalizeInput(EditSpec) ?? (EditSpec ?? string.Empty).Trim());
            Dispatcher.UIThread.Post(() => _toast.Error("保存失败", $"{target}：{ex.Message}"));
            return false;
        }
        finally
        {
            IsBusy = false;
            RefreshPageCommands();
        }
    }

    private async Task<bool> HandleSaveConflictAsync(
        DrugIndexDto? serverRow,
        string drugId,
        string spec,
        bool reselectSavedRow)
    {
        var choice = await _dialog.Alert(
            AlertBuilder<bool?>.Create("保存冲突", $"该记录已被其他终端修改\n{DrugLabel.Format(drugId, spec)}")
                .SaveConflict("放弃修改", "强制保存"));

        if (choice is null)
        {
            return false;
        }

        if (choice == false)
        {
            await ApplyConflictServerBaselineAndReloadAsync(serverRow);
            return false;
        }

        if (serverRow is null || _loadedSnapshot is null)
        {
            return false;
        }

        _loadedSnapshot = _loadedSnapshot with { Version = serverRow.Version };
        ClearRemoteEditBaseline();
        return await SaveRowAsync(reselectSavedRow);
    }

    [RelayCommand(CanExecute = nameof(CanFixDrugKey))]
    private async Task FixDrugKeyAsync()
    {
        if (SkipTrigger("drug.fix_key", 800))
        {
            return;
        }

        if (EditQty is not > 0)
        {
            return;
        }

        if (Selected is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(Selected.DrugId) || string.IsNullOrWhiteSpace(Selected.Spec))
        {
            return;
        }

        var targetDrugId = NormalizeInput(EditDrugId) ?? string.Empty;
        var targetSpec = NormalizeInput(EditSpec) ?? string.Empty;

        if (targetDrugId.Length == 0 || targetSpec.Length == 0)
        {
            return;
        }

        var sourceDrugId = Selected.DrugId;
        var sourceSpec = Selected.Spec;
        var sameKey = string.Equals(sourceDrugId, targetDrugId, StringComparison.Ordinal)
                      && string.Equals(sourceSpec, targetSpec, StringComparison.Ordinal);

        IsBusy = true;
        try
        {
            var source = await _drugIndex.GetByKeyAsync(sourceDrugId, sourceSpec, default);
            if (source is null)
            {
                await _dialog.Warn("纠错迁移", "源药品规格不存在或已被移除，请刷新后重试");
                await ReloadAsync(forceFull: true);
                return;
            }

            var preview = await _drugIndex.PreviewKeyFixAsync(
                source.DrugId,
                source.Spec,
                targetDrugId,
                targetSpec,
                default);

            if (!preview.SourceExists)
            {
                await _dialog.Warn("纠错迁移", "源药品规格不存在或已被移除，请刷新后重试");
                return;
            }

            var confirm = await _dialog.ConfirmDrugKeyFixPreview(
                source.DrugId,
                source.Spec,
                source.Qty,
                targetDrugId,
                targetSpec,
                EditQty.Value,
                preview.TargetExists,
                preview.TracePoolAffected,
                preview.TraceTxnAffected);
            if (!confirm)
            {
                return;
            }

            var target = new DrugIndexDto(
                DrugId: targetDrugId,
                Spec: targetSpec,
                Qty: EditQty.Value,
                RuleKey: NormalizeInput(EditRuleKey),
                PreTc: NormalizeInput(EditPreTc),
                Note: NormalizeInput(EditNote),
                CreatedAt: source.CreatedAt,
                UpdatedAt: DateTimeOffset.UtcNow,
                Version: 0);

            var reason = $"drug-key-fix: {source.DrugId}/{source.Spec} -> {targetDrugId}/{targetSpec}";
            var commit = await _drugIndex.ApplyKeyFixAsync(
                new DrugKeyFixRequest(
                    Source: source,
                    Target: target,
                    Reason: reason,
                    OperatorName: Environment.UserName,
                    SourceTag: "drug_index_ui"),
                default);

            var result = commit.Apply;
            var dbSourceAfter = commit.SourceAfter;
            var dbTargetAfter = commit.TargetAfter;
            if (!sameKey)
            {
                if (dbSourceAfter is not null || dbTargetAfter is null)
                {
                    throw new InvalidOperationException(
                        $"迁移提交校验失败(DB)：sourceExists={(dbSourceAfter is not null ? 1 : 0)}, targetExists={(dbTargetAfter is not null ? 1 : 0)}");
                }
            }
            else
            {
                if (dbTargetAfter is null)
                {
                    throw new InvalidOperationException("迁移提交校验失败(DB)：目标键未找到");
                }

                if (dbTargetAfter.Qty != EditQty.Value)
                {
                    throw new InvalidOperationException(
                        $"迁移提交校验失败(DB)：qty 未生效，期望 {EditQty.Value}，实际 {dbTargetAfter.Qty}");
                }
            }

            var focusDrugId = dbTargetAfter!.DrugId;
            var focusSpec = dbTargetAfter.Spec;

            CommitPostWrite(dbTargetAfter);
            QueueReselect(focusDrugId, focusSpec);
            await ReloadAsync();

            Dispatcher.UIThread.Post(() =>
                _toast.Success(
                    "药品纠错迁移",
                    $"源药品名/规格：{DrugLabel.WithQty(source.DrugId, source.Spec, source.Qty)}\n" +
                    $"目标药品名/规格：{DrugLabel.WithQty(focusDrugId, focusSpec, dbTargetAfter.Qty)}\n" +
                    $"追溯码池影响：{result.TracePoolAffected} 条\n" +
                    $"执行事务影响：{result.TraceTxnAffected} 条"));

            NotifyDrugCatalogChanged();
        }
        catch (DrugIndexConcurrencyException cx)
        {
            LogWarn("drug_index.fix_key.concurrency_conflict", "Detected key-fix concurrency conflict", cx);
            await _dialog.Warn("迁移冲突", "该记录已被其他终端修改，请先刷新后再试");
            await ReloadAsync(forceFull: true);
        }
        catch (Exception ex)
        {
            LogError("drug_index.fix_key.fail", "Failed to fix drug key", ex);
            Dispatcher.UIThread.Post(() => _toast.Error("纠错迁移失败", ex.Message));
        }
        finally
        {
            IsBusy = false;
            RefreshPageCommands();
        }
    }

    private async Task ReloadAsync(bool forceFull = false, bool confirmIfDirty = false, bool clearListFocus = false)
    {
        if (confirmIfDirty && !forceFull && HasPendingChanges)
        {
            if (!await TryConfirmDirtyBeforeActionAsync("继续刷新将丢失未保存内容"))
            {
                return;
            }
        }

        _forceFullReload = forceFull;
        _clearListFocusAfterReload = clearListFocus && !forceFull;
        await RunLocalReloadAsync(
            setBusy: v => IsListBusy = v,
            action: ReloadCoreAsync,
            onFinished: OnReloadFinished);
    }

    protected override async Task ReloadCoreAsync(CancellationToken ct)
    {
        // Epoch marks this reload attempt and helps suppress stale error toasts.
        var epoch = Interlocked.Increment(ref _reloadEpoch);

        try
        {
            // Reason: Capture query state before async work to avoid stale reads.
            var query = _query;
            var result = await _drugIndex.SearchAsync(query.Keyword, limit: SearchLimit, ct);
            var newRows = result.Items.Select(dto => new DrugRow(dto)).ToList();

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (epoch != Volatile.Read(ref _reloadEpoch))
                {
                    return;
                }

                _totalCount = result.TotalCount;
                _suppressSelectionGuard = true;
                try
                {
                    if (ShouldSilentReconcile())
                    {
                        ApplySilentReconcile(newRows);
                    }
                    else if (_forceFullReload)
                    {
                        ApplyFullReload(newRows);
                    }
                    else
                    {
                        ApplyCleanRefresh(newRows);
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
            throw;
        }
        catch (Exception ex)
        {
            LogError("drug_index.reload.fail", "Failed to reload drug index", ex);
            if (!CanToastError(ex))
            {
                throw;
            }

            await Task.Delay(180, ct).ConfigureAwait(false);

            if (ct.IsCancellationRequested)
            {
                throw;
            }

            if (Volatile.Read(ref _lastSuccessfulReloadEpoch) > epoch)
            {
                return;
            }

            Dispatcher.UIThread.Post(() =>
                _toast.Error("药品信息加载失败", ex.Message));
            throw;
        }
    }

    protected override void OnReloadFinished()
    {
        _forceFullReload = false;
        _clearListFocusAfterReload = false;
        RefreshPageCommands();
    }

    [RelayCommand]
    private Task SearchAsync()
    {
        if (SkipTrigger(milliseconds: 300))
        {
            return Task.CompletedTask;
        }

        _keywordSearchDebouncer.Cancel();
        return ReloadAsync(confirmIfDirty: true, clearListFocus: true);
    }

    [RelayCommand]
    private Task ClearSearchAsync()
    {
        if (SkipTrigger(milliseconds: 300))
        {
            return Task.CompletedTask;
        }

        _keywordSearchDebouncer.Cancel();
        Keyword = null;
        return Task.CompletedTask;
    }

    [RelayCommand(CanExecute = nameof(CanNewItem))]
    private void NewItem()
    {
        if (SkipTrigger(milliseconds: 250))
        {
            return;
        }

        ClearListFocus(clearOrigin: true, keepEditorVisible: true);
        RefreshCommands(SaveCommand);
    }

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private async Task DeleteAsync()
    {
        if (SkipTrigger(milliseconds: 800))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(_originDrugId) || string.IsNullOrWhiteSpace(_originSpec))
        {
            return;
        }

        var deleteDrugId = _originDrugId!;
        var deleteSpec = _originSpec!;

        var ok = await _dialog.ConfirmDestructive("删除药品规格",
            $"确认删除？\n{DrugLabel.Format(deleteDrugId, deleteSpec)}\n\n注意：追溯码池和事务中有记录会阻止删除正在引用的记录");

        if (!ok)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await _drugIndex.DeleteAsync(deleteDrugId, deleteSpec, default);
            Dispatcher.UIThread.Post(() => _toast.Success("已删除", DrugLabel.Format(deleteDrugId, deleteSpec)));
            await ReloadAsync(clearListFocus: true);
            NotifyDrugCatalogChanged();
        }
        catch (Exception ex)
        {
            LogError("drug_index.delete.fail", "Failed to delete drug row", ex, new { deleteDrugId, deleteSpec });
            Dispatcher.UIThread.Post(() => _toast.Error("删除失败", ex.Message));
        }
        finally
        {
            IsBusy = false;
            RefreshPageCommands();
        }
    }

    [RelayCommand(CanExecute = nameof(CanToggleFlags))]
    private void ToggleDeprecated(object? arg)
    {
        if (arg is DrugRow row)
        {
            Selected = row;
        }

        ApplyToggleDeprecated();
    }

    [RelayCommand(CanExecute = nameof(CanToggleFlags))]
    private void ToggleNoSplit(object? arg)
    {
        if (arg is DrugRow row)
        {
            Selected = row;
        }

        ApplyToggleNoSplit();
    }

    private bool CanToggleFlags()
        => CanEditActions;

    private void ApplyToggleDeprecated()
    {
        if (!CanEdit)
        {
            return;
        }

        EditNote = ToggleToken(EditNote, "弃用");
    }

    private void ApplyToggleNoSplit()
    {
        if (!CanEdit)
        {
            return;
        }

        EditNote = ToggleToken(EditNote, "未拆零");
    }

    private static string? ToggleToken(string? note, string token)
    {
        var s = (note ?? string.Empty).Trim();
        if (s.Length == 0)
        {
            return token;
        }

        if (s.Contains(token, StringComparison.Ordinal))
        {
            s = s.Replace(token, string.Empty, StringComparison.Ordinal);
            s = string.Join(' ', s.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));
            return s.Length == 0 ? null : s;
        }

        return s + " " + token;
    }

    private void NotifyDrugCatalogChanged()
    {
        _lookup.InvalidateDrugCatalog();
        _inventoryOverview.ReloadAfterDrugIndexChange();
        _scanCode.ReloadAfterDrugIndexChange();
        _dashboard.ReloadAfterDrugIndexChange();
    }

    private void RefreshPageCommands()
    {
        RefreshCommandsCoalesced("drug_index.refresh_commands", () =>
            RefreshCommands(GetNotifiableCommands()));
    }

    private IRelayCommand?[] GetNotifiableCommands()
        => _notifiableCommands ??=
        [
            _localRefreshCommand,
            NewItemCommand,
            SaveCommand,
            DeleteCommand,
            FixDrugKeyCommand,
            UnlockCommand,
            LockCommand,
            ToggleDeprecatedCommand,
            ToggleNoSplitCommand,
            _importCommand,
            _exportCommand
        ];

    private bool HasPrimaryKeyChanges()
    {
        if (_loadedSnapshot is not null)
        {
            return !string.Equals(NormalizeInput(_loadedSnapshot.DrugId), NormalizeInput(EditDrugId), StringComparison.Ordinal)
                   || !string.Equals(NormalizeInput(_loadedSnapshot.Spec), NormalizeInput(EditSpec), StringComparison.Ordinal);
        }

        if (!string.IsNullOrWhiteSpace(_originDrugId) && !string.IsNullOrWhiteSpace(_originSpec))
        {
            return !string.Equals(NormalizeInput(_originDrugId), NormalizeInput(EditDrugId), StringComparison.Ordinal)
                   || !string.Equals(NormalizeInput(_originSpec), NormalizeInput(EditSpec), StringComparison.Ordinal);
        }

        return false;
    }

    private bool HasQtyChanged()
    {
        if (EditQty is not > 0 || _loadedSnapshot is null)
        {
            return false;
        }

        return _loadedSnapshot.Qty != EditQty.Value;
    }

    private bool HasMigrationKeyChanges()
        => HasPrimaryKeyChanges() || HasQtyChanged();

    private sealed record ClipboardDrugRow(string DrugId, string Spec, int Qty);

    private sealed record ClipboardParseResult(IReadOnlyList<ClipboardDrugRow> Rows);

    private static ClipboardParseResult ParseClipboardRows(string text)
    {
        var lines = text
            .Split(ClipboardLineSeparators, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim())
            .Where(x => x.Length > 0)
            .ToList();

        if (lines.Count < 2)
        {
            return new ClipboardParseResult(Array.Empty<ClipboardDrugRow>());
        }

        var header = lines[0].Split('\t').Select(NormalizeHeader).ToArray();
        var nameIdx = FindFirstHeaderIndex(header, "物资名称", "药品名称", "品名");
        var specIdx = FindFirstHeaderIndex(header, "规格", "包装规格", "制剂规格");

        if (nameIdx < 0 || specIdx < 0)
        {
            return new ClipboardParseResult(Array.Empty<ClipboardDrugRow>());
        }

        var rows = new List<ClipboardDrugRow>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var raw in lines.Skip(1))
        {
            var cols = raw.Split('\t');
            if (cols.Length <= Math.Max(nameIdx, specIdx))
            {
                continue;
            }

            var drugId = cols[nameIdx].Trim();
            var spec = cols[specIdx].Trim();
            if (drugId.Length == 0 || spec.Length == 0)
            {
                continue;
            }

            var key = $"{drugId}||{spec}";
            if (!seen.Add(key))
            {
                continue;
            }

            rows.Add(new ClipboardDrugRow(drugId, spec, InferQtyFromSpec(spec)));
        }

        return new ClipboardParseResult(rows);
    }

    private static string NormalizeHeader(string value)
        => value.Replace(" ", string.Empty)
                .Replace("　", string.Empty)
                .Trim();

    private static int FindFirstHeaderIndex(IReadOnlyList<string> headers, params string[] names)
    {
        for (var i = 0; i < headers.Count; i++)
        {
            for (var j = 0; j < names.Length; j++)
            {
                if (string.Equals(headers[i], names[j], StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }
        }
        return -1;
    }

    private static int InferQtyFromSpec(string spec)
    {
        if (string.IsNullOrWhiteSpace(spec))
        {
            return 1;
        }

        var s = spec.Trim();

        var m1 = QtyAsteriskRegex.Match(s);
        if (m1.Success && int.TryParse(m1.Groups[1].Value, out var q1) && q1 > 0)
        {
            return q1;
        }

        var m2 = QtySuffixRegex.Match(s);
        if (m2.Success && int.TryParse(m2.Groups[1].Value, out var q2) && q2 > 0)
        {
            return q2;
        }

        return 1;
    }

    private void OnUnlockTimerTick(object? sender, EventArgs e)
        => RefreshOpsUnlock();

    public override void Dispose()
    {
        Items.CollectionChanged -= OnItemsCollectionChanged;
        _unlockService.StateChanged -= OnUnlockChanged;
        StopUnlockTimer();
        _unlockStatusTimer.Tick -= OnUnlockTimerTick;
        _keywordSearchDebouncer.Dispose();
        base.Dispose();
    }

}
