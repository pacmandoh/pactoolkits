using System;
using System.Collections.Generic;
using System.Threading;

namespace PacToolkits.Desktop.Avalonia.Common;

/// <summary>为相关日志事件生成和传播稳定的 Trace 标识</summary>
public static class LogTrace
{
    private static readonly AsyncLocal<Stack<string>?> Traces = new();

    public static string? Current
    {
        get
        {
            var stack = Traces.Value;
            return stack is { Count: > 0 } ? stack.Peek() : null;
        }
    }

    public static IDisposable Begin(string? traceId = null)
    {
        traceId ??= CreateId();
        var stack = Traces.Value ?? new Stack<string>();
        Traces.Value = stack;
        stack.Push(traceId);
        return new Scope(stack);
    }

    internal static string CreateId()
        => Guid.NewGuid().ToString("N")[..12];

    private sealed class Scope(Stack<string> stack) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (stack.Count > 0)
            {
                stack.Pop();
            }
        }
    }
}
