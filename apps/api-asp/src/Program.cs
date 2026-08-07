using System.Text.Json;
using PacToolkits.Api.Endpoints;
using PacToolkits.Api.Hosting;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.SerializerOptions.PropertyNameCaseInsensitive = true;
});

builder.Services.AddPacToolkitsApi(builder.Configuration);

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.MapHealth();
app.MapAuth();
app.MapPing();

app.Run();

/// <summary>WebApplicationFactory&lt;Program&gt; 入口锚点</summary>
public partial class Program;
