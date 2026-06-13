using CommunityToolkit.Mvvm.ComponentModel;
using System.Windows.Input;
using System.Collections.ObjectModel;
using System;
using PacToolkits.Application.DTOs;
using PacToolkits.Core;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure;

namespace PacToolkits.Desktop.Avalonia.ViewModels.Pages;

public interface IAboutPage { }

public sealed partial class AboutViewModel : AppPageBase, IAboutPage
{
    private readonly ReleaseVersionInfo _version;

    public AboutViewModel(IReleaseVersionService releaseVersion)
    {
        _version = releaseVersion.Current;
        var uiDbOk = DbSchemaCompat.IsSemVerAtLeast(_version.DbSchemaVersion, UiMinDbSchema);
        var agentDbOk = DbSchemaCompat.IsSemVerAtLeast(_version.DbSchemaVersion, AgentMinDbSchema);
        var compatSummary = uiDbOk && agentDbOk ? "DB Compatible" : "DB Check Required";

        VersionBadges = new ReadOnlyCollection<VersionBadgeItem>(new[]
        {
            new VersionBadgeItem("UI 最低 DB", UiMinDbSchema),
            new VersionBadgeItem("Agent 最低 DB", AgentMinDbSchema),
            new VersionBadgeItem("发布通道", _version.BuildChannel),
            new VersionBadgeItem("兼容状态", compatSummary),
            new VersionBadgeItem("版本来源", "version.generated.json")
        });
    }

    public override string DisplayName => "关于";
    public override string Icon => "Info";
    public override int Index => 1001;
    public override bool ShowInSidebar => false;
    public override ICommand? RefreshCommand => null;

#if DEBUG
    public bool IsDevLabVisible => true;
#else
    public bool IsDevLabVisible => false;
#endif

    [ObservableProperty] private double _waveDemoValue = 72;

    public string SuiteVersion => _version.SuiteVersion;
    public string UiVersion => _version.UiVersion;
    public string AhkVersion => _version.AgentVersion;
    public string DbSchemaVersion => _version.DbSchemaVersion;
    public string BuildChannel => _version.BuildChannel;
    public string BuildDate => _version.BuildDate;
    public string UiMinDbSchema => DbSchemaCompat.NormalizeBound(_version.UiMinDbSchema, _version.DbSchemaVersion);
    public string AgentMinDbSchema => DbSchemaCompat.NormalizeBound(_version.AgentMinDbSchema, _version.DbSchemaVersion);
    public string CompatUiDbRangeText => $"UI 最低 DB: {UiMinDbSchema}";
    public string CompatAgentDbRangeText => $"Agent 最低 DB: {AgentMinDbSchema}";
    public string VersionStatus => BuildVersionStatus();
    public string VersionHint => "版本由 release-manifest 统一生成并下发，UI 与 Agent 只读显示";

    public ReadOnlyCollection<VersionBadgeItem> VersionBadges { get; }

    private string BuildVersionStatus()
    {
        if (_version.UiVersion == "unknown" || _version.AgentVersion == "unknown" || _version.DbSchemaVersion == "unknown")
            return "Version Source Missing";

        var uiDbOk = DbSchemaCompat.IsSemVerAtLeast(_version.DbSchemaVersion, UiMinDbSchema);
        var agentDbOk = DbSchemaCompat.IsSemVerAtLeast(_version.DbSchemaVersion, AgentMinDbSchema);
        return uiDbOk && agentDbOk
            ? "Manifest Loaded / Compatible"
            : "Manifest Loaded / Compatibility Warning";
    }

}

public sealed record VersionBadgeItem(string Label, string Value)
{
    public string Display => $"{Label}: {Value}";
}
