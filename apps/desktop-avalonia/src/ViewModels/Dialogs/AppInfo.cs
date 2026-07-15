using System;
using System.Globalization;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure;
using ShadUI;

namespace PacToolkits.Desktop.Avalonia.ViewModels.Dialogs;

public sealed class AppInfo(DialogManager dialogManager)
    : FormBase(dialogManager)
{
    public required AppInfoArgs Info { get; init; }

    public string VersionText => $"Version {Info.Version}";
    public string ReleaseText
        => DateOnly.TryParseExact(
            Info.ReleaseDate,
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var date)
            ? $"Released {date.ToString("MMM d, yyyy", CultureInfo.InvariantCulture)}"
            : $"Released {Info.ReleaseDate}";
}
