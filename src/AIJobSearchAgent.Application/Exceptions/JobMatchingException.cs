using AIJobSearchAgent.Domain.Entities;

namespace AIJobSearchAgent.Application.Exceptions;

public sealed class JobMatchingException : Exception
{
    public JobMatchingException(
        string message,
        string modelName,
        string promptVersion,
        EvaluationStatus evaluationStatus,
        Exception? innerException = null)
        : base(message, innerException)
    {
        ModelName = modelName;
        PromptVersion = promptVersion;
        EvaluationStatus = evaluationStatus;
    }

    public string ModelName { get; }
    public string PromptVersion { get; }
    public EvaluationStatus EvaluationStatus { get; }
}
