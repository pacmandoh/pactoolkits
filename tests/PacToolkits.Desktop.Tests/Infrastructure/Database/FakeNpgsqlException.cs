namespace Npgsql;

// FullName 以 Npgsql. 开头，供 TransportErrors 的 Pg 判定
internal sealed class FakeNpgsqlException : Exception
{
    public FakeNpgsqlException(string message, Exception? inner)
        : base(message, inner)
    {
    }
}
