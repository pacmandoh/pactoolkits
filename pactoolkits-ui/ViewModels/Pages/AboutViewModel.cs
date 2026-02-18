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
        var uiCompatOk = IsSemVerGreaterOrEqual(_version.UiVersion, _version.AgentMinUi);
        var agentCompatOk = IsSemVerGreaterOrEqual(_version.AgentVersion, _version.UiMinAgent);
        var compatSummary = uiCompatOk && agentCompatOk ? "Compatible" : "Check Required";

        VersionBadges = new ReadOnlyCollection<VersionBadgeItem>(new[]
        {
            new VersionBadgeItem("Agent 要求 UI >=", _version.AgentMinUi),
            new VersionBadgeItem("UI 要求 Agent >=", _version.UiMinAgent),
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
    public string CompatAgentMinUi => _version.AgentMinUi;
    public string CompatUiMinAgent => _version.UiMinAgent;
    public string CompatAgentMinUiText => $"Agent 要求 UI >= {CompatAgentMinUi}";
    public string CompatUiMinAgentText => $"UI 要求 Agent >= {CompatUiMinAgent}";
    public string VersionStatus => BuildVersionStatus();
    public string VersionHint => "版本由 release-manifest 统一生成并下发，UI 与 Agent 只读显示。";

    public ReadOnlyCollection<VersionBadgeItem> VersionBadges { get; }

    private string BuildVersionStatus()
    {
        if (_version.UiVersion == "unknown" || _version.AgentVersion == "unknown" || _version.DbSchemaVersion == "unknown")
            return "Version Source Missing";

        var uiCompatOk = IsSemVerGreaterOrEqual(_version.UiVersion, _version.AgentMinUi);
        var agentCompatOk = IsSemVerGreaterOrEqual(_version.AgentVersion, _version.UiMinAgent);
        return uiCompatOk && agentCompatOk ? "Manifest Loaded / Compatible" : "Manifest Loaded / Compatibility Warning";
    }

    private static bool IsSemVerGreaterOrEqual(string current, string required)
    {
        if (!TryParseSemVer(current, out var c) || !TryParseSemVer(required, out var r))
            return false;

        if (c.major != r.major)
            return c.major > r.major;
        if (c.minor != r.minor)
            return c.minor > r.minor;
        return c.patch >= r.patch;
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
}

public sealed record VersionBadgeItem(string Label, string Value)
{
    public string Display => $"{Label}: {Value}";
}
