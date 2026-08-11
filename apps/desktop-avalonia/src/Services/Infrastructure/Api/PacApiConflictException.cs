using System;
using System.Collections.Generic;
using PacToolkits.Application.DTOs;

namespace PacToolkits.Desktop.Avalonia.Services.Infrastructure.Api;

/// <summary>HTTP 409；Problem 可含 currentVersion，库存批量 OCC 另含 conflicts</summary>
public sealed class PacApiConflictException : PacApiException
{
    public PacApiConflictException(PacApiProblem problem, Exception? inner = null)
        : base(problem, inner)
    {
        if (problem.Status != 409)
        {
            throw new ArgumentException("PacApiConflictException requires HTTP 409", nameof(problem));
        }
    }

    public IReadOnlyList<StockRowEditConflict>? Conflicts => Problem.Conflicts;
}
