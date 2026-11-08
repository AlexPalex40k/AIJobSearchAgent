namespace AIJobSearchAgent.Application.Exceptions;

public enum RequirementsExtractionFailureKind
{
    Failed,
    TimedOut
}

public sealed class VacancyRequirementsExtractionException : Exception
{
    public VacancyRequirementsExtractionException(
        string message,
        string modelName,
        string promptVersion,
        RequirementsExtractionFailureKind failureKind,
        Exception? innerException = null)
        : base(message, innerException)
    {
        ModelName = modelName;
        PromptVersion = promptVersion;
        FailureKind = failureKind;
    }

    public string ModelName { get; }
    public string PromptVersion { get; }
    public RequirementsExtractionFailureKind FailureKind { get; }
}
