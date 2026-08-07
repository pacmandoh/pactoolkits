using System.Buffers;
using System.Text;
using System.Text.Json;

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

    public static async Task<AgentsIpcMessage?> ReadAsync(Stream stream, CancellationToken ct)
    {
        var buffer = new ArrayBufferWriter<byte>(512);
        var one = new byte[1];
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var read = await stream.ReadAsync(one.AsMemory(0, 1), ct).ConfigureAwait(false);
            if (read == 0)
            {
                return null;
            }

            if (one[0] == (byte)'\n')
            {
                break;
            }

            if (one[0] == (byte)'\r')
            {
                continue;
            }

            buffer.Write(one);
            if (buffer.WrittenCount > 1_000_000)
            {
                throw new InvalidOperationException("IPC frame too large");
            }
        }

        if (buffer.WrittenCount == 0)
        {
            return null;
        }

        var line = Encoding.UTF8.GetString(buffer.WrittenSpan);
        return AgentsIpc.TryDeserialize(line);
    }

    public static AgentsStatus? CloneStatus(AgentsStatus? source)
    {
        if (source is null)
        {
            return null;
        }

        var json = JsonSerializer.Serialize(source, AgentsJson.Options);
        return JsonSerializer.Deserialize<AgentsStatus>(json, AgentsJson.Options);
    }
}
