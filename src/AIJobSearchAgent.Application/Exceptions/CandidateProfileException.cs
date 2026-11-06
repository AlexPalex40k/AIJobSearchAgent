namespace AIJobSearchAgent.Application.Exceptions;

public sealed class CandidateProfileException(string message, Exception? innerException = null)
    : Exception(message, innerException);
