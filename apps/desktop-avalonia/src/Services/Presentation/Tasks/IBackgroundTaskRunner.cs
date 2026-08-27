using System;
using System.Threading;
using System.Threading.Tasks;

namespace PacToolkits.Desktop.Avalonia.Services.Presentation.Tasks;

/// <summary>后台不必等待的任务；异常记日志</summary>
public interface IBackgroundTaskRunner
{
    /// <summary>
    /// 在调用方同步上下文中启动不必等待的任务
    /// 异常记日志；正常取消不作为错误
    /// </summary>
    void RunDetached(
        Func<CancellationToken, Task> work,
        string module,
        string eventName,
        CancellationToken ct = default,
        bool toastOnError = false);
}
