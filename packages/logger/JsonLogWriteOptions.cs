namespace PacToolkits.Logger;

/// <summary>写盘策略；与 Desktop LoggingOptions 对应字段对齐</summary>
public sealed class JsonLogWriteOptions
{
    public bool Enabled { get; init; } = true;

    public string MinimumLevel { get; init; } = LogLevel.Error;

    public int RetentionDays { get; init; } = 14;

    public int MaxFileSizeMb { get; init; } = 20;

    public IReadOnlyList<string> CleanupPatterns { get; init; } = [];

    public TimeSpan CleanupInterval { get; init; } = TimeSpan.FromHours(6);
}
