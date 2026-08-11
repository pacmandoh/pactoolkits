using PacToolkits.Desktop.Avalonia.ViewModels.Pages;

namespace PacToolkits.Desktop.Tests;

public sealed class InventoryReassignSelectionSyncTests
{
    [Fact]
    public void Partial_restore_sync_would_pollute_trace_selection_by_row_index()
    {
        var rows = CreateRows(
            ("trace-page1-a", false),
            ("trace-page1-b", false),
            ("trace-page1-c", false),
            ("trace-page1-d", false),
            ("trace-page1-e", true));

        var selectedByTrace = new Dictionary<string, StockRowSelection>(StringComparer.Ordinal)
        {
            ["trace-page1-e"] = StockRowSelection.From(rows[4]),
        };

        ApplyNextPageData(rows, CreateRows(
            ("trace-last-a", false),
            ("trace-last-b", false),
            ("trace-last-c", false),
            ("trace-last-d", false),
            ("trace-last-e", false)));

        RestoreChecksWithLiveSync(rows, selectedByTrace);

        Assert.Contains("trace-last-e", selectedByTrace.Keys);
        Assert.True(rows[4].IsSelected);
    }

    [Fact]
    public void Batch_restore_clears_stale_checks_before_trace_swap()
    {
        var rows = CreateRows(
            ("trace-page1-a", false),
            ("trace-page1-b", false),
            ("trace-page1-c", false),
            ("trace-page1-d", false),
            ("trace-page1-e", true));

        var selectedByTrace = new Dictionary<string, StockRowSelection>(StringComparer.Ordinal)
        {
            ["trace-page1-e"] = StockRowSelection.From(rows[4]),
        };

        foreach (var row in rows)
        {
            row.IsSelected = false;
        }

        ApplyNextPageData(rows, CreateRows(
            ("trace-last-a", false),
            ("trace-last-b", false),
            ("trace-last-c", false),
            ("trace-last-d", false),
            ("trace-last-e", false)));

        RestoreChecksWithoutLiveSync(rows, selectedByTrace);

        Assert.Single(selectedByTrace);
        Assert.Contains("trace-page1-e", selectedByTrace.Keys);
        Assert.DoesNotContain("trace-last-e", selectedByTrace.Keys);
        Assert.False(rows[4].IsSelected);
    }

    private static List<StockRowItem> CreateRows(params (string TraceCode, bool IsSelected)[] specs)
    {
        var rows = new List<StockRowItem>(specs.Length);
        for (var i = 0; i < specs.Length; i++)
        {
            var (traceCode, isSelected) = specs[i];
            rows.Add(new StockRowItem(
                rowNo: i + 1,
                drugId: "drug",
                spec: "spec",
                traceCode: traceCode,
                qty: 1,
                remain: 1,
                status: 0,
                version: 0,
                isLow: false,
                isDeprecated: false)
            {
                IsSelected = isSelected,
            });
        }

        return rows;
    }

    private static void ApplyNextPageData(List<StockRowItem> rows, IReadOnlyList<StockRowItem> nextPage)
    {
        for (var i = 0; i < rows.Count; i++)
        {
            var target = rows[i];
            var source = nextPage[i];
            target.RowNo = source.RowNo;
            target.DrugId = source.DrugId;
            target.Spec = source.Spec;
            target.TraceCode = source.TraceCode;
            target.Qty = source.Qty;
            target.Remain = source.Remain;
            target.Status = source.Status;
            target.Version = source.Version;
            target.IsLow = source.IsLow;
            target.IsDeprecated = source.IsDeprecated;
        }
    }

    private static void RestoreChecksWithLiveSync(
        IReadOnlyList<StockRowItem> rows,
        Dictionary<string, StockRowSelection> selectedByTrace)
    {
        foreach (var row in rows)
        {
            var key = row.TraceCode;
            row.IsSelected = selectedByTrace.ContainsKey(key);
            SyncSelectionFromRows(rows, selectedByTrace);
        }
    }

    private static void RestoreChecksWithoutLiveSync(
        IReadOnlyList<StockRowItem> rows,
        Dictionary<string, StockRowSelection> selectedByTrace)
    {
        foreach (var row in rows)
        {
            var key = row.TraceCode;
            row.IsSelected = selectedByTrace.ContainsKey(key);
        }
    }

    private static void SyncSelectionFromRows(
        IReadOnlyList<StockRowItem> rows,
        Dictionary<string, StockRowSelection> selectedByTrace)
    {
        foreach (var row in rows)
        {
            var key = row.TraceCode;
            if (row.IsSelected)
            {
                selectedByTrace[key] = StockRowSelection.From(row);
            }
            else
            {
                selectedByTrace.Remove(key);
            }
        }
    }
}
