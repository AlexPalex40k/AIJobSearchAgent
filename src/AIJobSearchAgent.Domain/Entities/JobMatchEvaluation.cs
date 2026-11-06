namespace AIJobSearchAgent.Domain.Entities;

public sealed class JobMatchEvaluation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid JobVacancyId { get; set; }
    public required string ProfileHash { get; set; }
    public required string ModelName { get; set; }
    public required string PromptVersion { get; set; }
    public int? Rating { get; set; }
    public List<string> MatchedSkills { get; set; } = [];
    public List<string> MissingRequiredSkills { get; set; } = [];
    public List<string> MissingOptionalSkills { get; set; } = [];
    public required string Explanation { get; set; }
    public EvaluationStatus EvaluationStatus { get; set; }
    public DateTimeOffset EvaluatedAt { get; set; } = DateTimeOffset.UtcNow;
    public JobVacancy JobVacancy { get; set; } = null!;
}
