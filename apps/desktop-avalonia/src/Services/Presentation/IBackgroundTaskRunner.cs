using System;
using System.Threading;
using System.Threading.Tasks;

namespace PacToolkits.Desktop.Avalonia.Services.Presentation;

/// <summary>后台 Task 运行器入口</summary>
public interface IBackgroundTaskRunner
{
    /// <summary>
    /// 在调用方同步上下文上启动 fire-and-forget Task
    /// 异常记日志；取消静默忽略
    /// </summary>
    void RunDetached(
        Func<CancellationToken, Task> work,
        string module,
        string eventName,
        CancellationToken ct = default,
        bool toastOnError = false);
}
