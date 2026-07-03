using System.Collections.Generic;
using System.Linq;
using PacToolkits.Desktop.Avalonia.Common;

namespace PacToolkits.Desktop.Avalonia.ViewModels.Pages;

public sealed partial class DrugIndex
{
    public bool DeferRefreshTopic(string? topic)
    {
        var key = (topic ?? string.Empty).Trim().ToLowerInvariant();
        return key is "trace_pool" or "trace_txn" or "trace_txn_item";
    }

    private readonly record struct DrugKey(string DrugId, string Spec);

    private static DrugKey KeyOf(DrugRow row) => new(row.DrugId, row.Spec);

    private bool ShouldSilentReconcile()
        => !_forceFullReload && (HasEditor || Selected is not null);

    private DrugKey? ResolveSelectedKey()
    {
        if (!string.IsNullOrWhiteSpace(_originDrugId) && !string.IsNullOrWhiteSpace(_originSpec))
        {
            return new DrugKey(_originDrugId, _originSpec);
        }

        if (Selected is not null)
        {
            return KeyOf(Selected);
        }

        return null;
    }

    private void ApplyFullReload(IReadOnlyList<DrugRow> newRows)
    {
        Selected?.NotePreview = null;

        Selected = null;
        _selectionBeforeChange = null;

        _originDrugId = null;
        _originSpec = null;
        _loadedSnapshot = null;

        ClearEditor(keepEditorVisible: false);
        Items.ReplaceAll(newRows);
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

        var nextItems = new List<DrugRow>(serverRows.Count);
        DrugRow? nextSelected = null;

        foreach (var remote in serverRows)
        {
            var key = KeyOf(remote);
            if (selectedKey == key)
            {
                if (HasPendingChanges)
                {
                    var existing = FindRow(remote.DrugId, remote.Spec);
                    var kept = existing ?? remote;
                    nextItems.Add(kept);
                    nextSelected = kept;
                    continue;
                }

                nextItems.Add(remote);
                nextSelected = remote;
                continue;
            }

            nextItems.Add(remote);
        }

        _suppressSelectionGuard = true;
        try
        {
            Items.ReplaceAll(nextItems);

            if (selectedKey is not null)
            {
                _selectionBeforeChange = null;
                Selected = nextSelected ?? FindRow(selectedKey.Value.DrugId, selectedKey.Value.Spec);

                if (!HasPendingChanges && Selected is not null)
                {
                    SyncEditorFrom(Selected);
                }
            }
        }
        finally
        {
            _suppressSelectionGuard = false;
        }

        OnPropertyChanged(nameof(ItemCountText));
    }
}
