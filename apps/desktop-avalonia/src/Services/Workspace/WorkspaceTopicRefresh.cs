using System;
using System.Collections.Generic;
using PacToolkits.Desktop.Avalonia.Contracts;
using PacToolkits.Desktop.Avalonia.ViewModels;
using PacToolkits.Desktop.Avalonia.ViewModels.Pages;

namespace PacToolkits.Desktop.Avalonia.Services.Workspace;

/// <summary>Maps change-watermark topics to which workspace pages should mark dirty.</summary>
public static class WorkspaceTopicRefresh
{
    public readonly record struct DirtyPlan(
        bool InvalidateDrugCatalog,
        bool MarkInventory,
        bool MarkDrugIndex,
        bool MarkDashboard,
        bool MarkScanCode,
        bool MarkMsfx,
        bool MarkAllRefreshable);

    public static (bool SkipInventory, bool SkipDrugIndex) SkipActiveRefresh(AppPageBase? active, string? topic)
        => (
            active is IInventoryRefreshPage inventory && inventory.DeferRefreshTopic(topic),
            active is IDrugIndexRefreshPage drugIndex && drugIndex.DeferRefreshTopic(topic));

    public static bool DeferDrugIndex(string? topic)
    {
        var key = Normalize(topic);
        return key is "trace_pool" or "trace_txn" or "trace_txn_item";
    }

    public static bool DeferInventory(string? topic)
    {
        var key = Normalize(topic);
        return key is "inventory" or "trace_pool" or "trace_txn" or "trace_txn_item" or "";
    }

    public static DirtyPlan PlanDirtyMarks(string? topic, bool skipInventoryPage, bool skipDrugIndexPage)
    {
        var key = Normalize(topic);

        return key switch
        {
            "drug_index" => new DirtyPlan(
                InvalidateDrugCatalog: true,
                MarkInventory: false,
                MarkDrugIndex: !skipDrugIndexPage,
                MarkDashboard: true,
                MarkScanCode: true,
                MarkMsfx: false,
                MarkAllRefreshable: false),
            "inventory" or "trace_pool" or "trace_txn" or "trace_txn_item" => new DirtyPlan(
                InvalidateDrugCatalog: false,
                MarkInventory: !skipInventoryPage,
                MarkDrugIndex: false,
                MarkDashboard: true,
                MarkScanCode: false,
                MarkMsfx: false,
                MarkAllRefreshable: false),
            "msfx" => new DirtyPlan(
                InvalidateDrugCatalog: false,
                MarkInventory: false,
                MarkDrugIndex: false,
                MarkDashboard: false,
                MarkScanCode: false,
                MarkMsfx: true,
                MarkAllRefreshable: false),
            _ => new DirtyPlan(
                InvalidateDrugCatalog: false,
                MarkInventory: false,
                MarkDrugIndex: false,
                MarkDashboard: false,
                MarkScanCode: false,
                MarkMsfx: false,
                MarkAllRefreshable: true),
        };
    }

    public static void ApplyDirtyPlan(
        DirtyPlan plan,
        Action? invalidateDrugCatalog,
        Func<Type, AppPageBase?> resolvePage,
        IEnumerable<AppPageBase> workspacePages,
        Func<AppPageBase, bool> canRefresh,
        Action<AppPageBase> markDirty)
    {
        if (plan.InvalidateDrugCatalog)
        {
            invalidateDrugCatalog?.Invoke();
        }

        if (plan.MarkInventory && resolvePage(typeof(InventoryOverview)) is { } inventory)
        {
            markDirty(inventory);
        }

        if (plan.MarkDrugIndex && resolvePage(typeof(DrugIndex)) is { } drugIndex)
        {
            markDirty(drugIndex);
        }

        if (plan.MarkDashboard && resolvePage(typeof(Dashboard)) is { } dashboard)
        {
            markDirty(dashboard);
        }

        if (plan.MarkScanCode && resolvePage(typeof(ScanCode)) is { } scanCode)
        {
            markDirty(scanCode);
        }

        if (plan.MarkMsfx && resolvePage(typeof(MsfxLink)) is { } msfx)
        {
            markDirty(msfx);
        }

        if (!plan.MarkAllRefreshable)
        {
            return;
        }

        foreach (var page in workspacePages)
        {
            if (canRefresh(page))
            {
                markDirty(page);
            }
        }
    }

    private static string Normalize(string? topic)
        => (topic ?? string.Empty).Trim().ToLowerInvariant();
}
