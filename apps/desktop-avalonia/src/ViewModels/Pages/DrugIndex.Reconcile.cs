using System.Collections.Generic;
using System.Linq;
using PacToolkits.Desktop.Avalonia.Common;
using PacToolkits.Desktop.Avalonia.Services.Application;

namespace PacToolkits.Desktop.Avalonia.ViewModels.Pages;

public sealed partial class DrugIndex
{
    public bool DeferRefreshTopic(string? topic)
        => WatermarkActiveRefreshDeferPolicy.ShouldDeferDrugIndexActiveRefresh(topic);

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
        return existing is not null && RowContentMatches(existing, server) ? existing : server;
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

    private void DetachListSelection()
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
    }

    private void ApplyCleanRefresh(IReadOnlyList<DrugRow> serverRows)
    {
        DetachListSelection();
        Items.ReplaceAll(MergeServerRows(serverRows, keepDraftKey: null));
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
        ClearListFocus(clearOrigin: true);
        Items.ReplaceAll(newRows);
        FinalizeItemsReload();
    }

    private void ApplySilentReconcile(IReadOnlyList<DrugRow> serverRows)
    {
        var serverByKey = serverRows.ToDictionary(KeyOf);
        var selectedKey = ResolveSelectedKey();

        if (selectedKey is { } sk && HasPendingChanges && _loadedSnapshot is not null)
        {
            if (serverByKey.TryGetValue(sk, out var remote) && remote.Version != _loadedSnapshot.Version)
            {
                _toast.Warn("药品信息", "该行已在其它终端修改");
            }
            else if (!serverByKey.ContainsKey(sk))
            {
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

        _suppressSelectionGuard = true;
        try
        {
            Items.ReplaceAll(MergeServerRows(serverRows, selectedKey));
        }
        finally
        {
            _suppressSelectionGuard = false;
        }

        FinalizeItemsReload();
    }

    private void FinalizeItemsReload()
    {
        if (_pendingReselectKey is { } pending)
        {
            FocusSavedRow(pending.DrugId, pending.Spec);
        }
        else if (!HasPendingChanges)
        {
            ClearListFocus(clearOrigin: false);
        }

        OnPropertyChanged(nameof(ItemCountText));
        OnPropertyChanged(nameof(IsResultTruncated));
    }

    private void ClearListFocus(bool clearOrigin, bool keepEditorVisible = false)
    {
        DetachListSelection();

        if (clearOrigin)
        {
            _originDrugId = null;
            _originSpec = null;
            _loadedSnapshot = null;
        }

        ClearEditor(keepEditorVisible: keepEditorVisible);
    }

    private void FocusSavedRow(string drugId, string spec)
    {
        _pendingReselectKey = null;

        var row = FindRow(drugId, spec);
        if (row is null)
        {
            ClearListFocus(clearOrigin: false);
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
        else
        {
            ApplySelection(row);
        }
    }
}
