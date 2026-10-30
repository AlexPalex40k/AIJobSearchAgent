namespace AIJobSearchAgent.Application.Models;

public sealed record JobQuery(string? Keyword, string? Source, int Page, int PageSize);
