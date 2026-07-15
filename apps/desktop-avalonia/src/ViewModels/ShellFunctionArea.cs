using System;
using System.Collections.Generic;
using System.Linq;

namespace PacToolkits.Desktop.Avalonia.ViewModels;

public sealed record ShellFunctionArea(string Id, string Name, string Subtitle, string Icon)
{
    public override string ToString() => Name;
}

public sealed record PageTab(string Text, string Icon);

public static class ShellFunctionAreas
{
    public const string TraceabilityId = "traceability";
    public const string AutomationId = "automation";

    public static ShellFunctionArea Traceability { get; } =
        new(TraceabilityId, "追溯码池", "Barcode", "GalleryVerticalEnd");

    public static ShellFunctionArea Automation { get; } =
        new(AutomationId, "自动化集成", "Agent · Msfx", "AudioWaveform");

    public static string TraceabilityName => Traceability.Name;
    public static string AutomationName => Automation.Name;
    public static string TraceabilityIcon => Traceability.Icon;
    public static string AutomationIcon => Automation.Icon;

    public static IReadOnlyList<ShellFunctionArea> All { get; } = [Traceability, Automation];

    public static ShellFunctionArea Resolve(string? id)
        => All.FirstOrDefault(area => string.Equals(area.Id, id, StringComparison.Ordinal))
           ?? Traceability;
}
