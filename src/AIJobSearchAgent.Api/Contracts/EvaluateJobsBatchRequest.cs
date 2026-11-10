namespace AIJobSearchAgent.Api.Contracts;

public sealed class EvaluateJobsBatchRequest
{
    public IReadOnlyList<Guid>? JobIds { get; init; }
}
