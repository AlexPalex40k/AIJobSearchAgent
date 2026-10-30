using AIJobSearchAgent.Domain.Entities;

namespace AIJobSearchAgent.Application.Services;

public static class JobVacancyNormalizer
{
    public static bool TryNormalize(JobVacancy vacancy)
    {
        vacancy.ExternalId = vacancy.ExternalId?.Trim() ?? string.Empty;
        vacancy.Source = vacancy.Source?.Trim() ?? string.Empty;
        vacancy.Title = vacancy.Title?.Trim() ?? string.Empty;
        vacancy.Company = vacancy.Company?.Trim() ?? string.Empty;
        vacancy.Location = vacancy.Location?.Trim() ?? string.Empty;
        vacancy.Description = vacancy.Description?.Trim() ?? string.Empty;
        vacancy.Url = vacancy.Url?.Trim() ?? string.Empty;

        if (vacancy.Id == Guid.Empty)
        {
            vacancy.Id = Guid.NewGuid();
        }

        if (vacancy.CreatedAt == default)
        {
            vacancy.CreatedAt = DateTimeOffset.UtcNow;
        }

        return vacancy.ExternalId.Length is > 0 and <= 300 &&
               vacancy.Source.Length is > 0 and <= 100 &&
               vacancy.Title.Length is > 0 and <= 500 &&
               vacancy.Company.Length is > 0 and <= 300 &&
               vacancy.Location.Length <= 300 &&
               vacancy.Description.Length <= 100_000 &&
               vacancy.Url.Length is > 0 and <= 2_000 &&
               Uri.TryCreate(vacancy.Url, UriKind.Absolute, out var uri) &&
               (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }
}
