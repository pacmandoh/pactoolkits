namespace PacToolkits.Application.Abstractions;

public interface IDbConfigNotifier
{
    event EventHandler? Applied;
}
