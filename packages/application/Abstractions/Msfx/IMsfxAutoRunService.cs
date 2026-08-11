using PacToolkits.Application.DTOs;

namespace PacToolkits.Application.Abstractions;

/// <summary>
/// 接收 MSFX 自动任务的进度和数据变更通知
/// </summary>
public interface IMsfxAutoRunObserver
{
    bool IsManualWriteActive { get; }
    void Report(MsfxAutoRunUpdate update);
    Task DataChangedAsync(MsfxAutoRunData data, CancellationToken ct);
}

/// <summary>
/// 编排 MSFX 单据拉取、映射和任务生成，并支持恢复中断批次
/// </summary>
public interface IMsfxAutoRunService
{
    Task<int> RecoverInterruptedAsync(CancellationToken ct);

    Task<MsfxAutoRunResult> RunAsync(
        MsfxAutoRunRequest request,
        IMsfxAutoRunObserver observer,
        CancellationToken ct);
}
