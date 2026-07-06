using System.Data;
using Microsoft.Extensions.Options;
using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;
using PacToolkits.Infrastructure.Database;

namespace PacToolkits.Infrastructure.Repositories;

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

    public Task<int> CountAsync(string? keyword, CancellationToken ct)
        => _db.WithConnection(async (conn, token) =>
        {
            const string sql = """
                select count(*)::int
                from drug_index
                where
                  @kw = ''
                  or drug_id ilike ('%' || @kw || '%')
                  or spec    ilike ('%' || @kw || '%')
                  or coalesce(rule_key,'') ilike ('%' || @kw || '%')
                  or coalesce(pre_tc,'')   ilike ('%' || @kw || '%')
                  or coalesce(note,'')     ilike ('%' || @kw || '%')
            """;

            await using var cmd = conn.CreateCommand(sql, _opt.CommandTimeoutSeconds);
            var kw = (keyword ?? string.Empty).Trim();
            cmd.AddParam("kw", kw);

            var scalar = await cmd.ExecuteScalarAsync(token);
            return scalar is int count ? count : Convert.ToInt32(scalar, System.Globalization.CultureInfo.InvariantCulture);
        }, ct);

    public Task<IReadOnlyList<DrugIndexDto>> ListCatalogAsync(int limit, CancellationToken ct)
        => _db.WithConnection(async (conn, token) =>
        {
            const string sql = """
                select drug_id, spec, qty, rule_key, pre_tc, note, created_at, updated_at, version
                from drug_index
                order by drug_id asc, spec asc
                limit @n
            """;

            await using var cmd = conn.CreateCommand(sql, _opt.CommandTimeoutSeconds);
            cmd.AddParam("n", Math.Clamp(limit, 1, 10_000));

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
            if (!await reader.ReadAsync(token))
            {
                return null;
            }

            return ReadDrugIndexDto(reader);
        }, ct);

    public Task<bool> IsDrugDeprecatedAsync(string drugId, CancellationToken ct)
        => _db.WithConnection(async (conn, token) =>
        {
            const string sql = """
                select
                  count(*)::int as total_count,
                  count(*) filter (where coalesce(note,'') ilike '%弃用%')::int as deprecated_count
                from drug_index
                where lower(btrim(drug_id)) = lower(btrim(@drug_id))
            """;

            await using var cmd = conn.CreateCommand(sql, _opt.CommandTimeoutSeconds);
            cmd.AddParam("drug_id", drugId);

            await using var reader = await cmd.ExecuteReaderAsync(token);
            if (!await reader.ReadAsync(token))
            {
                return false;
            }

            var total = reader.GetInt32(0);
            var deprecated = reader.GetInt32(1);
            return total > 0 && deprecated > 0 && deprecated == total;
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

                await using (var reader = await cmd.ExecuteReaderAsync(token))
                {
                    if (await reader.ReadAsync(token))
                    {
                        return ReadDrugIndexDto(reader);
                    }
                }

                var current = await GetByKeyAsync(conn, dto.DrugId, dto.Spec, token);
                throw new DrugIndexConcurrencyException("该记录已被其他终端创建，请刷新后重试", current);
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

            await using (var updated = await update.ExecuteReaderAsync(token))
            {
                if (await updated.ReadAsync(token))
                {
                    return ReadDrugIndexDto(updated);
                }
            }

            var latest = await GetByKeyAsync(conn, dto.DrugId, dto.Spec, token);
            throw new DrugIndexConcurrencyException("该记录已被其他终端修改，请刷新后重试", latest);
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

    public Task<DrugKeyFixPreviewDto> PreviewKeyFixAsync(
        string sourceDrugId,
        string sourceSpec,
        string targetDrugId,
        string targetSpec,
        CancellationToken ct)
        => _db.WithConnection(async (conn, token) =>
        {
            var srcDrug = sourceDrugId ?? string.Empty;
            var srcSpec = sourceSpec ?? string.Empty;
            var dstDrug = targetDrugId ?? string.Empty;
            var dstSpec = targetSpec ?? string.Empty;
            var srcDrugCheck = srcDrug.Trim();
            var srcSpecCheck = srcSpec.Trim();
            var dstDrugCheck = dstDrug.Trim();
            var dstSpecCheck = dstSpec.Trim();

            if (srcDrugCheck.Length == 0 || srcSpecCheck.Length == 0)
            {
                throw new ArgumentException("源药品名与规格不能为空");
            }

            if (dstDrugCheck.Length == 0 || dstSpecCheck.Length == 0)
            {
                throw new ArgumentException("目标药品名与规格不能为空");
            }

            const string srcSql = """
                select exists(
                  select 1 from drug_index
                  where drug_id = @src_drug and spec = @src_spec
                )
            """;
            const string dstSql = """
                select exists(
                  select 1 from drug_index
                  where drug_id = @dst_drug and spec = @dst_spec
                )
            """;
            const string poolSql = """
                select count(*)::int
                from trace_pool
                where drug_id = @src_drug and spec = @src_spec
            """;
            const string txnSql = """
                select count(*)::int
                from trace_txn
                where drug_id = @src_drug and spec = @src_spec
            """;

            bool sourceExists;
            await using (var cmd = conn.CreateCommand(srcSql, _opt.CommandTimeoutSeconds))
            {
                cmd.AddParam("src_drug", srcDrug);
                cmd.AddParam("src_spec", srcSpec);
                var scalar = await cmd.ExecuteScalarAsync(token);
                sourceExists = scalar is bool b && b;
            }

            bool targetExists;
            await using (var cmd = conn.CreateCommand(dstSql, _opt.CommandTimeoutSeconds))
            {
                cmd.AddParam("dst_drug", dstDrug);
                cmd.AddParam("dst_spec", dstSpec);
                var scalar = await cmd.ExecuteScalarAsync(token);
                targetExists = scalar is bool b && b;
            }

            var poolAffected = 0;
            await using (var cmd = conn.CreateCommand(poolSql, _opt.CommandTimeoutSeconds))
            {
                cmd.AddParam("src_drug", srcDrug);
                cmd.AddParam("src_spec", srcSpec);
                var scalar = await cmd.ExecuteScalarAsync(token);
                poolAffected = scalar is int i ? i : Convert.ToInt32(scalar ?? 0);
            }

            var txnAffected = 0;
            await using (var cmd = conn.CreateCommand(txnSql, _opt.CommandTimeoutSeconds))
            {
                cmd.AddParam("src_drug", srcDrug);
                cmd.AddParam("src_spec", srcSpec);
                var scalar = await cmd.ExecuteScalarAsync(token);
                txnAffected = scalar is int i ? i : Convert.ToInt32(scalar ?? 0);
            }

            return new DrugKeyFixPreviewDto(sourceExists, targetExists, poolAffected, txnAffected);
        }, ct);

    public Task<DrugKeyFixApplyResultDto> ApplyKeyFixAsync(
        DrugIndexDto source,
        DrugIndexDto target,
        string reason,
        string operatorName,
        string sourceTag,
        CancellationToken ct)
        => _db.WithTransaction(async (conn, tx, token) =>
        {
            var srcDrug = source.DrugId ?? string.Empty;
            var srcSpec = source.Spec ?? string.Empty;
            var dstDrug = target.DrugId ?? string.Empty;
            var dstSpec = target.Spec ?? string.Empty;
            var dstQty = target.Qty;
            var dstRuleKey = (object?)target.RuleKey ?? DBNull.Value;
            var dstPreTc = (object?)target.PreTc ?? DBNull.Value;
            var dstNote = (object?)target.Note ?? DBNull.Value;
            var reasonSafe = (reason ?? string.Empty).Trim();
            var operatorSafe = (operatorName ?? string.Empty).Trim();
            var sourceSafe = (sourceTag ?? string.Empty).Trim();
            var srcDrugCheck = srcDrug.Trim();
            var srcSpecCheck = srcSpec.Trim();
            var dstDrugCheck = dstDrug.Trim();
            var dstSpecCheck = dstSpec.Trim();

            if (srcDrugCheck.Length == 0 || srcSpecCheck.Length == 0)
            {
                throw new ArgumentException("源药品名与规格不能为空");
            }

            if (dstDrugCheck.Length == 0 || dstSpecCheck.Length == 0)
            {
                throw new ArgumentException("目标药品名与规格不能为空");
            }

            if (dstQty <= 0)
            {
                throw new ArgumentException("目标单盒数量必须大于 0");
            }

            if (reasonSafe.Length == 0)
            {
                throw new ArgumentException("迁移原因不能为空", nameof(reason));
            }

            if (operatorSafe.Length == 0)
            {
                throw new ArgumentException("操作人不能为空", nameof(operatorName));
            }

            if (sourceSafe.Length == 0)
            {
                throw new ArgumentException("来源不能为空", nameof(sourceTag));
            }

            var sameKey =
                string.Equals(srcDrug, dstDrug, StringComparison.Ordinal) &&
                string.Equals(srcSpec, dstSpec, StringComparison.Ordinal);

            const string lockSourceSql = """
                select drug_id, spec, qty, rule_key, pre_tc, note, created_at, updated_at, version
                from drug_index
                where drug_id = @src_drug and spec = @src_spec
                for update
            """;

            DrugIndexDto? sourceDb = null;
            await using (var cmd = conn.CreateCommand(lockSourceSql, _opt.CommandTimeoutSeconds, tx))
            {
                cmd.AddParam("src_drug", srcDrug);
                cmd.AddParam("src_spec", srcSpec);
                await using var reader = await cmd.ExecuteReaderAsync(token);
                if (await reader.ReadAsync(token))
                {
                    sourceDb = ReadDrugIndexDto(reader);
                }
            }

            if (sourceDb is null)
            {
                throw new InvalidOperationException("源药品规格不存在或已被移除");
            }

            if (sourceDb.Version != source.Version)
            {
                throw new DrugIndexConcurrencyException("该记录已被其他终端修改，请刷新后重试", sourceDb);
            }

            var targetExisted = false;
            if (!sameKey)
            {
                const string existsTargetSql = """
                    select exists(
                      select 1 from drug_index
                      where drug_id = @dst_drug and spec = @dst_spec
                    )
                """;

                await using var cmd = conn.CreateCommand(existsTargetSql, _opt.CommandTimeoutSeconds, tx);
                cmd.AddParam("dst_drug", dstDrug);
                cmd.AddParam("dst_spec", dstSpec);
                var scalar = await cmd.ExecuteScalarAsync(token);
                targetExisted = scalar is bool b && b;
            }
            else
            {
                targetExisted = true;
            }
            var poolAffected = 0;
            await using (var cmd = conn.CreateCommand(
                             "select count(*)::int from trace_pool where drug_id = @src_drug and spec = @src_spec",
                             _opt.CommandTimeoutSeconds, tx))
            {
                cmd.AddParam("src_drug", srcDrug);
                cmd.AddParam("src_spec", srcSpec);
                var scalar = await cmd.ExecuteScalarAsync(token);
                poolAffected = scalar is int i ? i : Convert.ToInt32(scalar ?? 0);
            }

            var txnAffected = 0;
            await using (var cmd = conn.CreateCommand(
                             "select count(*)::int from trace_txn where drug_id = @src_drug and spec = @src_spec",
                             _opt.CommandTimeoutSeconds, tx))
            {
                cmd.AddParam("src_drug", srcDrug);
                cmd.AddParam("src_spec", srcSpec);
                var scalar = await cmd.ExecuteScalarAsync(token);
                txnAffected = scalar is int i ? i : Convert.ToInt32(scalar ?? 0);
            }

            DrugIndexDto current;
            if (sameKey)
            {
                const string updateSameKeySql = """
                    update drug_index
                    set qty = @dst_qty,
                        rule_key = @dst_rule_key,
                        pre_tc = @dst_pre_tc,
                        note = @dst_note,
                        updated_at = clock_timestamp(),
                        version = version + 1
                    where drug_id = @src_drug
                      and spec = @src_spec
                      and version = @src_version
                    returning drug_id, spec, qty, rule_key, pre_tc, note, created_at, updated_at, version
                """;

                await using (var cmd = conn.CreateCommand(updateSameKeySql, _opt.CommandTimeoutSeconds, tx))
                {
                    cmd.AddParam("dst_qty", dstQty);
                    cmd.AddParam("dst_rule_key", dstRuleKey);
                    cmd.AddParam("dst_pre_tc", dstPreTc);
                    cmd.AddParam("dst_note", dstNote);
                    cmd.AddParam("src_drug", srcDrug);
                    cmd.AddParam("src_spec", srcSpec);
                    cmd.AddParam("src_version", source.Version);
                    await using var reader = await cmd.ExecuteReaderAsync(token);
                    if (!await reader.ReadAsync(token))
                    {
                        throw new DrugIndexConcurrencyException("该记录已被其他终端修改，请刷新后重试", sourceDb);
                    }

                    current = ReadDrugIndexDto(reader);
                }

                const string syncPoolQtySql = """
                    update trace_pool
                    set qty = @dst_qty,
                        remain = least(remain, @dst_qty)
                    where drug_id = @src_drug
                      and spec = @src_spec
                """;
                await using (var cmd = conn.CreateCommand(syncPoolQtySql, _opt.CommandTimeoutSeconds, tx))
                {
                    cmd.AddParam("dst_qty", dstQty);
                    cmd.AddParam("src_drug", srcDrug);
                    cmd.AddParam("src_spec", srcSpec);
                    await cmd.ExecuteNonQueryAsync(token);
                }
            }
            else if (!targetExisted)
            {
                // Target key not present: move source PK directly and rely on FK ON UPDATE CASCADE.
                const string movePkSql = """
                    update drug_index
                    set drug_id = @dst_drug,
                        spec = @dst_spec,
                        qty = @dst_qty,
                        rule_key = @dst_rule_key,
                        pre_tc = @dst_pre_tc,
                        note = @dst_note,
                        updated_at = clock_timestamp(),
                        version = version + 1
                    where drug_id = @src_drug
                      and spec = @src_spec
                      and version = @src_version
                    returning drug_id, spec, qty, rule_key, pre_tc, note, created_at, updated_at, version
                """;

                await using (var moveCmd = conn.CreateCommand(movePkSql, _opt.CommandTimeoutSeconds, tx))
                {
                    moveCmd.AddParam("dst_drug", dstDrug);
                    moveCmd.AddParam("dst_spec", dstSpec);
                    moveCmd.AddParam("dst_qty", dstQty);
                    moveCmd.AddParam("dst_rule_key", dstRuleKey);
                    moveCmd.AddParam("dst_pre_tc", dstPreTc);
                    moveCmd.AddParam("dst_note", dstNote);
                    moveCmd.AddParam("src_drug", srcDrug);
                    moveCmd.AddParam("src_spec", srcSpec);
                    moveCmd.AddParam("src_version", source.Version);

                    await using var reader = await moveCmd.ExecuteReaderAsync(token);
                    if (!await reader.ReadAsync(token))
                    {
                        throw new DrugIndexConcurrencyException("该记录已被其他终端修改，请刷新后重试", sourceDb);
                    }

                    current = ReadDrugIndexDto(reader);
                }

                const string syncMovedPoolQtySql = """
                    update trace_pool
                    set qty = @dst_qty,
                        remain = least(remain, @dst_qty)
                    where drug_id = @dst_drug
                      and spec = @dst_spec
                """;
                await using (var cmd = conn.CreateCommand(syncMovedPoolQtySql, _opt.CommandTimeoutSeconds, tx))
                {
                    cmd.AddParam("dst_qty", dstQty);
                    cmd.AddParam("dst_drug", dstDrug);
                    cmd.AddParam("dst_spec", dstSpec);
                    await cmd.ExecuteNonQueryAsync(token);
                }
            }
            else
            {
                const string upsertTargetSql = """
                    insert into drug_index(drug_id, spec, qty, rule_key, pre_tc, note, created_at, updated_at, version)
                    values(@dst_drug, @dst_spec, @dst_qty, @dst_rule_key, @dst_pre_tc, @dst_note, clock_timestamp(), clock_timestamp(), 0)
                    on conflict (drug_id, spec) do update
                    set qty = excluded.qty,
                        rule_key = excluded.rule_key,
                        pre_tc = excluded.pre_tc,
                        note = excluded.note,
                        updated_at = clock_timestamp(),
                        version = drug_index.version + 1
                    returning drug_id, spec, qty, rule_key, pre_tc, note, created_at, updated_at, version
                """;

                await using (var cmd = conn.CreateCommand(upsertTargetSql, _opt.CommandTimeoutSeconds, tx))
                {
                    cmd.AddParam("dst_drug", dstDrug);
                    cmd.AddParam("dst_spec", dstSpec);
                    cmd.AddParam("dst_qty", dstQty);
                    cmd.AddParam("dst_rule_key", dstRuleKey);
                    cmd.AddParam("dst_pre_tc", dstPreTc);
                    cmd.AddParam("dst_note", dstNote);
                    await using var reader = await cmd.ExecuteReaderAsync(token);
                    if (!await reader.ReadAsync(token))
                    {
                        throw new InvalidOperationException("目标药品规格写入失败");
                    }

                    current = ReadDrugIndexDto(reader);
                }

                const string updatePoolSql = """
                    update trace_pool
                    set drug_id = @dst_drug,
                        spec = @dst_spec,
                        qty = @dst_qty,
                        remain = least(remain, @dst_qty)
                    where drug_id = @src_drug
                      and spec = @src_spec
                """;
                await using (var cmd = conn.CreateCommand(updatePoolSql, _opt.CommandTimeoutSeconds, tx))
                {
                    cmd.AddParam("dst_drug", dstDrug);
                    cmd.AddParam("dst_spec", dstSpec);
                    cmd.AddParam("dst_qty", dstQty);
                    cmd.AddParam("src_drug", srcDrug);
                    cmd.AddParam("src_spec", srcSpec);
                    await cmd.ExecuteNonQueryAsync(token);
                }

                const string updateTxnSql = """
                    update trace_txn
                    set drug_id = @dst_drug,
                        spec = @dst_spec
                    where drug_id = @src_drug
                      and spec = @src_spec
                """;
                await using (var cmd = conn.CreateCommand(updateTxnSql, _opt.CommandTimeoutSeconds, tx))
                {
                    cmd.AddParam("dst_drug", dstDrug);
                    cmd.AddParam("dst_spec", dstSpec);
                    cmd.AddParam("src_drug", srcDrug);
                    cmd.AddParam("src_spec", srcSpec);
                    await cmd.ExecuteNonQueryAsync(token);
                }

                const string deleteSourceSql = """
                    delete from drug_index
                    where drug_id = @src_drug
                      and spec = @src_spec
                """;
                await using (var cmd = conn.CreateCommand(deleteSourceSql, _opt.CommandTimeoutSeconds, tx))
                {
                    cmd.AddParam("src_drug", srcDrug);
                    cmd.AddParam("src_spec", srcSpec);
                    var deleted = await cmd.ExecuteNonQueryAsync(token);
                    if (deleted <= 0)
                    {
                        throw new InvalidOperationException("源药品规格删除失败");
                    }
                }
            }

            long auditId = 0;
            const string hasAuditSql = "select to_regclass('public.drug_key_fix_audit') is not null";
            var hasAuditTable = false;
            await using (var hasAuditCmd = conn.CreateCommand(hasAuditSql, _opt.CommandTimeoutSeconds, tx))
            {
                var scalar = await hasAuditCmd.ExecuteScalarAsync(token);
                hasAuditTable = scalar is bool b && b;
            }

            if (hasAuditTable)
            {
                const string auditSql = """
                    insert into drug_key_fix_audit(
                      at,
                      operator_name,
                      source,
                      reason,
                      old_drug_id,
                      old_spec,
                      new_drug_id,
                      new_spec,
                      target_existed,
                      trace_pool_affected,
                      trace_txn_affected,
                      success,
                      error
                    )
                    values(
                      clock_timestamp(),
                      @operator_name,
                      @source,
                      @reason,
                      @old_drug_id,
                      @old_spec,
                      @new_drug_id,
                      @new_spec,
                      @target_existed,
                      @trace_pool_affected,
                      @trace_txn_affected,
                      true,
                      null
                    )
                    returning id
                """;

                await using var auditCmd = conn.CreateCommand(auditSql, _opt.CommandTimeoutSeconds, tx);
                auditCmd.AddParam("operator_name", operatorSafe);
                auditCmd.AddParam("source", sourceSafe);
                auditCmd.AddParam("reason", reasonSafe);
                auditCmd.AddParam("old_drug_id", srcDrug);
                auditCmd.AddParam("old_spec", srcSpec);
                auditCmd.AddParam("new_drug_id", dstDrug);
                auditCmd.AddParam("new_spec", dstSpec);
                auditCmd.AddParam("target_existed", targetExisted);
                auditCmd.AddParam("trace_pool_affected", poolAffected);
                auditCmd.AddParam("trace_txn_affected", txnAffected);
                var scalar = await auditCmd.ExecuteScalarAsync(token);
                auditId = scalar is long l ? l : Convert.ToInt64(scalar ?? 0L);
            }
            else
            {
                // Keep key-fix available for old schema; audit becomes best-effort until DB upgraded.
            }

            return new DrugKeyFixApplyResultDto(targetExisted, poolAffected, txnAffected, auditId, current);
        }, IsolationLevel.ReadCommitted, ct);

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

    private async Task<DrugIndexDto?> GetByKeyAsync(
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
        if (!await reader.ReadAsync(ct))
        {
            return null;
        }

        return ReadDrugIndexDto(reader);
    }
}
