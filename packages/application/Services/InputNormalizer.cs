namespace PacToolkits.Application.Services;

/// <summary>
/// 将空白输入规范为 null，避免空串参与查询
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
