using PacToolkits.Application.DTOs;

namespace PacToolkits.Application.Abstractions;

/// <summary>
/// 自动跑批进度观察者（进度上报与数据变更回调）
/// </summary>
public interface IMsfxAutoRunObserver
{
    bool IsManualWriteActive { get; }
    void Report(MsfxAutoRunUpdate update);
    Task DataChangedAsync(MsfxAutoRunData data, CancellationToken ct);
}

/// <summary>
/// MSFX 自动跑批编排（拉单、映射、任务生成）；可恢复中断批次
/// </summary>
public interface IMsfxAutoRunService
{
    Task<int> RecoverInterruptedAsync(CancellationToken ct);

    Task<MsfxAutoRunResult> RunAsync(
        MsfxAutoRunRequest request,
        IMsfxAutoRunObserver observer,
        CancellationToken ct);
}
