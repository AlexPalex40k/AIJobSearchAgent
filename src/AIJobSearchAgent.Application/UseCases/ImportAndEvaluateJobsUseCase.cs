using AIJobSearchAgent.Application.Models;

namespace AIJobSearchAgent.Application.UseCases;

public sealed class ImportAndEvaluateJobsUseCase(
    ImportJobsUseCase importJobs,
    EvaluateJobsBatchUseCase evaluateJobsBatch)
{
    public async Task<ImportAndEvaluateJobsSummary> ExecuteAsync(
        JobSearchCriteria criteria,
        CancellationToken cancellationToken)
    {
        var importSummary = await importJobs.ExecuteAsync(criteria, cancellationToken);
        var successfullyEvaluated = 0;
        var skipped = 0;
        var failed = 0;

        foreach (var batch in importSummary.PersistedVacancyIds
                     .Distinct()
                     .Chunk(EvaluateJobsBatchUseCase.MaximumBatchSize))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var batchSummary = await evaluateJobsBatch.ExecuteAsync(batch, cancellationToken);
            successfullyEvaluated += batchSummary.SuccessfullyEvaluatedCount;
            skipped += batchSummary.SkippedCount;
            failed += batchSummary.FailedCount;
        }

        return new ImportAndEvaluateJobsSummary(
            importSummary,
            successfullyEvaluated,
            skipped,
            failed);
    }
}
