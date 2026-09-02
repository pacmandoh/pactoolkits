using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;

namespace PacToolkits.Api.Tests;

public sealed class TraceBarcodesEndpointsTests
{
    [Fact]
    public async Task Pick_requires_write_scope()
    {
        var traceBarcode = new FakeTraceBarcode();
        await using var factory = new ApiFactory { TraceBarcode = traceBarcode };
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            ApiFactory.ForgeAccessToken(scopes: ["read"]));

        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/trace-barcodes/pick")
        {
            Content = new StringContent(
                """{"batchId":"11111111-1111-1111-1111-111111111111","operatorName":"op@host","items":[{"drugId":"d1","spec":"s1","count":1}]}""",
                Encoding.UTF8,
                "application/json"),
        };
        request.Headers.TryAddWithoutValidation(PacApiHeaders.CommandId, Guid.NewGuid().ToString("D"));

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, traceBarcode.PickCalls);
    }

    [Fact]
    public async Task Pick_returns_result()
    {
        var traceBarcode = new FakeTraceBarcode();
        await using var factory = new ApiFactory { TraceBarcode = traceBarcode };
        using var client = factory.CreateClient();
        var token = await ApiFactory.FetchAccessTokenAsync(client, TestContext.Current.CancellationToken);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/trace-barcodes/pick")
        {
            Content = new StringContent(
                """
                {
                  "batchId": "11111111-1111-1111-1111-111111111111",
                  "operatorName": "op@host",
                  "items": [{ "drugId": "d1", "spec": "s1", "count": 2 }],
                  "excludeRecentDays": 7,
                  "excludeTraceCodes": ["seed"]
                }
                """,
                Encoding.UTF8,
                "application/json"),
        };
        request.Headers.TryAddWithoutValidation(PacApiHeaders.CommandId, Guid.NewGuid().ToString("D"));
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, traceBarcode.PickCalls);
        using var doc = JsonDocument.Parse(body);
        Assert.Equal("d1", doc.RootElement.GetProperty("groups")[0].GetProperty("drugId").GetString());
    }

    [Fact]
    public async Task Pick_requires_command_id()
    {
        await using var factory = new ApiFactory { TraceBarcode = new FakeTraceBarcode() };
        using var client = factory.CreateClient();
        var token = await ApiFactory.FetchAccessTokenAsync(client, TestContext.Current.CancellationToken);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/trace-barcodes/pick")
        {
            Content = new StringContent("""{"items":[]}""", Encoding.UTF8, "application/json"),
        };
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Pick_replays_completed_command()
    {
        var traceBarcode = new FakeTraceBarcode();
        await using var factory = new ApiFactory { TraceBarcode = traceBarcode };
        using var client = factory.CreateClient();
        var token = await ApiFactory.FetchAccessTokenAsync(client, TestContext.Current.CancellationToken);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var commandId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var json =
            """{"batchId":"22222222-2222-2222-2222-222222222222","operatorName":"op@host","items":[{"drugId":"d1","spec":"s1","count":1}]}""";

        async Task<HttpResponseMessage> SendOnce()
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/trace-barcodes/pick")
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
            request.Headers.TryAddWithoutValidation(PacApiHeaders.CommandId, commandId.ToString("D"));
            return await client.SendAsync(request, TestContext.Current.CancellationToken);
        }

        using var first = await SendOnce();
        using var second = await SendOnce();

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(1, traceBarcode.PickCalls);
    }

    [Fact]
    public async Task Audit_requires_command_id()
    {
        await using var factory = new ApiFactory { TraceBarcode = new FakeTraceBarcode() };
        using var client = factory.CreateClient();
        var token = await ApiFactory.FetchAccessTokenAsync(client, TestContext.Current.CancellationToken);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/trace-barcodes/audit")
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json"),
        };
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Audit_returns_inserted_count()
    {
        var traceBarcode = new FakeTraceBarcode { AuditInserted = 2 };
        await using var factory = new ApiFactory { TraceBarcode = traceBarcode };
        using var client = factory.CreateClient();
        var token = await ApiFactory.FetchAccessTokenAsync(client, TestContext.Current.CancellationToken);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/trace-barcodes/audit")
        {
            Content = new StringContent(
                """
                {
                  "batchId": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
                  "action": "preview",
                  "operatorName": "op@host",
                  "items": [
                    { "traceCode": "T1", "drugId": "d1", "spec": "s1", "fileName": null, "success": true, "error": null },
                    { "traceCode": "T2", "drugId": "d1", "spec": "s1", "fileName": null, "success": true, "error": null }
                  ]
                }
                """,
                Encoding.UTF8,
                "application/json"),
        };
        request.Headers.TryAddWithoutValidation(PacApiHeaders.CommandId, Guid.NewGuid().ToString("D"));
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, traceBarcode.AuditCalls);
        using var doc = JsonDocument.Parse(body);
        Assert.Equal(2, doc.RootElement.GetProperty("inserted").GetInt32());
    }

    [Fact]
    public async Task Audit_replays_completed_command()
    {
        var traceBarcode = new FakeTraceBarcode { AuditInserted = 1 };
        await using var factory = new ApiFactory { TraceBarcode = traceBarcode };
        using var client = factory.CreateClient();
        var token = await ApiFactory.FetchAccessTokenAsync(client, TestContext.Current.CancellationToken);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var commandId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
        var json = """
            {
              "batchId": "dddddddd-dddd-dddd-dddd-dddddddddddd",
              "action": "export",
              "operatorName": "op@host",
              "items": [
                { "traceCode": "T1", "drugId": "d1", "spec": "s1", "fileName": "a.png", "success": true, "error": null }
              ]
            }
            """;

        async Task<HttpResponseMessage> SendOnce()
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/trace-barcodes/audit")
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
            request.Headers.TryAddWithoutValidation(PacApiHeaders.CommandId, commandId.ToString("D"));
            return await client.SendAsync(request, TestContext.Current.CancellationToken);
        }

        using var first = await SendOnce();
        using var second = await SendOnce();
        var firstBody = await first.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var secondBody = await second.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(firstBody, secondBody);
        Assert.Equal(1, traceBarcode.AuditCalls);
    }

    [Fact]
    public async Task Audit_maps_argument_exception_to_400()
    {
        await using var factory = new ApiFactory { TraceBarcode = new FakeTraceBarcode { ThrowArgumentOnAudit = true } };
        using var client = factory.CreateClient();
        var token = await ApiFactory.FetchAccessTokenAsync(client, TestContext.Current.CancellationToken);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/trace-barcodes/audit")
        {
            Content = new StringContent(
                """
                {
                  "batchId": "00000000-0000-0000-0000-000000000000",
                  "action": "preview",
                  "operatorName": "op@host",
                  "items": [{ "traceCode": "T1", "drugId": null, "spec": null, "fileName": null, "success": true, "error": null }]
                }
                """,
                Encoding.UTF8,
                "application/json"),
        };
        request.Headers.TryAddWithoutValidation(PacApiHeaders.CommandId, Guid.NewGuid().ToString("D"));
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Pick_maps_argument_exception_to_400()
    {
        await using var factory = new ApiFactory { TraceBarcode = new FakeTraceBarcode { ThrowArgumentOnPick = true } };
        using var client = factory.CreateClient();
        var token = await ApiFactory.FetchAccessTokenAsync(client, TestContext.Current.CancellationToken);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/trace-barcodes/pick")
        {
            Content = new StringContent(
                """{"batchId":"33333333-3333-3333-3333-333333333333","operatorName":"op@host","items":[{"drugId":"d1","spec":"s1","count":0}]}""",
                Encoding.UTF8,
                "application/json"),
        };
        request.Headers.TryAddWithoutValidation(PacApiHeaders.CommandId, Guid.NewGuid().ToString("D"));
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ClearAuditLog_requires_command_id()
    {
        await using var factory = new ApiFactory { TraceBarcode = new FakeTraceBarcode() };
        using var client = factory.CreateClient();
        var token = await ApiFactory.FetchAccessTokenAsync(client, TestContext.Current.CancellationToken);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/trace-barcodes/audit-log/clear");
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ClearAuditLog_returns_deleted_count()
    {
        var traceBarcode = new FakeTraceBarcode { ClearDeleted = 3 };
        await using var factory = new ApiFactory { TraceBarcode = traceBarcode };
        using var client = factory.CreateClient();
        var token = await ApiFactory.FetchAccessTokenAsync(client, TestContext.Current.CancellationToken);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/trace-barcodes/audit-log/clear")
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json"),
        };
        request.Headers.TryAddWithoutValidation(PacApiHeaders.CommandId, Guid.NewGuid().ToString("D"));
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, traceBarcode.ClearCalls);
        using var doc = JsonDocument.Parse(body);
        Assert.Equal(3, doc.RootElement.GetProperty("deleted").GetInt32());
    }

    [Fact]
    public async Task ClearAuditLog_replays_completed_command()
    {
        var traceBarcode = new FakeTraceBarcode { ClearDeleted = 5 };
        await using var factory = new ApiFactory { TraceBarcode = traceBarcode };
        using var client = factory.CreateClient();
        var token = await ApiFactory.FetchAccessTokenAsync(client, TestContext.Current.CancellationToken);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var commandId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");

        async Task<HttpResponseMessage> SendOnce()
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/trace-barcodes/audit-log/clear")
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json"),
            };
            request.Headers.TryAddWithoutValidation(PacApiHeaders.CommandId, commandId.ToString("D"));
            return await client.SendAsync(request, TestContext.Current.CancellationToken);
        }

        using var first = await SendOnce();
        using var second = await SendOnce();
        var firstBody = await first.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var secondBody = await second.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(firstBody, secondBody);
        Assert.Equal(1, traceBarcode.ClearCalls);
    }

    private sealed class FakeTraceBarcode : ITraceBarcodeService
    {
        public event Action? AuditCleared
        {
            add { }
            remove { }
        }

        public int PickCalls { get; private set; }

        public int AuditCalls { get; private set; }

        public int ClearCalls { get; private set; }

        public int AuditInserted { get; init; } = 1;

        public int ClearDeleted { get; init; }

        public bool ThrowArgumentOnAudit { get; init; }

        public bool ThrowArgumentOnPick { get; init; }

        public Task<TraceBarcodePickResult> PickAsync(TraceBarcodePickRequest request, CancellationToken ct)
        {
            PickCalls++;
            if (ThrowArgumentOnPick)
            {
                throw new ArgumentException("pick count must be greater than zero");
            }

            var group = new TraceBarcodePickGroup(
                "d1",
                "s1",
                1,
                1,
                0,
                [new TraceBarcodePickedRow("T1", "d1", "s1", 1, 1, null)]);
            return Task.FromResult(new TraceBarcodePickResult([group]));
        }

        public Task<int> AuditAsync(TraceBarcodeAuditRequest request, CancellationToken ct)
        {
            AuditCalls++;
            if (ThrowArgumentOnAudit)
            {
                throw new ArgumentException("batchId must be non-empty");
            }

            return Task.FromResult(AuditInserted);
        }

        public Task<int> ClearAuditLogAsync(CancellationToken ct, Guid? commandId = null)
        {
            ClearCalls++;
            return Task.FromResult(ClearDeleted);
        }
    }
}
