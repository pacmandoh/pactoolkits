namespace PacToolkits.Application.DTOs;

public sealed record TraceCodesExistingRequest(IReadOnlyList<string> TraceCodes);

public sealed record TraceCodesExistingResponse(IReadOnlyList<string> Existing);
