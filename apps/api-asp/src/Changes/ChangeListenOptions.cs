namespace PacToolkits.Api.Changes;

/// <summary>变更流宿主开关（Changes 配置节）</summary>
public sealed class ChangeListenOptions
{
    public const string SectionName = "Changes";

    public bool ListenEnabled { get; set; } = true;
}
