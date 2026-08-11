namespace PacToolkits.Application.Services;

/// <summary>
/// 将空白输入规范化为 <see langword="null"/>，避免空字符串改变查询语义
/// </summary>
public static class InputNormalizer
{
    public static string? Normalize(string? value)
    {
        if (value is null)
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }
}
