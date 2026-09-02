using PacToolkits.Application.Abstractions;
using PacToolkits.Application.Services;

namespace PacToolkits.Desktop.Tests.Application;

public sealed class BarcodeGenSettingsTests
{
    [Fact]
    public void Normalize_clamps_exclude_recent_days()
    {
        var options = BarcodeGenSettingsService.Normalize(new BarcodeGenOptions { ExcludeRecentDays = -3 });
        Assert.Equal(0, options.ExcludeRecentDays);

        options = BarcodeGenSettingsService.Normalize(new BarcodeGenOptions
        {
            ExcludeRecentDays = BarcodeGenOptions.MaxExcludeRecentDays + 10
        });
        Assert.Equal(BarcodeGenOptions.MaxExcludeRecentDays, options.ExcludeRecentDays);
    }

    [Fact]
    public void Normalize_clamps_image_and_text_fields()
    {
        var options = BarcodeGenSettingsService.Normalize(new BarcodeGenOptions
        {
            Image = new BarcodeGenImageOptions
            {
                WidthPx = 5000,
                QuietZoneModules = 99,
                Unit = new string('u', 64)
            },
            Export = new BarcodeGenExportOptions
            {
                FileNamePrefix = new string('p', 80)
            }
        });

        Assert.Equal(BarcodeGenSettingsService.MaxWidthPx, options.Image.WidthPx);
        Assert.Equal(BarcodeGenSettingsService.MaxQuietZoneModules, options.Image.QuietZoneModules);
        Assert.Equal(BarcodeGenSettingsService.MaxFileNamePrefixLength, options.Export.FileNamePrefix.Length);
        Assert.Equal(BarcodeGenSettingsService.MaxLabelUnitLength, options.Image.Unit.Length);
    }

    [Fact]
    public void Normalize_does_not_mutate_the_draft()
    {
        var draft = new BarcodeGenOptions
        {
            ExcludeRecentDays = -1,
            Image = new BarcodeGenImageOptions { WidthPx = 10 }
        };

        _ = BarcodeGenSettingsService.Normalize(draft);

        Assert.Equal(-1, draft.ExcludeRecentDays);
        Assert.Equal(10, draft.Image.WidthPx);
    }
}
