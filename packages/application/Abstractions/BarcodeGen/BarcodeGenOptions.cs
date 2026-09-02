namespace PacToolkits.Application.Abstractions;

public sealed class BarcodeGenOptions
{
    public const int MaxExcludeRecentDays = 365;

    public BarcodeGenImageOptions Image { get; set; } = new();
    public BarcodeGenExportOptions Export { get; set; } = new();
    public int ExcludeRecentDays { get; set; } = 7;
}

public sealed class BarcodeGenImageOptions
{
    public int WidthPx { get; set; } = 600;
    public int QuietZoneModules { get; set; } = 10;
    public string Unit { get; set; } = string.Empty;
}

public sealed class BarcodeGenExportOptions
{
    public const string DefaultPrefix = "PacToolkits";

    public string FileNamePrefix { get; set; } = DefaultPrefix;
}
