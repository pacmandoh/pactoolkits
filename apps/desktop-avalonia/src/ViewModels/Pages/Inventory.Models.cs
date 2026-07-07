using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using PacToolkits.Application.DTOs;
using PacToolkits.Application.Services;
using PacToolkits.Desktop.Avalonia.Common;

namespace PacToolkits.Desktop.Avalonia.ViewModels.Pages;

public sealed partial class StockRowItem : ObservableObject, ISelectableRow, INotifyDataErrorInfo
{
    private readonly Dictionary<string, List<string>> _errors = new(StringComparer.Ordinal);
    private TraceCodeValidationRule? _traceCodeRule;
    private bool _traceCodeEditValidationEnabled;

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
    [ObservableProperty] private bool _isSelected;

    public bool HasTraceCodeValidationError
        => _traceCodeEditValidationEnabled
           && GetErrors(nameof(TraceCode)).Cast<string>().Any();

    bool INotifyDataErrorInfo.HasErrors => _errors.Count > 0;

    public event EventHandler<DataErrorsChangedEventArgs>? ErrorsChanged;

    public IEnumerable GetErrors(string? propertyName)
    {
        if (string.IsNullOrEmpty(propertyName))
        {
            return _errors.Values.SelectMany(static x => x);
        }

        return _errors.TryGetValue(propertyName, out var errors)
            ? errors
            : Array.Empty<string>();
    }

    partial void OnTraceCodeChanged(string value)
        => ValidateTraceCodeEdit();

    public void EnableTraceCodeEditValidation(TraceCodeValidationRule rule)
    {
        _traceCodeRule = rule;
        _traceCodeEditValidationEnabled = true;
    }

    public void RefreshTraceCodeEditRule(TraceCodeValidationRule rule)
    {
        if (!_traceCodeEditValidationEnabled)
        {
            return;
        }

        _traceCodeRule = rule;
        ValidateTraceCodeEdit();
    }

    public void DisableTraceCodeEditValidation()
    {
        _traceCodeEditValidationEnabled = false;
        _traceCodeRule = null;
        SetValidationError(nameof(TraceCode), null);
    }

    private void ValidateTraceCodeEdit()
    {
        if (!_traceCodeEditValidationEnabled || _traceCodeRule is null)
        {
            return;
        }

        if (!TraceCodeAnalyzer.TryValidateFormat(TraceCode, _traceCodeRule, out var error))
        {
            SetValidationError(nameof(TraceCode), error);
            return;
        }

        SetValidationError(nameof(TraceCode), null);
    }

    private void SetValidationError(string propertyName, string? error)
    {
        if (string.IsNullOrWhiteSpace(error))
        {
            if (_errors.Remove(propertyName))
            {
                ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(propertyName));
                OnPropertyChanged(nameof(HasTraceCodeValidationError));
            }

            return;
        }

        _errors[propertyName] = [error];
        ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(propertyName));
        OnPropertyChanged(nameof(HasTraceCodeValidationError));
    }
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

public sealed record StockRowSelection(
    string TraceCode,
    string DrugId,
    string Spec,
    int Qty,
    int Remain)
{
    public static StockRowSelection From(StockRowItem row)
        => new(row.TraceCode, row.DrugId, row.Spec, row.Qty, row.Remain);
}

public sealed record StockReassignPreviewRowItem(
    string CurrentDrugId,
    string CurrentSpec,
    int CurrentQty,
    int CurrentRemain,
    string TargetDrugId,
    string TargetSpec,
    int TargetQty,
    int TargetRemain,
    string TraceCode
);

public sealed record PendingStockEdit(
    string MatchTraceCode,
    string ColumnHeader,
    string NewValue,
    string DrugId,
    string Spec);

public sealed record StockEditSnapshot(string TraceCode, int Remain);
