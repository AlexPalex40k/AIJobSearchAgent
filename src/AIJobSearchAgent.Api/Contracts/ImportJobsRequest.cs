using System.ComponentModel.DataAnnotations;

namespace AIJobSearchAgent.Api.Contracts;

public sealed class ImportJobsRequest
{
    public IReadOnlyList<string> Keywords { get; init; } = [];

    [Range(1, 10)]
    public int MaxPages { get; init; } = 1;
}
