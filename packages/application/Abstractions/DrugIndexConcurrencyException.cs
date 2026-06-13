using PacToolkits.Application.DTOs;

namespace PacToolkits.Application.Abstractions;

public sealed class DrugIndexConcurrencyException : Exception
{
    public DrugIndexDto? Current { get; }

    public DrugIndexConcurrencyException(string message, DrugIndexDto? current = null)
        : base(message)
    {
        Current = current;
    }
}
