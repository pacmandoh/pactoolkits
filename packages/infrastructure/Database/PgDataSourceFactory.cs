using Npgsql;
using PacToolkits.Application.Abstractions;

namespace PacToolkits.Infrastructure.Database;

/// <summary>
/// NpgsqlDataSource 工厂：持有可热重建的连接池
/// </summary>
public interface IPgDataSourceFactory
{
    NpgsqlDataSource Get();
    void Rebuild(PgOptions options);
}

/// <summary>
/// <c>NpgsqlDataSource</c> 的创建 / 释放实现
///
/// 负责：按 <c>PgOptions</c> 重建池；未配置时拒绝 <c>Get</c>
/// 不打开业务会话（由 <c>PgDb</c> 使用）
/// </summary>
public sealed class PgDataSourceFactory : IPgDataSourceFactory, IDisposable
{
    private readonly object _gate = new();
    private NpgsqlDataSource? _dataSource;

    public NpgsqlDataSource Get()
    {
        if (_dataSource == null)
        {
            throw new InvalidOperationException("PostgreSQL 未配置");
        }

        return _dataSource;
    }

    public void Rebuild(PgOptions opt)
    {
        lock (_gate)
        {
            _dataSource?.Dispose();

            var csb = new NpgsqlConnectionStringBuilder(PgConnectionFactory.BuildConnectionString(opt))
            {
                Pooling = true,
                MaxPoolSize = opt.PoolSize
            };

            _dataSource = NpgsqlDataSource.Create(csb);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _dataSource?.Dispose();
            _dataSource = null;
        }
    }
}
