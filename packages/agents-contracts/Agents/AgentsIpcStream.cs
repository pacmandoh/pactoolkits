using System.Buffers;
using System.Text;

namespace PacToolkits.Agents.Contracts.Agents;

/// <summary>
/// 管道上的换行分隔 JSON 读/写
/// </summary>
public static class AgentsIpcStream
{
    private static readonly byte[] NewLine = [(byte)'\n'];

    public static async Task WriteAsync(Stream stream, AgentsIpcMessage message, CancellationToken ct)
    {
        var json = AgentsIpc.Serialize(message);
        var bytes = Encoding.UTF8.GetBytes(json);
        await stream.WriteAsync(bytes, ct).ConfigureAwait(false);
        await stream.WriteAsync(NewLine, ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);
    }

    public static async Task<AgentsIpcMessage?> ReadAsync(
        Stream stream,
        AgentsIpcReadBuffer buffer,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(buffer);

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            if (buffer.TryTakeLine(out var line))
            {
                return line.Length == 0 ? null : AgentsIpc.TryDeserialize(line);
            }

            var memory = buffer.Chunk.AsMemory();
            var read = await stream.ReadAsync(memory, ct).ConfigureAwait(false);
            if (read == 0)
            {
                return null;
            }

            buffer.Append(memory.Span[..read]);
        }
    }
}

/// <summary>维护跨次读取的 IPC 帧边界</summary>
public sealed class AgentsIpcReadBuffer
{
    internal readonly byte[] Chunk = new byte[4096];
    private readonly ArrayBufferWriter<byte> _pending = new(512);

    internal void Append(ReadOnlySpan<byte> bytes)
    {
        if (_pending.WrittenCount + bytes.Length > 1_000_000)
        {
            throw new InvalidOperationException("IPC frame too large");
        }

        _pending.Write(bytes);
    }

    internal bool TryTakeLine(out string line)
    {
        var span = _pending.WrittenSpan;
        var newline = span.IndexOf((byte)'\n');
        if (newline < 0)
        {
            line = string.Empty;
            return false;
        }

        var payload = span[..newline];
        if (payload.Length > 0 && payload[^1] == (byte)'\r')
        {
            payload = payload[..^1];
        }

        line = Encoding.UTF8.GetString(payload);
        var restStart = newline + 1;
        var restLen = span.Length - restStart;
        byte[]? rest = restLen > 0 ? span[restStart..].ToArray() : null;
        _pending.Clear();
        if (rest is { Length: > 0 })
        {
            _pending.Write(rest);
        }

        return true;
    }
}
