using PacToolkits.Application.Abstractions;

namespace PacToolkits.Application.Services;

/// <summary>归一化并发布 Desktop 本地条码生成配置</summary>
public sealed class BarcodeGenSettingsService : IBarcodeGenSettingsService
{
    internal const int MinWidthPx = 200;
    internal const int MaxWidthPx = 2000;
    internal const int MaxQuietZoneModules = 30;
    internal const int MaxFileNamePrefixLength = 64;
    internal const int MaxLabelUnitLength = 32;

    private readonly IBarcodeGenSettingsStore _store;
    private readonly object _gate = new();
    private BarcodeGenOptions _current = new();

    public BarcodeGenOptions Current
    {
        get
        {
            lock (_gate)
            {
                return Clone(_current);
            }
        }
    }

    public event Action? Changed;

    public BarcodeGenSettingsService(IBarcodeGenSettingsStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        Reload();
    }

    public void Apply(BarcodeGenOptions options)
    {
        var normalized = Normalize(options);
        lock (_gate)
        {
            if (Equals(_current, normalized))
            {
                return;
            }

            _current = normalized;
        }

        Changed?.Invoke();
    }

    public void Reload() => Apply(_store.Load());

    public async Task SaveAsync(BarcodeGenOptions options, CancellationToken ct = default)
    {
        var normalized = Normalize(options);
        await _store.SaveAsync(Clone(normalized), ct).ConfigureAwait(false);

        lock (_gate)
        {
            _current = normalized;
        }

        Changed?.Invoke();
    }

    private static bool Equals(BarcodeGenOptions left, BarcodeGenOptions right)
        => left.ExcludeRecentDays == right.ExcludeRecentDays
           && left.Export.FileNamePrefix == right.Export.FileNamePrefix
           && left.Image.WidthPx == right.Image.WidthPx
           && left.Image.QuietZoneModules == right.Image.QuietZoneModules
           && left.Image.Unit == right.Image.Unit;

    public static BarcodeGenOptions Normalize(BarcodeGenOptions? src)
    {
        var root = src ?? new BarcodeGenOptions();
        var image = root.Image ?? new BarcodeGenImageOptions();
        var export = root.Export ?? new BarcodeGenExportOptions();
        return new BarcodeGenOptions
        {
            ExcludeRecentDays = Math.Clamp(root.ExcludeRecentDays, 0, BarcodeGenOptions.MaxExcludeRecentDays),
            Image = new BarcodeGenImageOptions
            {
                WidthPx = Math.Clamp(image.WidthPx, MinWidthPx, MaxWidthPx),
                QuietZoneModules = Math.Clamp(image.QuietZoneModules, 0, MaxQuietZoneModules),
                Unit = TrimTo(image.Unit?.Trim() ?? string.Empty, MaxLabelUnitLength)
            },
            Export = new BarcodeGenExportOptions
            {
                FileNamePrefix = TrimTo(
                    string.IsNullOrWhiteSpace(export.FileNamePrefix)
                        ? BarcodeGenExportOptions.DefaultPrefix
                        : export.FileNamePrefix.Trim(),
                    MaxFileNamePrefixLength)
            }
        };
    }

    private static string TrimTo(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength];

    private static BarcodeGenOptions Clone(BarcodeGenOptions src)
        => Normalize(src);
}
