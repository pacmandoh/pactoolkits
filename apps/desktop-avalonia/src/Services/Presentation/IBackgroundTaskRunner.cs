using System;
using System.Threading;
using System.Threading.Tasks;

namespace PacToolkits.Desktop.Avalonia.Services.Presentation;

/// <summary>定义可记录异常的后台任务调度契约</summary>
public interface IBackgroundTaskRunner
{
    /// <summary>
    /// 在调用方同步上下文中启动无需等待的任务
    /// 记录执行异常，正常取消不作为错误报告
    /// </summary>
    void RunDetached(
        Func<CancellationToken, Task> work,
        string module,
        string eventName,
        CancellationToken ct = default,
        bool toastOnError = false);
}
