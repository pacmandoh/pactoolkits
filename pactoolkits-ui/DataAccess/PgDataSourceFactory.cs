using System;
using Npgsql;

namespace pactoolkits_ui.DataAccess;

public interface IPgDataSourceFactory
{
    NpgsqlDataSource Get();
    void Rebuild(PgOptions options);
}

public sealed class PgDataSourceFactory : IPgDataSourceFactory, IDisposable
{
    private readonly object _gate = new();
    private NpgsqlDataSource? _dataSource;

    public NpgsqlDataSource Get()
    {
        if (_dataSource == null)
            throw new InvalidOperationException("PostgreSQL 未配置");

        return _dataSource;
    }

    public void Rebuild(PgOptions opt)
    {
        lock (_gate)
        {
            _dataSource?.Dispose();

            var csb = new NpgsqlConnectionStringBuilder
            {
                Host = opt.Host,
                Port = opt.Port,
                Database = opt.Database,
                Username = opt.Username,
                Password = opt.Password,
                Pooling = true,
                MaxPoolSize = opt.PoolSize,
                Timeout = opt.ConnectTimeoutSeconds
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