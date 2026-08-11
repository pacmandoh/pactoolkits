using System.Net;
using System.Net.Http.Headers;
using System.Text;
using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;

namespace PacToolkits.Api.Tests;

public sealed class TraceCodesEndpointsTests
{
    [Fact]
    public async Task Submit_returns_result()
    {
        var scan = new FakeScanCode();
        await using var factory = new ApiFactory { ScanCode = scan };
        using var client = factory.CreateClient();
        var token = await ApiFactory.FetchAccessTokenAsync(client, TestContext.Current.CancellationToken);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/trace-codes/submit")
        {
            Content = new StringContent(
                """
                {
                  "drugId": "d1",
                  "spec": "s1",
                  "validUniqueCodes": ["T1"],
                  "analysis": { "total": 1, "invalid": 0, "duplicate": 0, "validUniqueCodes": ["T1"] },
                  "clientRaw": "c1",
                  "source": "manual"
                }
                """,
                Encoding.UTF8,
                "application/json"),
        };
        request.Headers.TryAddWithoutValidation(PacApiHeaders.CommandId, Guid.NewGuid().ToString("D"));
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, scan.SubmitCalls);
    }

    [Fact]
    public async Task Submit_propagates_service_failure()
    {
        var scan = new FakeScanCode { ThrowOnSubmit = true };
        await using var factory = new ApiFactory { ScanCode = scan };
        using var client = factory.CreateClient();
        var token = await ApiFactory.FetchAccessTokenAsync(client, TestContext.Current.CancellationToken);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/trace-codes/submit")
        {
            Content = new StringContent(
                """
                {
                  "drugId": "d1",
                  "spec": "s1",
                  "validUniqueCodes": ["T1"],
                  "analysis": { "total": 1, "invalid": 0, "duplicate": 0, "validUniqueCodes": ["T1"] },
                  "clientRaw": "c1",
                  "source": "manual"
                }
                """,
                Encoding.UTF8,
                "application/json"),
        };
        request.Headers.TryAddWithoutValidation(PacApiHeaders.CommandId, Guid.NewGuid().ToString("D"));
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    [Fact]
    public async Task Submit_rejects_null_analysis()
    {
        await using var factory = new ApiFactory { ScanCode = new FakeScanCode() };
        using var client = factory.CreateClient();
        var token = await ApiFactory.FetchAccessTokenAsync(client, TestContext.Current.CancellationToken);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/trace-codes/submit")
        {
            Content = new StringContent(
                """
                {
                  "drugId": "d1",
                  "spec": "s1",
                  "validUniqueCodes": ["T1"],
                  "analysis": null,
                  "clientRaw": "c1",
                  "source": "manual"
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
    public async Task Submit_maps_argument_exception_to_400()
    {
        await using var factory = new ApiFactory
        {
            ScanCode = new FakeScanCode { ThrowArgumentOnSubmit = true },
        };
        using var client = factory.CreateClient();
        var token = await ApiFactory.FetchAccessTokenAsync(client, TestContext.Current.CancellationToken);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/trace-codes/submit")
        {
            Content = new StringContent(
                """
                {
                  "drugId": "d1",
                  "spec": "s1",
                  "validUniqueCodes": ["T1"],
                  "analysis": { "total": 1, "invalid": 0, "duplicate": 0, "validUniqueCodes": ["T1"] },
                  "clientRaw": "c1",
                  "source": "manual"
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
    public async Task Submit_rejects_null_valid_unique_codes()
    {
        await using var factory = new ApiFactory { ScanCode = new FakeScanCode() };
        using var client = factory.CreateClient();
        var token = await ApiFactory.FetchAccessTokenAsync(client, TestContext.Current.CancellationToken);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/trace-codes/submit")
        {
            Content = new StringContent(
                """
                {
                  "drugId": "d1",
                  "spec": "s1",
                  "validUniqueCodes": null,
                  "analysis": { "total": 0, "invalid": 0, "duplicate": 0, "validUniqueCodes": [] },
                  "clientRaw": "c1",
                  "source": "manual"
                }
                """,
                Encoding.UTF8,
                "application/json"),
        };
        request.Headers.TryAddWithoutValidation(PacApiHeaders.CommandId, Guid.NewGuid().ToString("D"));
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private sealed class FakeScanCode : IScanCodeService
    {
        public int SubmitCalls { get; private set; }

        public bool ThrowOnSubmit { get; init; }

        public bool ThrowArgumentOnSubmit { get; init; }

        public Task<ScanCodeSubmitResult> SubmitAsync(ScanCodeSubmitRequest request, CancellationToken ct)
        {
            SubmitCalls++;
            if (ThrowArgumentOnSubmit)
            {
                throw new ArgumentException("Analysis.Total must equal Invalid + Duplicate + ValidUniqueCodes.Count");
            }

            if (ThrowOnSubmit)
            {
                throw new InvalidOperationException("log failed");
            }

            return Task.FromResult(new ScanCodeSubmitResult(
                DrugFound: true,
                Insert: new ScanCodeInsertResult(1, 1, 0),
                QtyPerTrace: 1,
                EntryResult: "success",
                EntryMessage: "ok"));
        }

        public Task<IReadOnlyList<string>> FindExistingTraceCodesAsync(
            IReadOnlyList<string> traceCodes,
            CancellationToken ct)
            => Task.FromResult<IReadOnlyList<string>>([]);
    }
}
