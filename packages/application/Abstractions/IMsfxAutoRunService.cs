using PacToolkits.Application.DTOs;

namespace PacToolkits.Application.Abstractions;

public interface IMsfxAutoRunObserver
{
    bool IsManualWriteActive { get; }
    void Report(MsfxAutoRunUpdate update);
    Task DataChangedAsync(MsfxAutoRunData data, CancellationToken ct);
    Task<bool> AuthorizeMappingAsync(long batchId, CancellationToken ct);
}

public interface IMsfxAutoRunService
{
    Task<MsfxAutoRunResult> RunAsync(
        MsfxAutoRunRequest request,
        IMsfxAutoRunObserver observer,
        CancellationToken ct);
}
