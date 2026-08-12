using System;
using Microsoft.Extensions.Options;

namespace PacToolkits.Desktop.Avalonia.Services.Infrastructure.Api;

/// <summary>Desktop 访问 PacToolkits.Api 的地址与密钥；设置页写入 AppConfigStore，保存后热应用</summary>
public sealed class PacApiOptions
{
    public string BaseUrl { get; set; } = string.Empty;

    public string ApiKey { get; set; } = string.Empty;

    public string HeaderName { get; set; } = "X-Api-Key";

    public bool IsConfigured
        => !string.IsNullOrWhiteSpace(BaseUrl) && !string.IsNullOrWhiteSpace(ApiKey);

    public static ValidateOptionsResult Validate(PacApiOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var hasUrl = !string.IsNullOrWhiteSpace(options.BaseUrl);
        var hasKey = !string.IsNullOrWhiteSpace(options.ApiKey);
        if (!hasUrl && !hasKey)
        {
            return ValidateOptionsResult.Success;
        }

        if (!hasUrl || !hasKey)
        {
            return ValidateOptionsResult.Fail("须同时填写服务地址与访问密钥，或全部留空");
        }

        var baseUrl = options.BaseUrl.Trim();
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return ValidateOptionsResult.Fail("服务地址须为绝对 http(s) URI");
        }

        if (!IsLoopback(uri) && uri.Scheme != Uri.UriSchemeHttps)
        {
            return ValidateOptionsResult.Fail("非本机地址须使用 HTTPS");
        }

        // Resolve 按根路径拼接；query/fragment/userinfo 会进意外地址
        if (!string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment)
            || !string.IsNullOrEmpty(uri.UserInfo))
        {
            return ValidateOptionsResult.Fail("服务地址不得包含 query、fragment 或 userinfo");
        }

        var header = (options.HeaderName ?? string.Empty).Trim();
        if (!IsHttpFieldName(header))
        {
            return ValidateOptionsResult.Fail("请求头名称无效");
        }

        return ValidateOptionsResult.Success;
    }

    private static bool IsLoopback(Uri uri)
        => uri.IsLoopback
           || string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase)
           || string.Equals(uri.Host, "127.0.0.1", StringComparison.OrdinalIgnoreCase)
           || string.Equals(uri.Host, "::1", StringComparison.OrdinalIgnoreCase);

    /// <summary>RFC 9110 token：字母数字与 !#$%&amp;'*+-.^_`|~</summary>
    private static bool IsHttpFieldName(string name)
    {
        if (name.Length == 0)
        {
            return false;
        }

        foreach (var c in name)
        {
            if (char.IsAsciiLetterOrDigit(c))
            {
                continue;
            }

            if ("!#$%&'*+-.^_`|~".Contains(c, StringComparison.Ordinal))
            {
                continue;
            }

            return false;
        }

        return true;
    }
}

/// <summary>IValidateOptions 适配；空配置合法，故不用 ValidateOnStart</summary>
internal sealed class PacApiOptionsValidator : IValidateOptions<PacApiOptions>
{
    public ValidateOptionsResult Validate(string? name, PacApiOptions options)
        => PacApiOptions.Validate(options);
}
