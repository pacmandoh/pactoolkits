using System;
using System.Collections.Generic;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure.Dialogs;

namespace PacToolkits.Desktop.Avalonia.ViewModels.Pages;

public enum PoolPickStatus
{
    None,
    Ok,
    Gap
}

public sealed partial class PoolRow : ObservableObject
{
    public PoolRow(string drugId, string spec, int count)
    {
        DrugId = drugId;
        Spec = spec;
        Count = count;
    }

    public string DrugId { get; }
    public string Spec { get; }

    [ObservableProperty] private int _count;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPickOk))]
    [NotifyPropertyChangedFor(nameof(IsPickGap))]
    private PoolPickStatus _pickStatus;

    [ObservableProperty] private string? _statusHint;

    public bool IsPickOk => PickStatus == PoolPickStatus.Ok;

    public bool IsPickGap => PickStatus == PoolPickStatus.Gap;

    public void ResetPickStatus()
    {
        PickStatus = PoolPickStatus.None;
        StatusHint = null;
    }

    partial void OnCountChanged(int value) => ResetPickStatus();
}

public sealed partial class PreviewCard : ObservableObject, IDisposable
{
    public PreviewCard(
        string traceCode,
        string? drugId,
        string? spec,
        string title,
        byte[] pngBytes,
        string fileName,
        IReadOnlyList<InfoDetailItem> detailItems)
    {
        TraceCode = traceCode;
        DrugId = drugId;
        Spec = spec;
        Title = title;
        PngBytes = pngBytes;
        FileName = fileName;
        DetailItems = detailItems;
    }

    public string TraceCode { get; }
    public string? DrugId { get; }
    public string? Spec { get; }
    public string Title { get; }
    public byte[] PngBytes { get; }
    public string FileName { get; }
    public IReadOnlyList<InfoDetailItem> DetailItems { get; }

    [ObservableProperty] private Bitmap? _previewImage;

    public void Dispose()
    {
        PreviewImage?.Dispose();
        PreviewImage = null;
    }
}
