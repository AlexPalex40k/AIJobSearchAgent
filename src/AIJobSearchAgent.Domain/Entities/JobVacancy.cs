namespace AIJobSearchAgent.Domain.Entities;

public sealed class JobVacancy
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string ExternalId { get; set; }
    public required string Source { get; set; }
    public required string Title { get; set; }
    public required string Company { get; set; }
    public required string Location { get; set; }
    public bool IsRemote { get; set; }
    public required string Description { get; set; }
    public required string Url { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
