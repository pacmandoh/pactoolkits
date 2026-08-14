using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using PacToolkits.Application.Abstractions;

namespace PacToolkits.Api.Tests;

public sealed class CatalogEndpointsTests
{
    public static TheoryData<string> CatalogRoutes =>
    [
        "/v1/catalog/drug-ids",
        "/v1/catalog/client-ids",
        "/v1/catalog/drugs/d1/specs",
        "/v1/catalog/drugs/d1/s1/quantity",
        "/v1/catalog/drugs/d1/deprecated",
    ];

    [Theory]
    [MemberData(nameof(CatalogRoutes))]
    public async Task Catalog_routes_reject_anonymous(string path)
    {
        await using var factory = new ApiFactory { Lookup = new FakeLookup() };
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(path, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(CatalogRoutes))]
    public async Task Catalog_routes_accept_bearer(string path)
    {
        await using var factory = new ApiFactory { Lookup = new FakeLookup() };
        using var client = factory.CreateClient();
        var token = await ApiFactory.FetchAccessTokenAsync(client, TestContext.Current.CancellationToken);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await client.GetAsync(path, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Drug_ids_return_items()
    {
        await using var factory = new ApiFactory { Lookup = new FakeLookup() };
        using var client = factory.CreateClient();
        var token = await ApiFactory.FetchAccessTokenAsync(client, TestContext.Current.CancellationToken);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await client.GetAsync("/v1/catalog/drug-ids", TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(body);
        Assert.Equal("d1", doc.RootElement.GetProperty("items")[0].GetString());
    }

    private sealed class FakeLookup : ILookupCatalogService
    {
        public Task<IReadOnlyList<string>> GetClientIdsAsync(CancellationToken ct, bool forceRefresh = false)
            => Task.FromResult<IReadOnlyList<string>>(["m1"]);

        public Task<IReadOnlyList<string>> GetDrugIdsAsync(CancellationToken ct, bool forceRefresh = false)
            => Task.FromResult<IReadOnlyList<string>>(["d1"]);

        public Task<IReadOnlyList<string>> GetSpecsByDrugAsync(
            string drugId,
            CancellationToken ct,
            bool forceRefresh = false)
            => Task.FromResult<IReadOnlyList<string>>(["s1"]);

        public Task<string?> ResolveCanonicalDrugIdAsync(
            string? input,
            CancellationToken ct,
            bool forceRefresh = false)
            => Task.FromResult<string?>(input);

        public Task<int?> GetQtyAsync(
            string? drugId,
            string? spec,
            CancellationToken ct,
            bool forceRefresh = false)
            => Task.FromResult<int?>(12);

        public Task<bool> IsDeprecatedDrugIdAsync(
            string? drugId,
            CancellationToken ct,
            bool forceRefresh = false)
            => Task.FromResult(false);

        public void InvalidateDrugCatalog()
        {
        }
    }
}
