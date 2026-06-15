namespace PacToolkits.Application.DTOs;

public sealed record DbSchemaVersionReadResult(
    bool Ok,
    string? Value,
    string? Reason,
    bool IsMetadataMissing = false);
