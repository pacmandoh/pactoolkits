using System;

namespace PacToolkits.Desktop.Avalonia.Services.Workspace.Inventory;

/// <summary>
/// 库存明细编辑中的远端感知：静默拉页时决定如何合并，禁止冲掉单元格草稿
/// </summary>
public static class InventoryStockEditRemotePolicy
{
    public enum Action
    {
        None,
        SyncAll,
        KeepDraftBaseline,
        MissingSilent,
        MissingNotify,
    }

    public static Action Decide(
        long snapVersion,
        string snapTraceCode,
        int snapRemain,
        string currentTraceCode,
        int currentRemain,
        long? serverVersion)
    {
        var localDirty = !string.Equals(snapTraceCode, currentTraceCode, StringComparison.Ordinal)
                         || snapRemain != currentRemain;

        if (serverVersion is null)
        {
            return localDirty ? Action.MissingNotify : Action.MissingSilent;
        }

        var versionDrift = snapVersion != serverVersion.Value;
        if (versionDrift && localDirty)
        {
            return Action.KeepDraftBaseline;
        }

        if (versionDrift)
        {
            return Action.SyncAll;
        }

        return Action.None;
    }
}
