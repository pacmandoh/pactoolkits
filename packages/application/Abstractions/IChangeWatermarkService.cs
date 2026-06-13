namespace PacToolkits.Application.Abstractions;

public interface IChangeWatermarkService : IDisposable
{
    event Action<string>? TopicChanged;
    void Start();
}
