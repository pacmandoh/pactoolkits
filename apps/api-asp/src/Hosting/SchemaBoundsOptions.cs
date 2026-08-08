namespace PacToolkits.Api.Hosting;

/// <summary>API 接受的库 schema 闭区间</summary>
public sealed class SchemaBoundsOptions
{
    public const string SectionName = "SchemaBounds";

    public string MinDbSchema { get; set; } = "1.2.25";

    public string MaxDbSchema { get; set; } = "1.2.25";
}
