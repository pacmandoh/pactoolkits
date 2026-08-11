using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using PacToolkits.Application.DTOs;
using PacToolkits.Desktop.Avalonia.Services.Workspace.Refresh;
using PacToolkits.Desktop.Avalonia.Ui.Collections;

namespace PacToolkits.Desktop.Avalonia.ViewModels.Pages;

public sealed partial class DrugIndex
{
    private enum WorkingSetReload
    {
        Keep,
        Clear,
    }

    private DrugIndexDto? _remoteEditBaseline;

    public bool DeferRefreshTopic(string? topic)
        => WorkspaceTopicRefresh.DeferDrugIndex(topic);

    public Task ReloadFromWatermarkAsync()
        => ReloadAsync(confirmIfDirty: false);

    private readonly record struct DrugKey(string DrugId, string Spec);

    private DrugKey? _pendingReselectKey;

    private static DrugKey KeyOf(DrugRow row) => new(row.DrugId, row.Spec);

    private static DrugKey KeyOf(string drugId, string spec) => new(drugId, spec);

    private void QueueReselect(string drugId, string spec)
        => _pendingReselectKey = KeyOf(drugId, spec);

    private bool ShouldSilentReconcile()
        => !_forceFullReload && !_pendingReselectKey.HasValue && HasPendingChanges;

    private static bool RowContentMatches(DrugRow existing, DrugRow server)
        => existing.Version == server.Version;

    private DrugRow MergeServerRow(DrugRow server, DrugKey? keepDraftKey)
    {
        if (keepDraftKey == KeyOf(server) && HasPendingChanges)
        {
            var draft = FindRow(server.DrugId, server.Spec);
            return draft ?? server;
        }

        var existing = FindRow(server.DrugId, server.Spec);
        if (existing is null)
        {
            return server;
        }

        if (RowContentMatches(existing, server))
        {
            return existing;
        }

        existing.ApplySaved(server.ToDto());
        return existing;
    }

    private List<DrugRow> MergeServerRows(IReadOnlyList<DrugRow> serverRows, DrugKey? keepDraftKey)
    {
        var merged = new List<DrugRow>(serverRows.Count);
        foreach (var server in serverRows)
        {
            merged.Add(MergeServerRow(server, keepDraftKey));
        }

        return merged;
    }

    private void SynchronizeItemsOrder(IReadOnlyList<DrugRow> desired)
    {
        if (Items.Count != desired.Count)
        {
            // ReplaceAll：顺序/内容变化时避免 ObservableCollection Reset 打掉 DataGrid 选中
            Items.ReplaceAll(desired);
            return;
        }

        for (var i = 0; i < desired.Count; i++)
        {
            if (KeyOf(Items[i]) != KeyOf(desired[i]))
            {
                Items.ReplaceAll(desired);
                return;
            }
        }
    }

    private void ApplyCleanRefresh(IReadOnlyList<DrugRow> serverRows)
    {
        var merged = MergeServerRows(serverRows, keepDraftKey: null);
        SynchronizeItemsOrder(merged);
        FinalizeItemsReload();
    }

    private DrugKey? ResolveSelectedKey()
    {
        if (!string.IsNullOrWhiteSpace(_originDrugId) && !string.IsNullOrWhiteSpace(_originSpec))
        {
            return KeyOf(_originDrugId, _originSpec);
        }

        if (Selected is not null)
        {
            return KeyOf(Selected);
        }

        return null;
    }

    private void ApplyFullReload(IReadOnlyList<DrugRow> newRows)
    {
        ClearWorkingSet(clearOrigin: true);
        Items.ReplaceAll(newRows);
        FinalizeItemsReload();
    }

    private void ApplySilentReconcile(IReadOnlyList<DrugRow> serverRows)
    {
        // 编辑期间收到远端变更时合并服务端数据，同时保留本地草稿
        var serverByKey = serverRows.ToDictionary(KeyOf);
        var selectedKey = ResolveSelectedKey();

        if (selectedKey is { } sk && HasPendingChanges && _loadedSnapshot is not null)
        {
            if (serverByKey.TryGetValue(sk, out var remote) && remote.Version != _loadedSnapshot.Version)
            {
                _remoteEditBaseline = remote.ToDto();
                _toast.Warn("药品信息", "该行已在其它终端修改");
            }
            else if (!serverByKey.ContainsKey(sk))
            {
                _remoteEditBaseline = null;
                _toast.Warn("药品信息", "该行已在其它终端删除");
            }
        }

        for (var i = Items.Count - 1; i >= 0; i--)
        {
            var row = Items[i];
            var key = KeyOf(row);
            if (serverByKey.ContainsKey(key))
            {
                continue;
            }

            if (selectedKey == key && HasPendingChanges)
            {
                continue;
            }

            Items.RemoveAt(i);
        }

        var merged = MergeServerRows(serverRows, selectedKey);
        SynchronizeItemsOrder(merged);
        FinalizeItemsReload();
    }

    private void FinalizeItemsReload()
    {
        if (_pendingReselectKey is { } pending)
        {
            ReselectRow(pending.DrugId, pending.Spec);
        }
        else if (_workingSetAfterReload == WorkingSetReload.Clear && !HasPendingChanges)
        {
            ClearWorkingSet(clearOrigin: false);
        }
        else if (Selected is not null && !HasPendingChanges)
        {
            var refreshed = FindRow(Selected.DrugId, Selected.Spec);
            if (refreshed is not null && !ReferenceEquals(refreshed, Selected))
            {
                _suppressSelectionGuard = true;
                try
                {
                    Selected = refreshed;
                }
                finally
                {
                    _suppressSelectionGuard = false;
                }
            }
            else if (refreshed is not null)
            {
                SyncEditorFrom(refreshed);
            }
        }

        OnPropertyChanged(nameof(ItemCountText));
        OnPropertyChanged(nameof(IsResultTruncated));
    }

    private void ClearWorkingSet(bool clearOrigin, bool keepEditorVisible = false)
    {
        Selected?.NotePreview = null;

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

        if (clearOrigin)
        {
            _originDrugId = null;
            _originSpec = null;
            _loadedSnapshot = null;
        }

        _remoteEditBaseline = null;
        ClearEditor(keepEditorVisible: keepEditorVisible);
    }

    public event Action? RevealSelected;

    private void ReselectRow(string drugId, string spec)
    {
        _pendingReselectKey = null;

        var row = FindRow(drugId, spec);
        if (row is null)
        {
            // 当前结果集未包含目标行时仍保留编辑器与选中态
            return;
        }

        _selectionBeforeChange = row;

        if (!ReferenceEquals(Selected, row))
        {
            _suppressSelectionGuard = true;
            try
            {
                Selected = row;
            }
            finally
            {
                _suppressSelectionGuard = false;
            }
        }
        else if (!HasPendingChanges)
        {
            SyncEditorFrom(row);
        }

        RevealSelected?.Invoke();
    }

    private void ClearRemoteEditBaseline()
        => _remoteEditBaseline = null;

    private void ApplyConflictServerBaseline(DrugIndexDto? serverRow)
    {
        if (serverRow is null)
        {
            return;
        }

        _remoteEditBaseline = serverRow;
        DiscardDraft();
    }

    private async Task ApplyConflictServerBaselineAndReloadAsync(DrugIndexDto? serverRow)
    {
        ApplyConflictServerBaseline(serverRow);
        await ReloadAsync(forceFull: false, workingSet: WorkingSetReload.Clear);
    }
}
