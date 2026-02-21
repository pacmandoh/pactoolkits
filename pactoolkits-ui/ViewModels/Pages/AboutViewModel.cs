using Material.Icons;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Windows.Input;
using System.Collections.ObjectModel;
using System;
using pactoolkits_ui.Services;

namespace pactoolkits_ui.ViewModels.Pages;

public interface IAboutPage { }

public sealed partial class AboutViewModel : AppPageBase, IAboutPage
{
    private readonly ReleaseVersionInfo _version;

    public AboutViewModel(IReleaseVersionService releaseVersion)
    {
        _version = releaseVersion.Current;
        var uiDbOk = IsSemVerInRange(_version.DbSchemaVersion, UiMinDbSchema, UiMaxDbSchema);
        var agentDbOk = IsSemVerInRange(_version.DbSchemaVersion, AgentMinDbSchema, AgentMaxDbSchema);
        var compatSummary = uiDbOk && agentDbOk ? "DB Compatible" : "DB Check Required";

        VersionBadges = new ReadOnlyCollection<VersionBadgeItem>(new[]
        {
            new VersionBadgeItem("UI 兼容 DB", $"{UiMinDbSchema} ~ {UiMaxDbSchema}"),
            new VersionBadgeItem("Agent 兼容 DB", $"{AgentMinDbSchema} ~ {AgentMaxDbSchema}"),
            new VersionBadgeItem("发布通道", _version.BuildChannel),
            new VersionBadgeItem("兼容状态", compatSummary),
            new VersionBadgeItem("版本来源", "version.generated.json")
        });
    }

    public override string DisplayName => "关于";
    public override MaterialIconKind Icon => MaterialIconKind.InformationOutline;
    public override int Index => 1001;
    public override bool ShowInSidebar => false;
    public override ICommand? RefreshCommand => null;

    [ObservableProperty] private double _waveDemoValue = 72;

    public string SuiteVersion => _version.SuiteVersion;
    public string UiVersion => _version.UiVersion;
    public string AhkVersion => _version.AgentVersion;
    public string DbSchemaVersion => _version.DbSchemaVersion;
    public string BuildChannel => _version.BuildChannel;
    public string BuildDate => _version.BuildDate;
    public string UiMinDbSchema => NormalizeSchemaBound(_version.UiMinDbSchema, _version.DbSchemaVersion);
    public string UiMaxDbSchema => NormalizeSchemaBound(_version.UiMaxDbSchema, _version.DbSchemaVersion);
    public string AgentMinDbSchema => NormalizeSchemaBound(_version.AgentMinDbSchema, _version.DbSchemaVersion);
    public string AgentMaxDbSchema => NormalizeSchemaBound(_version.AgentMaxDbSchema, _version.DbSchemaVersion);
    public string CompatUiDbRangeText => $"UI 兼容 DB: {UiMinDbSchema} ~ {UiMaxDbSchema}";
    public string CompatAgentDbRangeText => $"Agent 兼容 DB: {AgentMinDbSchema} ~ {AgentMaxDbSchema}";
    public string VersionStatus => BuildVersionStatus();
    public string VersionHint => "版本由 release-manifest 统一生成并下发，UI 与 Agent 只读显示。";

    public ReadOnlyCollection<VersionBadgeItem> VersionBadges { get; }

    private string BuildVersionStatus()
    {
        if (_version.UiVersion == "unknown" || _version.AgentVersion == "unknown" || _version.DbSchemaVersion == "unknown")
            return "Version Source Missing";

        var uiDbOk = IsSemVerInRange(_version.DbSchemaVersion, UiMinDbSchema, UiMaxDbSchema);
        var agentDbOk = IsSemVerInRange(_version.DbSchemaVersion, AgentMinDbSchema, AgentMaxDbSchema);
        return uiDbOk && agentDbOk
            ? "Manifest Loaded / Compatible"
            : "Manifest Loaded / Compatibility Warning";
    }

    private static bool TryParseSemVer(string value, out (int major, int minor, int patch) ver)
    {
        ver = (0, 0, 0);
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var parts = value.Split('.', StringSplitOptions.TrimEntries);
        if (parts.Length != 3)
            return false;

        if (!int.TryParse(parts[0], out var major)) return false;
        if (!int.TryParse(parts[1], out var minor)) return false;
        if (!int.TryParse(parts[2], out var patch)) return false;

        ver = (major, minor, patch);
        return true;
    }

    private static bool IsSemVerInRange(string value, string min, string max)
    {
        if (!TryParseSemVer(value, out var v) || !TryParseSemVer(min, out var minV) || !TryParseSemVer(max, out var maxV))
            return false;
        return CompareSemVer(v, minV) >= 0 && CompareSemVer(v, maxV) <= 0;
    }

    private static int CompareSemVer((int major, int minor, int patch) left, (int major, int minor, int patch) right)
    {
        if (left.major != right.major) return left.major.CompareTo(right.major);
        if (left.minor != right.minor) return left.minor.CompareTo(right.minor);
        return left.patch.CompareTo(right.patch);
    }

    private static string NormalizeSchemaBound(string bound, string fallback)
        => string.Equals(bound, "unknown", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(bound)
            ? fallback
            : bound;
}

public sealed record VersionBadgeItem(string Label, string Value)
{
    public string Display => $"{Label}: {Value}";
}
