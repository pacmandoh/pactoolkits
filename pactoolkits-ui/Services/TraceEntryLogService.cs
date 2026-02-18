using System;
using System.Threading;
using System.Threading.Tasks;
using pactoolkits_ui.Contracts;
using pactoolkits_ui.DataAccess;
using Microsoft.Extensions.Options;

namespace pactoolkits_ui.Services;

public interface ITraceEntryLogService
{
    Task WriteAsync(TraceEntryLogDto dto, CancellationToken ct = default);
}

public sealed class TraceEntryLogService : ITraceEntryLogService
{
    private readonly IDb _db;
    private readonly PgOptions _opt;

    public TraceEntryLogService(IDb db, IOptions<PgOptions> opt)
    {
        _db = db;
        _opt = opt.Value;
    }

    public async Task WriteAsync(TraceEntryLogDto dto, CancellationToken ct = default)
    {
        await _db.WithConnection(async (conn, token) =>
        {
            const string sql = """
                insert into trace_entry_log (
                  entry_at,
                  drug_id,
                  spec,
                  entry_count,
                  qty_per_trace,
                  total_available_qty,
                  failed_count,
                  result,
                  txn_id,
                  client_id,
                  source,
                  message
                )
                values (
                  @entry_at,
                  @drug_id,
                  @spec,
                  @entry_count,
                  @qty_per_trace,
                  @total_available_qty,
                  @failed_count,
                  @result,
                  @txn_id,
                  @client_id,
                  @source,
                  @message
                )
            """;

            await using var cmd = conn.CreateCommand(sql, _opt.CommandTimeoutSeconds);
            cmd.AddParam("entry_at", dto.EntryAt.UtcDateTime);
            cmd.AddParam("drug_id", dto.DrugId);
            cmd.AddParam("spec", dto.Spec);
            cmd.AddParam("entry_count", Math.Max(0, dto.EntryCount));
            cmd.AddParam("qty_per_trace", Math.Max(0, dto.QtyPerTrace));
            cmd.AddParam("total_available_qty", Math.Max(0, dto.TotalAvailableQty));
            cmd.AddParam("failed_count", Math.Max(0, dto.FailedCount));
            cmd.AddParam("result", NormalizeToken(dto.Result));
            cmd.AddParam("client_id", SanitizeLine(dto.Client));
            cmd.AddParam("source", NormalizeToken(dto.Source));
            cmd.Parameters.AddWithValue("txn_id", dto.TxnId.HasValue ? dto.TxnId.Value : (object)DBNull.Value);
            cmd.Parameters.AddWithValue("message", string.IsNullOrWhiteSpace(dto.Message)
                ? DBNull.Value
                : SanitizeLine(dto.Message));

            await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);
    }

    private static string NormalizeToken(string? s)
        => string.IsNullOrWhiteSpace(s) ? string.Empty : s.Trim().ToLowerInvariant();

    private static string SanitizeLine(string? s)
        => (s ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();
}
