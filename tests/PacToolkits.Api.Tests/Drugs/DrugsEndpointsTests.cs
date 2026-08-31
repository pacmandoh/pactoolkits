using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;

namespace PacToolkits.Api.Tests;

public sealed class DrugsEndpointsTests
{
    [Fact]
    public async Task Search_rejects_anonymous()
    {
        await using var factory = new ApiFactory { Drugs = new FakeDrugs() };
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/v1/drugs", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Search_returns_items()
    {
        await using var factory = new ApiFactory { Drugs = new FakeDrugs() };
        using var client = factory.CreateClient();
        var token = await ApiFactory.FetchAccessTokenAsync(client, TestContext.Current.CancellationToken);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await client.GetAsync("/v1/drugs?limit=10", TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(body);
        Assert.Equal(1, doc.RootElement.GetProperty("totalCount").GetInt32());
        Assert.Equal("d1", doc.RootElement.GetProperty("items")[0].GetProperty("drugId").GetString());
    }

    [Fact]
    public async Task Save_requires_command_id()
    {
        await using var factory = new ApiFactory { Drugs = new FakeDrugs() };
        using var client = factory.CreateClient();
        var token = await ApiFactory.FetchAccessTokenAsync(client, TestContext.Current.CancellationToken);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var request = new HttpRequestMessage(HttpMethod.Put, "/v1/drugs/key?drugId=d1&spec=s1")
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json"),
        };
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Save_returns_concurrency_as_409()
    {
        var drugs = new FakeDrugs { NextOutcome = DrugSaveOutcome.ConcurrencyConflict };
        await using var factory = new ApiFactory { Drugs = drugs };
        using var client = factory.CreateClient();
        var token = await ApiFactory.FetchAccessTokenAsync(client, TestContext.Current.CancellationToken);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var request = new HttpRequestMessage(HttpMethod.Put, "/v1/drugs/key?drugId=d1&spec=s1")
        {
            Content = new StringContent(
                """
                {
                  "dto": {
                    "drugId": "d1",
                    "spec": "s1",
                    "qty": 1,
                    "ruleKey": null,
                    "preTc": null,
                    "note": null,
                    "createdAt": "2024-01-01T00:00:00Z",
                    "updatedAt": null,
                    "version": 1
                  },
                  "originDrugId": "d1",
                  "originSpec": "s1",
                  "expectedVersion": 1,
                  "isNew": false,
                  "hasPrimaryKeyChanges": false,
                  "hasQtyChanged": false
                }
                """,
                Encoding.UTF8,
                "application/json"),
        };
        request.Headers.TryAddWithoutValidation(PacApiHeaders.CommandId, Guid.NewGuid().ToString("D"));
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var doc = JsonDocument.Parse(body);
        Assert.Equal(1, doc.RootElement.GetProperty("currentVersion").GetInt64());
    }

    [Fact]
    public async Task Save_replays_completed_command()
    {
        var drugs = new FakeDrugs();
        await using var factory = new ApiFactory { Drugs = drugs };
        using var client = factory.CreateClient();
        var token = await ApiFactory.FetchAccessTokenAsync(client, TestContext.Current.CancellationToken);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var commandId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var json = """
            {
              "dto": {
                "drugId": "d1",
                "spec": "s1",
                "qty": 1,
                "ruleKey": null,
                "preTc": null,
                "note": null,
                "createdAt": "2024-01-01T00:00:00Z",
                "updatedAt": null,
                "version": 0
              },
              "originDrugId": null,
              "originSpec": null,
              "expectedVersion": null,
              "isNew": true,
              "hasPrimaryKeyChanges": false,
              "hasQtyChanged": false
            }
            """;

        async Task<HttpResponseMessage> SendOnce()
        {
            using var request = new HttpRequestMessage(HttpMethod.Put, "/v1/drugs/key?drugId=d1&spec=s1")
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
        Assert.Equal(1, drugs.SaveCalls);
    }

    [Fact]
    public async Task KeyFixCommit_rejects_null_source_or_target()
    {
        await using var factory = new ApiFactory { Drugs = new FakeDrugs() };
        using var client = factory.CreateClient();
        var token = await ApiFactory.FetchAccessTokenAsync(client, TestContext.Current.CancellationToken);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/drugs/key-fix/commit")
        {
            Content = new StringContent(
                """
                {
                  "source": null,
                  "target": {
                    "drugId": "d2",
                    "spec": "s2",
                    "qty": 1,
                    "ruleKey": null,
                    "preTc": null,
                    "note": null,
                    "createdAt": "2024-01-01T00:00:00Z",
                    "updatedAt": null,
                    "version": 1
                  },
                  "reason": "fix",
                  "operatorName": "op",
                  "sourceTag": "ui"
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
    public async Task GetByKey_keeps_slash_in_drug_and_spec()
    {
        var drugs = new FakeDrugs();
        await using var factory = new ApiFactory { Drugs = drugs };
        using var client = factory.CreateClient();
        var token = await ApiFactory.FetchAccessTokenAsync(client, TestContext.Current.CancellationToken);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var drug = "氨/西林";
        var spec = "10ml/盒";
        using var response = await client.GetAsync(
            "/v1/drugs/key?drugId=" + Uri.EscapeDataString(drug) + "&spec=" + Uri.EscapeDataString(spec),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(drug, drugs.LastGetDrugId);
        Assert.Equal(spec, drugs.LastGetSpec);
    }

    [Fact]
    public async Task Delete_referenced_row_returns_409()
    {
        var drugs = new FakeDrugs { DeleteError = new DrugIndexInUseException() };
        await using var factory = new ApiFactory { Drugs = drugs };
        using var client = factory.CreateClient();
        var token = await ApiFactory.FetchAccessTokenAsync(client, TestContext.Current.CancellationToken);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var request = new HttpRequestMessage(HttpMethod.Delete, "/v1/drugs/key?drugId=d1&spec=s1&expectedVersion=1");
        request.Headers.TryAddWithoutValidation(PacApiHeaders.CommandId, Guid.NewGuid().ToString("D"));
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var doc = JsonDocument.Parse(body);
        Assert.Equal("conflict", doc.RootElement.GetProperty("code").GetString());
        Assert.Equal("Drug spec is still referenced", doc.RootElement.GetProperty("detail").GetString());
        Assert.False(doc.RootElement.TryGetProperty("currentVersion", out _));
    }

    private sealed class FakeDrugs : IDrugIndexService
    {
        public int SaveCalls { get; private set; }

        public string? LastGetDrugId { get; private set; }

        public string? LastGetSpec { get; private set; }

        public DrugSaveOutcome NextOutcome { get; set; } = DrugSaveOutcome.Saved;

        public Task<DrugIndexSearchResult> SearchAsync(string? keyword, int limit, CancellationToken ct)
        {
            var row = SampleRow();
            return Task.FromResult(new DrugIndexSearchResult([row], 1));
        }

        public Task<DrugIndexDto?> GetByKeyAsync(string drugId, string spec, CancellationToken ct)
        {
            LastGetDrugId = drugId;
            LastGetSpec = spec;
            return Task.FromResult<DrugIndexDto?>(SampleRow());
        }

        public Task<DrugIndexSaveResult> SaveAsync(DrugIndexSaveRequest request, CancellationToken ct)
        {
            SaveCalls++;
            var row = NextOutcome == DrugSaveOutcome.ConcurrencyConflict ? SampleRow() : request.Dto;
            return Task.FromResult(new DrugIndexSaveResult(NextOutcome, row, null));
        }

        public Exception? DeleteError { get; set; }

        public Task DeleteAsync(string drugId, string spec, long expectedVersion, CancellationToken ct)
            => DeleteError is null ? Task.CompletedTask : Task.FromException(DeleteError);

        public Task<DrugKeyFixPreviewDto> PreviewKeyFixAsync(
            string sourceDrugId,
            string sourceSpec,
            string targetDrugId,
            string targetSpec,
            CancellationToken ct)
            => Task.FromResult(new DrugKeyFixPreviewDto(true, false, 0, 0, 0));

        public Task<DrugKeyFixCommitResult> ApplyKeyFixAsync(DrugKeyFixRequest request, CancellationToken ct)
            => throw new NotSupportedException();

        private static DrugIndexDto SampleRow()
            => new(
                DrugId: "d1",
                Spec: "s1",
                Qty: 1,
                RuleKey: null,
                PreTc: null,
                Note: null,
                CreatedAt: DateTimeOffset.Parse("2024-01-01T00:00:00Z"),
                UpdatedAt: null,
                Version: 1);
    }
}
