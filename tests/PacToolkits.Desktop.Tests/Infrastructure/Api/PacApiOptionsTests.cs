using PacToolkits.Desktop.Avalonia.Services.Infrastructure.Api;

namespace PacToolkits.Desktop.Tests;

public sealed class PacApiOptionsTests
{
    [Fact]
    public void Empty_config_is_valid_when_pacapi_disabled()
        => Assert.True(PacApiOptions.Validate(new PacApiOptions()).Succeeded);

    [Fact]
    public void BaseUrl_without_api_key_fails()
        => Assert.False(PacApiOptions.Validate(new PacApiOptions { BaseUrl = "http://127.0.0.1:5080" }).Succeeded);

    [Fact]
    public void ApiKey_without_base_url_fails()
        => Assert.False(PacApiOptions.Validate(new PacApiOptions { ApiKey = "k" }).Succeeded);

    [Fact]
    public void Loopback_http_allowed()
    {
        var o = new PacApiOptions
        {
            BaseUrl = "http://127.0.0.1:5080",
            ApiKey = "k",
        };
        Assert.True(PacApiOptions.Validate(o).Succeeded);
    }

    [Fact]
    public void Private_ipv4_http_allowed()
    {
        var o = new PacApiOptions
        {
            BaseUrl = "http://192.168.1.10:5080",
            ApiKey = "k",
        };
        Assert.True(PacApiOptions.Validate(o).Succeeded);
    }

    [Fact]
    public void Single_label_hostname_http_allowed()
    {
        var o = new PacApiOptions
        {
            BaseUrl = "http://pac-api-host:5080",
            ApiKey = "k",
        };
        Assert.True(PacApiOptions.Validate(o).Succeeded);
    }

    [Fact]
    public void Public_host_http_rejected()
    {
        var o = new PacApiOptions
        {
            BaseUrl = "http://api.example.com",
            ApiKey = "k",
        };
        Assert.False(PacApiOptions.Validate(o).Succeeded);
    }

    [Fact]
    public void Absolute_https_ok()
    {
        var o = new PacApiOptions
        {
            BaseUrl = "https://api.example.com",
            ApiKey = "k",
            HeaderName = "X-Api-Key",
        };
        Assert.True(PacApiOptions.Validate(o).Succeeded);
    }

    [Fact]
    public void HeaderName_with_space_fails()
    {
        var o = new PacApiOptions
        {
            BaseUrl = "http://127.0.0.1:5080",
            ApiKey = "k",
            HeaderName = "Bad Header",
        };
        Assert.False(PacApiOptions.Validate(o).Succeeded);
    }

    [Fact]
    public void BaseUrl_with_query_fails()
    {
        var o = new PacApiOptions
        {
            BaseUrl = "http://127.0.0.1:5080/?x=1",
            ApiKey = "k",
        };
        Assert.False(PacApiOptions.Validate(o).Succeeded);
    }

    [Fact]
    public void BaseUrl_with_userinfo_fails()
    {
        var o = new PacApiOptions
        {
            BaseUrl = "http://user:pass@127.0.0.1:5080",
            ApiKey = "k",
        };
        Assert.False(PacApiOptions.Validate(o).Succeeded);
    }
}
