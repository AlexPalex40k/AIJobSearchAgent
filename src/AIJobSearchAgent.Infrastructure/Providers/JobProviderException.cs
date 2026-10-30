namespace AIJobSearchAgent.Infrastructure.Providers;

public sealed class JobProviderException(string message, Exception innerException)
    : Exception(message, innerException);
