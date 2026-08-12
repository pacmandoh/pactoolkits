using PacToolkits.Application.DTOs;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure.Api;

namespace PacToolkits.Desktop.Tests;

public sealed class ApiAvailabilityClassifyTests
{
    [Fact]
    public void Schema_incompatible_with_unavailable_status_is_schema_blocked()
    {
        var status = new PacApiSystemStatus(
            Status: "unavailable",
            Utc: DateTimeOffset.UtcNow,
            Database: "ok",
            Schema: "incompatible",
            SchemaVersion: "1.0.0",
            Reason: "schema boom");

        Assert.Equal(ApiAvailabilityState.SchemaBlocked, ApiAvailabilityService.ClassifyStatus(status));
    }

    [Fact]
    public void Schema_unavailable_with_database_ok_is_server_database_blocked()
    {
        var status = new PacApiSystemStatus(
            Status: "unavailable",
            Utc: DateTimeOffset.UtcNow,
            Database: "ok",
            Schema: "unavailable",
            SchemaVersion: null,
            Reason: "read failed");

        Assert.Equal(
            ApiAvailabilityState.ServerDatabaseBlocked,
            ApiAvailabilityService.ClassifyStatus(status));
    }

    [Fact]
    public void Database_unavailable_with_schema_skipped_is_server_database_blocked()
    {
        var status = new PacApiSystemStatus(
            Status: "unavailable",
            Utc: DateTimeOffset.UtcNow,
            Database: "unavailable",
            Schema: "skipped",
            SchemaVersion: null,
            Reason: "database_unavailable");

        Assert.Equal(
            ApiAvailabilityState.ServerDatabaseBlocked,
            ApiAvailabilityService.ClassifyStatus(status));
    }

    [Fact]
    public void Metadata_missing_is_schema_blocked()
    {
        var status = new PacApiSystemStatus(
            Status: "unavailable",
            Utc: DateTimeOffset.UtcNow,
            Database: "ok",
            Schema: "metadata_missing",
            SchemaVersion: null,
            Reason: "schema_metadata_missing");

        Assert.Equal(ApiAvailabilityState.SchemaBlocked, ApiAvailabilityService.ClassifyStatus(status));
    }

    [Fact]
    public void All_ok_is_ready()
    {
        var status = new PacApiSystemStatus(
            Status: "ok",
            Utc: DateTimeOffset.UtcNow,
            Database: "ok",
            Schema: "ok",
            SchemaVersion: "1.0.0",
            Reason: null);

        Assert.Equal(ApiAvailabilityState.Ready, ApiAvailabilityService.ClassifyStatus(status));
    }

    [Theory]
    [InlineData(401, "unauthorized", true)]
    [InlineData(403, "forbidden", true)]
    [InlineData(429, "rate_limited", false)]
    [InlineData(0, "transport", false)]
    [InlineData(408, "timeout", false)]
    public void DescribeFailure_classifies_credential_vs_other(int status, string code, bool credentialRejected)
    {
        var ex = new PacApiException(
            new PacApiProblem(
                Status: status,
                Code: code,
                Title: "x",
                Detail: null,
                TraceId: "t",
                RetryAfter: null));

        Assert.False(string.IsNullOrWhiteSpace(ApiAvailabilityService.DescribeFailure(ex)));
        Assert.Equal(credentialRejected, ApiAvailabilityService.IsCredentialRejected(ex));
    }

    [Fact]
    public void DescribeUserFacing_falls_back_when_unclassified()
    {
        Assert.False(string.IsNullOrWhiteSpace(
            ApiAvailabilityService.DescribeUserFacing(new InvalidOperationException("boom"))));
    }
}
