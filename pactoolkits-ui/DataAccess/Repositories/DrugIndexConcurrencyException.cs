using System;
using pactoolkits_ui.Contracts;

namespace pactoolkits_ui.Repositories;

public sealed class DrugIndexConcurrencyException : Exception
{
    public DrugIndexDto? Current { get; }

    public DrugIndexConcurrencyException(string message, DrugIndexDto? current = null)
        : base(message)
    {
        Current = current;
    }
}
