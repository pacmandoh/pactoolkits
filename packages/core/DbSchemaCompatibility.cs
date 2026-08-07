namespace PacToolkits.Core;

/// <summary>
/// 数据库 schema 与声明范围（或读库结果）的兼容状态
/// </summary>
public enum DbSchemaCompatibility
{
    Unknown,
    MetadataMissing,
    BelowMinimum,
    Compatible,
    AboveMaximum,
}

/// <summary>
/// Schema 兼容判定结果
///
/// Message 仅承载读库 Reason；区间 FromRange 不写文案
/// </summary>
public sealed record DbSchemaCompatibilityResult(
    DbSchemaCompatibility Status,
    string CurrentVersion,
    string MinimumVersion,
    string MaximumVersion,
    string Message)
{
    public bool IsCompatible => Status == DbSchemaCompatibility.Compatible;

    /// <summary>区间 Status 映射；不含 MetadataMissing，Message 留空</summary>
    public static DbSchemaCompatibilityResult FromRange(SemVerRangeResult range)
        => new(
            range.Status switch
            {
                SemVerRangeStatus.BelowMinimum => DbSchemaCompatibility.BelowMinimum,
                SemVerRangeStatus.AboveMaximum => DbSchemaCompatibility.AboveMaximum,
                SemVerRangeStatus.Compatible => DbSchemaCompatibility.Compatible,
                _ => DbSchemaCompatibility.Unknown,
            },
            range.Current,
            range.Minimum,
            range.Maximum,
            Message: string.Empty);
}
