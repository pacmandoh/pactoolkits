using System;
using PacToolkits.Application.DTOs;

namespace PacToolkits.Desktop.Avalonia.Services.Infrastructure.Api;

/// <summary>HTTP 409 OCC；Problem 含 currentVersion</summary>
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
}
