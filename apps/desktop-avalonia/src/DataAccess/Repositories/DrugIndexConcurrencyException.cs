using System;
using PacToolkits.Desktop.Avalonia.Contracts;

namespace PacToolkits.Desktop.Avalonia.Repositories;

public sealed class DrugIndexConcurrencyException : Exception
{
    public DrugIndexDto? Current { get; }

    public DrugIndexConcurrencyException(string message, DrugIndexDto? current = null)
        : base(message)
    {
        Current = current;
    }
}
