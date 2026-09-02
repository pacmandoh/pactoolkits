using System.IO;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.Input;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure.Dialogs;
using ShadUI;

namespace PacToolkits.Desktop.Avalonia.ViewModels.Dialogs;

/// <summary>在对话框生命周期内持有条码预览 Bitmap</summary>
public sealed partial class BarcodePreviewDetail(DialogManager dialogManager) : FormBase(dialogManager)
{
    public required BarcodePreviewDetailArgs Detail { get; init; }

    public Bitmap? PreviewImage { get; private set; }

    internal void Initialize()
    {
        using var stream = new MemoryStream(Detail.PngBytes);
        PreviewImage = new Bitmap(stream);
    }

    internal void ReleasePreview()
    {
        PreviewImage?.Dispose();
        PreviewImage = null;
    }

    [RelayCommand]
    private void Close()
    {
        CloseDialog(success: true);
    }
}
