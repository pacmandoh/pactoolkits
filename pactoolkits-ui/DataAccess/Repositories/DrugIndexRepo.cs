using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using pactoolkits_ui.Contracts;
using pactoolkits_ui.DataAccess;

namespace pactoolkits_ui.Repositories;

public sealed class DrugIndexRepo : IDrugIndexRepo
{
    private readonly IDb _db;
    private readonly PgOptions _opt;

    public DrugIndexRepo(IDb db, IOptions<PgOptions> opt)
    {
        _db = db;
        _opt = opt.Value;
    }

    public Task<IReadOnlyList<DrugIndexDto>> SearchAsync(string? keyword, int limit, CancellationToken ct)
        => _db.WithConnection(async (conn, token) =>
        {
            const string sql = """
                select drug_id, spec, qty, rule_key, pre_tc, note, created_at, updated_at, version
                from drug_index
                where
                  @kw = ''
                  or drug_id ilike ('%' || @kw || '%')
                  or spec    ilike ('%' || @kw || '%')
                  or coalesce(rule_key,'') ilike ('%' || @kw || '%')
                  or coalesce(pre_tc,'')   ilike ('%' || @kw || '%')
                  or coalesce(note,'')     ilike ('%' || @kw || '%')
                order by drug_id asc, spec asc
                limit @n
            """;

            await using var cmd = conn.CreateCommand(sql, _opt.CommandTimeoutSeconds);
            var kw = (keyword ?? string.Empty).Trim();
            cmd.AddParam("kw", kw);
            cmd.AddParam("n", Math.Clamp(limit, 1, 2000));

            var list = new List<DrugIndexDto>();
            await using var reader = await cmd.ExecuteReaderAsync(token);
            while (await reader.ReadAsync(token))
            {
                list.Add(ReadDrugIndexDto(reader));
            }

            return (IReadOnlyList<DrugIndexDto>)list;
        }, ct);

    public Task<DrugIndexDto?> GetByKeyAsync(string drugId, string spec, CancellationToken ct)
        => _db.WithConnection(async (conn, token) =>
        {
            const string sql = """
                select drug_id, spec, qty, rule_key, pre_tc, note, created_at, updated_at, version
                from drug_index
                where drug_id = @drug_id and spec = @spec
                limit 1
            """;

            await using var cmd = conn.CreateCommand(sql, _opt.CommandTimeoutSeconds);
            cmd.AddParam("drug_id", drugId);
            cmd.AddParam("spec", spec);

            await using var reader = await cmd.ExecuteReaderAsync(token);
            if (!await reader.ReadAsync(token)) return null;

            return ReadDrugIndexDto(reader);
        }, ct);

    public Task<bool> ExistsAsync(string drugId, string spec, CancellationToken ct)
        => _db.WithConnection(async (conn, token) =>
        {
            const string sql = """
                select 1
                from drug_index
                where drug_id = @drug_id and spec = @spec
                limit 1
            """;
            await using var cmd = conn.CreateCommand(sql, _opt.CommandTimeoutSeconds);
            cmd.AddParam("drug_id", drugId);
            cmd.AddParam("spec", spec);

            var obj = await cmd.ExecuteScalarAsync(token);
            return obj is not null;
        }, ct);

    public Task<DrugIndexDto> UpsertAsync(DrugIndexDto dto, long? expectedVersion, CancellationToken ct)
        => _db.WithConnection(async (conn, token) =>
        {
            if (expectedVersion is null)
            {
                const string insertSql = """
                    insert into drug_index(drug_id, spec, qty, rule_key, pre_tc, note, version)
                    values (@drug_id, @spec, @qty, @rule_key, @pre_tc, @note, 0)
                    on conflict (drug_id, spec) do nothing
                    returning drug_id, spec, qty, rule_key, pre_tc, note, created_at, updated_at, version
                """;

                await using var cmd = conn.CreateCommand(insertSql, _opt.CommandTimeoutSeconds);
                cmd.AddParam("drug_id", dto.DrugId);
                cmd.AddParam("spec", dto.Spec);
                cmd.AddParam("qty", dto.Qty);
                cmd.AddParam("rule_key", (object?)dto.RuleKey ?? DBNull.Value);
                cmd.AddParam("pre_tc", (object?)dto.PreTc ?? DBNull.Value);
                cmd.AddParam("note", (object?)dto.Note ?? DBNull.Value);

                await using var reader = await cmd.ExecuteReaderAsync(token);
                if (await reader.ReadAsync(token))
                    return ReadDrugIndexDto(reader);

                var current = await GetByKeyInternalAsync(conn, dto.DrugId, dto.Spec, token);
                throw new DrugIndexConcurrencyException("该记录已被其他终端创建，请刷新后重试。", current);
            }

            const string updateSql = """
                update drug_index
                set
                  qty = @qty,
                  rule_key = @rule_key,
                  pre_tc = @pre_tc,
                  note = @note,
                  version = version + 1
                where drug_id = @drug_id
                  and spec = @spec
                  and version = @expected_version
                returning drug_id, spec, qty, rule_key, pre_tc, note, created_at, updated_at, version
            """;

            await using var update = conn.CreateCommand(updateSql, _opt.CommandTimeoutSeconds);
            update.AddParam("drug_id", dto.DrugId);
            update.AddParam("spec", dto.Spec);
            update.AddParam("qty", dto.Qty);
            update.AddParam("rule_key", (object?)dto.RuleKey ?? DBNull.Value);
            update.AddParam("pre_tc", (object?)dto.PreTc ?? DBNull.Value);
            update.AddParam("note", (object?)dto.Note ?? DBNull.Value);
            update.AddParam("expected_version", expectedVersion.Value);

            await using var updated = await update.ExecuteReaderAsync(token);
            if (await updated.ReadAsync(token))
                return ReadDrugIndexDto(updated);

            var latest = await GetByKeyInternalAsync(conn, dto.DrugId, dto.Spec, token);
            throw new DrugIndexConcurrencyException("该记录已被其他终端修改，请刷新后重试。", latest);
        }, ct);

    public Task DeleteAsync(string drugId, string spec, CancellationToken ct)
        => _db.WithConnection(async (conn, token) =>
        {
            const string sql = """
                delete from drug_index
                where drug_id = @drug_id and spec = @spec
            """;

            await using var cmd = conn.CreateCommand(sql, _opt.CommandTimeoutSeconds);
            cmd.AddParam("drug_id", drugId);
            cmd.AddParam("spec", spec);

            await cmd.ExecuteNonQueryAsync(token);
        }, ct);

    private static DrugIndexDto ReadDrugIndexDto(IDataRecord reader)
        => new(
            DrugId: reader.GetString(0),
            Spec: reader.GetString(1),
            Qty: reader.GetInt32(2),
            RuleKey: reader.IsDBNull(3) ? null : reader.GetString(3),
            PreTc: reader.IsDBNull(4) ? null : reader.GetString(4),
            Note: reader.IsDBNull(5) ? null : reader.GetString(5),
            CreatedAt: ReadDateTimeOffset(reader.GetValue(6)),
            UpdatedAt: reader.IsDBNull(7) ? null : ReadDateTimeOffset(reader.GetValue(7)),
            Version: reader.GetInt64(8)
        );

    private static DateTimeOffset ReadDateTimeOffset(object value)
    {
        return value switch
        {
            DateTimeOffset dto when dto.Offset == TimeSpan.Zero
                => new DateTimeOffset(DateTime.SpecifyKind(dto.DateTime, DateTimeKind.Local)),
            DateTimeOffset dto => dto,
            DateTime dt => dt.Kind switch
            {
                DateTimeKind.Utc => new DateTimeOffset(dt, TimeSpan.Zero),
                DateTimeKind.Local => new DateTimeOffset(dt),
                _ => new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Local))
            },
            _ => DateTimeOffset.Parse(value.ToString() ?? string.Empty)
        };
    }

    private async Task<DrugIndexDto?> GetByKeyInternalAsync(
        IDbConnection conn,
        string drugId,
        string spec,
        CancellationToken ct)
    {
        const string sql = """
            select drug_id, spec, qty, rule_key, pre_tc, note, created_at, updated_at, version
            from drug_index
            where drug_id = @drug_id and spec = @spec
            limit 1
        """;

        await using var cmd = conn.CreateCommand(sql, _opt.CommandTimeoutSeconds);
        cmd.AddParam("drug_id", drugId);
        cmd.AddParam("spec", spec);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        return ReadDrugIndexDto(reader);
    }
}
