using System.ComponentModel.DataAnnotations;

namespace AIJobSearchAgent.Infrastructure.Matching;

public sealed class OllamaOptions
{
    public const string SectionName = "Ollama";

    [Required, Url]
    public string Endpoint { get; init; } = "http://localhost:11434";

    [Required, StringLength(200)]
    public string Model { get; init; } = "qwen3:1.7b";

    [Range(1, 600)]
    public int TimeoutSeconds { get; init; } = 120;

    [Range(1_000, 100_000)]
    public int MaxDescriptionCharacters { get; init; } = 30_000;
}
