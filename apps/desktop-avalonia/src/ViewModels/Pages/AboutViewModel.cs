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
        var desktopDbOk = DbSchemaCompat.IsSemVerAtLeast(_version.DatabasePostgresVersion, DesktopMinDbSchema);
        var agentDbOk = DbSchemaCompat.IsSemVerAtLeast(_version.DatabasePostgresVersion, AgentInjectorAhkMinDbSchema);
        var compatSummary = desktopDbOk && agentDbOk ? "DB Compatible" : "DB Check Required";

        VersionBadges = new ReadOnlyCollection<VersionBadgeItem>(new[]
        {
            new VersionBadgeItem("Desktop 最低 DB", DesktopMinDbSchema),
            new VersionBadgeItem("Agent 最低 DB", AgentInjectorAhkMinDbSchema),
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

    public string ProductVersion => _version.ProductVersion;
    public string DesktopVersion => _version.DesktopVersion;
    public string AgentInjectorAhkVersion => _version.AgentInjectorAhkVersion;
    public string DatabasePostgresVersion => _version.DatabasePostgresVersion;
    public string BuildChannel => _version.BuildChannel;
    public string BuildDate => _version.BuildDate;
    public string DesktopMinDbSchema => DbSchemaCompat.NormalizeBound(_version.DesktopMinDbSchema, _version.DatabasePostgresVersion);
    public string AgentInjectorAhkMinDbSchema => DbSchemaCompat.NormalizeBound(_version.AgentInjectorAhkMinDbSchema, _version.DatabasePostgresVersion);
    public string CompatDesktopDbRangeText => $"Desktop 最低 DB: {DesktopMinDbSchema}";
    public string CompatAgentDbRangeText => $"Agent 最低 DB: {AgentInjectorAhkMinDbSchema}";
    public string VersionStatus => BuildVersionStatus();
    public string VersionHint => "版本由 release-manifest.json (schema v2) 统一生成并下发";

    public ReadOnlyCollection<VersionBadgeItem> VersionBadges { get; }

    private string BuildVersionStatus()
    {
        if (_version.DesktopVersion == "unknown" || _version.AgentInjectorAhkVersion == "unknown" || _version.DatabasePostgresVersion == "unknown")
            return "Version Source Missing";

        var desktopDbOk = DbSchemaCompat.IsSemVerAtLeast(_version.DatabasePostgresVersion, DesktopMinDbSchema);
        var agentDbOk = DbSchemaCompat.IsSemVerAtLeast(_version.DatabasePostgresVersion, AgentInjectorAhkMinDbSchema);
        return desktopDbOk && agentDbOk
            ? "Manifest Loaded / Compatible"
            : "Manifest Loaded / Compatibility Warning";
    }

}

public sealed record VersionBadgeItem(string Label, string Value)
{
    public string Display => $"{Label}: {Value}";
}
