using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.HttpOverrides;
using PacToolkits.Api.Endpoints;
using PacToolkits.Api.Hosting;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.SerializerOptions.PropertyNameCaseInsensitive = true;
});

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    // 清空默认后只信任同机环回反向代理（nginx 等）；其它网段在部署时写入 KnownProxies / KnownIPNetworks
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
    options.KnownProxies.Add(IPAddress.Loopback);
    options.KnownProxies.Add(IPAddress.IPv6Loopback);
});

builder.Services.AddPacToolkitsApi(builder.Configuration);

var app = builder.Build();

app.UseForwardedHeaders();
app.UsePacToolkitsProblemDetails();
app.UseMiddleware<RequestAuditMiddleware>();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<DbAccessGateMiddleware>();

app.MapHealth();
app.MapAuth();
app.MapPing();
app.MapSystemInfo();
app.MapChanges();
app.MapDashboard();
app.MapCatalog();
app.MapDrugs();
app.MapTraceCodes();
app.MapInventory();
app.MapMsfx();
app.MapMsfxAutoRun();
app.MapInjector();

app.Run();

/// <summary>WebApplicationFactory 测试入口</summary>
public partial class Program;
