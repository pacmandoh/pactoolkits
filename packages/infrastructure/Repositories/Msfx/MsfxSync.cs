using Microsoft.Extensions.Options;
using PacToolkits.Application.Abstractions;

namespace PacToolkits.Infrastructure.Repositories;

/// <summary>
/// MSFX 同步数据访问（partial：Pull / Ingest / Mapping / Inject / Query / Helpers）
/// 不调用外部 MSFX API；仅持久化与查询
/// </summary>
public sealed partial class MsfxSyncRepo :
    IMsfxPullRepo,
    IMsfxIngestRepo,
    IMsfxMappingRepo,
    IMsfxInjectRepo,
    IMsfxQueryRepo
{
    private const int MappingCommandTimeoutSeconds = 120;

    // 未结案码仍留在映射队列：已 MAPPED 但码状态为 NEW/TASKED/FAILED
    private const string MapQueueBaseWhere = """
        (
          s.map_status in ('PENDING', 'NEED_REVIEW', 'FAILED')
          or (s.map_status = 'MAPPED' and s.code_status in ('NEW', 'TASKED', 'FAILED'))
        )
        and (@code_status::text is null or s.code_status = @code_status::text)
        """;

    // 手工映射按现有值、输入值、归一化结果和占位符的顺序回填，空字符串不视为有效值
    private const string ManualMapNormBackfillSetClause = """
        source_name_norm = coalesce(
          nullif(trim(s.source_name_norm), ''),
          nullif(trim(@g_source_name_norm), ''),
          nullif(trim(msfx_norm_name(s.source_drug_name_raw)), ''),
          nullif(trim(msfx_norm_name(@drug_id)), ''),
          '-'),
        source_spec_norm = coalesce(
          nullif(trim(s.source_spec_norm), ''),
          nullif(trim(@g_source_spec_norm), ''),
          nullif(trim(msfx_norm_spec(s.source_spec_raw, (
            select i.pkg_spec
            from msfx_code_relation r
            join msfx_upout_item i on i.id = r.upout_item_id
            where r.id = s.source_relation_id
            limit 1
          ))), ''),
          nullif(trim(msfx_norm_spec(@spec, null)), ''),
          '-'),
        mapped_drug_id = coalesce(nullif(trim(@drug_id), ''), s.mapped_drug_id),
        mapped_spec = coalesce(nullif(trim(@spec), ''), s.mapped_spec),
        """;

    private readonly IDb _db;
    private readonly PgOptions _opt;

    public MsfxSyncRepo(IDb db, IOptions<PgOptions> opt)
    {
        _db = db;
        _opt = opt.Value;
    }
}
