namespace PacToolkits.Api.Hosting;

/// <summary>API 接受的库 schema 闭区间</summary>
public sealed class SchemaBoundsOptions
{
    public const string SectionName = "SchemaBounds";

    public string MinDbSchema { get; set; } = SchemaBoundsManifest.MinDbSchema;

    public string MaxDbSchema { get; set; } = SchemaBoundsManifest.MaxDbSchema;
}
