using CommunityToolkit.Mvvm.ComponentModel;

namespace PacToolkits.Desktop.Avalonia.ViewModels.Pages;

public sealed partial class StockRowItem : ObservableObject
{
    public StockRowItem(
        int rowNo,
        string drugId,
        string spec,
        string traceCode,
        int qty,
        int remain,
        int status,
        bool isLow,
        bool isDeprecated)
    {
        RowNo = rowNo;
        DrugId = drugId;
        Spec = spec;
        TraceCode = traceCode;
        Qty = qty;
        Remain = remain;
        Status = status;
        IsLow = isLow;
        IsDeprecated = isDeprecated;
    }

    [ObservableProperty] private int _rowNo;

    [ObservableProperty] private string _drugId;
    [ObservableProperty] private string _spec;
    [ObservableProperty] private string _traceCode;
    [ObservableProperty] private int _qty;
    [ObservableProperty] private int _remain;
    [ObservableProperty] private int _status;
    [ObservableProperty] private bool _isLow;
    [ObservableProperty] private bool _isDeprecated;
}

public sealed record DrugSpecAggRowItem(
    int RowNo,
    string DrugId,
    string Spec,
    long CodeCount,
    long QtySum,
    long RemainSum,
    long WeekUsed,
    decimal Threshold,
    bool IsLow,
    bool IsDeprecated
);

public sealed record LowStockRowItem(
    int RowNo,
    string DrugId,
    string Spec,
    long RemainSum,
    decimal Threshold,
    bool IsLow
);

public sealed record MissingStockRowItem(
    int RowNo,
    string DrugId,
    string Spec,
    string? Note
);

public sealed record StockReassignPreviewRowItem(
    string CurrentDrugId,
    string CurrentSpec,
    int CurrentQty,
    int CurrentRemain,
    string TargetDrugId,
    string TargetSpec,
    int TargetQty,
    string TraceCode
);

public sealed record PendingStockEdit(
    string MatchTraceCode,
    string ColumnHeader,
    string NewValue,
    string DrugId,
    string Spec);

public sealed record StockEditSnapshot(string TraceCode, int Remain);
