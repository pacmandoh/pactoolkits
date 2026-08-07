using System;
using System.IO;
using PacToolkits.Agents.Contracts.Agents;

namespace PacToolkits.Desktop.Avalonia.Services.Infrastructure.Agents;

/// <summary>
/// 仅监视 Host 入口热更（模块热更在 Host）；双次稳定采样
/// </summary>
internal sealed class AgentsBinaryMonitor
{
    private string? _path;
    private readonly AgentsBinaryChange _change = new();

    public void Sync(string? executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            _path = null;
            return;
        }

        var normalized = Path.GetFullPath(executablePath);
        if (string.Equals(_path, normalized, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _path = normalized;
        _change.Observe(AgentsBinaryStampIO.TryRead(normalized), active: false, out _);
    }

    public bool Detect(AgentsRunState hostState, out AgentsBinaryStamp changed)
    {
        changed = default;
        if (_path is null)
        {
            return false;
        }

        // 热更仅在 Host 进程活跃窗口采样；与 IsActive 一致
        return _change.Observe(
            AgentsBinaryStampIO.TryRead(_path),
            hostState.IsActive(),
            out changed);
    }

    public void Accept(AgentsBinaryStamp stamp)
        => _change.Accept(stamp);
}
