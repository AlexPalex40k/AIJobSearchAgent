using System.Text.Json;
using System.Text.Json.Nodes;
using AIJobSearchAgent.Application.Abstractions;
using AIJobSearchAgent.Application.Models;
using AIJobSearchAgent.Benchmarks;
using AIJobSearchAgent.Domain.Entities;
using AIJobSearchAgent.Domain.Profiles;
using AIJobSearchAgent.Infrastructure.Profiles;
using Microsoft.Extensions.Options;

namespace AIJobSearchAgent.Tests;

public sealed class BenchmarkTests
{
    [Fact]
    public async Task CommittedBenchmarkInputs_AreValidSyntheticAndApproved()
    {
        var solutionRoot = FindSolutionRoot();
        var dataset = await BenchmarkDatasetLoader.LoadAsync(
            Path.Combine(solutionRoot, "benchmarks", "dataset.v1.json"),
            CancellationToken.None);
        var profileProvider = new CandidateProfileFileProvider(
            Options.Create(new CandidateProfileOptions
            {
                Path = Path.Combine(solutionRoot, "benchmarks", "candidate-profile.synthetic.json")
            }),
            new CandidateProfileFileLocation(solutionRoot));
        var profile = await profileProvider.LoadAsync(CancellationToken.None);

        Assert.Equal("1.0", dataset.Dataset.DatasetVersion);
        Assert.Equal(18, dataset.Dataset.Cases.Count);
        Assert.All(dataset.Dataset.Cases, benchmarkCase =>
            Assert.Equal(ReferenceLabelStatus.Approved, benchmarkCase.LabelStatus));
        Assert.All(dataset.Dataset.Cases, benchmarkCase =>
        {
            Assert.False(string.IsNullOrWhiteSpace(benchmarkCase.ReviewedBy));
            Assert.NotNull(benchmarkCase.ReviewedAt);
        });
        Assert.All(dataset.Dataset.Cases, benchmarkCase => Assert.Null(benchmarkCase.VacancyUrl));
        Assert.Equal(64, dataset.DatasetHash.Length);
        Assert.Null(profile.Profile.Name);
        Assert.Equal(6, profile.Profile.YearsProfessionalExperience);
        Assert.Equal("B2", profile.Profile.SpokenLanguages[0].Level);
        Assert.Equal("native", profile.Profile.SpokenLanguages[1].Level);
        Assert.Equal(64, profile.ProfileHash.Length);
    }

    [Fact]
    public void DatasetValidation_AcceptsValidDatasetAndRejectsDuplicateCaseIds()
    {
        var valid = new BenchmarkDataset
        {
            DatasetVersion = "test-v1",
            Cases = [CreateCase("case-one"), CreateCase("case-two")]
        };

        BenchmarkDatasetValidator.Validate(valid);

        var invalid = new BenchmarkDataset
        {
            DatasetVersion = "test-v1",
            Cases = [CreateCase("duplicate"), CreateCase("duplicate")]
        };
        var exception = Assert.Throws<BenchmarkValidationException>(() =>
            BenchmarkDatasetValidator.Validate(invalid));
        Assert.Contains("duplicated", exception.Message);
    }

    [Fact]
    public void DatasetValidation_RequiresApprovalMetadataButAllowsProvisionalLabels()
    {
        BenchmarkDatasetValidator.Validate(new BenchmarkDataset
        {
            DatasetVersion = "test-v1",
            Cases = [CreateCase("provisional")]
        });

        var approvedWithoutReview = CreateCase("approved", ReferenceLabelStatus.Approved);
        var exception = Assert.Throws<BenchmarkValidationException>(() =>
            BenchmarkDatasetValidator.Validate(new BenchmarkDataset
            {
                DatasetVersion = "test-v1",
                Cases = [approvedWithoutReview]
            }));

        Assert.Contains("reviewedBy", exception.Message);
        Assert.Contains("reviewedAt", exception.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public void DatasetValidation_RejectsOutOfRangeRatings(int rating)
    {
        var invalidCase = WithExpectedRating(CreateCase("invalid-rating"), rating);

        var exception = Assert.Throws<BenchmarkValidationException>(() =>
            BenchmarkDatasetValidator.Validate(new BenchmarkDataset
            {
                DatasetVersion = "test-v1",
                Cases = [invalidCase]
            }));

        Assert.Contains("between 1 and 5", exception.Message);
    }

    [Theory]
    [InlineData("relative/jobs/123")]
    [InlineData("ftp://jobs.example.test/123")]
    [InlineData("")]
    public void DatasetValidation_RejectsInvalidOptionalVacancyUrls(string vacancyUrl)
    {
        var benchmarkCase = CreateCase("invalid-url", vacancyUrl: vacancyUrl);

        var exception = Assert.Throws<BenchmarkValidationException>(() =>
            BenchmarkDatasetValidator.Validate(new BenchmarkDataset
            {
                DatasetVersion = "test-v1",
                Cases = [benchmarkCase]
            }));

        Assert.Contains("vacancyUrl", exception.Message);
    }

    [Fact]
    public void DatasetValidation_AllowsMissingAndAbsoluteHttpVacancyUrls()
    {
        var dataset = new BenchmarkDataset
        {
            DatasetVersion = "test-v1",
            Cases =
            [
                CreateCase("synthetic-without-url"),
                CreateCase("real-with-url", vacancyUrl: "https://careers.example.test/jobs/123")
            ]
        };

        BenchmarkDatasetValidator.Validate(dataset);
    }

    [Fact]
    public void Metrics_CalculateAccuracyOnlyForSuccessfulApprovedLabels()
    {
        var results = new[]
        {
            CreateResult("exact", 5, 5, ReferenceLabelStatus.Approved),
            CreateResult("within-one", 4, 3, ReferenceLabelStatus.Approved),
            CreateResult("outside-one", 3, 1, ReferenceLabelStatus.Approved),
            CreateResult("provisional-perfect", 1, 1, ReferenceLabelStatus.Provisional)
        };

        var metrics = BenchmarkMetricsCalculator.Calculate(results);

        Assert.Equal(1, metrics.ExactMatchAccuracy!.Numerator);
        Assert.Equal(3, metrics.ExactMatchAccuracy.Denominator);
        Assert.Equal(1d / 3, metrics.ExactMatchAccuracy.Value!.Value, 10);
        Assert.Equal(2, metrics.WithinOnePointAccuracy!.Numerator);
        Assert.Equal(3, metrics.WithinOnePointAccuracy.Denominator);
        Assert.Equal(2d / 3, metrics.WithinOnePointAccuracy.Value!.Value, 10);
    }

    [Fact]
    public void Metrics_CalculateMeanAbsoluteErrorForSuccessfulApprovedLabels()
    {
        var metrics = BenchmarkMetricsCalculator.Calculate(
        [
            CreateResult("exact", 5, 5, ReferenceLabelStatus.Approved),
            CreateResult("one-away", 4, 3, ReferenceLabelStatus.Approved),
            CreateResult("two-away", 3, 1, ReferenceLabelStatus.Approved)
        ]);

        Assert.Equal(1, metrics.MeanAbsoluteError!.Value);
        Assert.Equal(3, metrics.MeanAbsoluteError.EvaluationCount);
    }

    [Fact]
    public void Metrics_ReportFailureRateWithAllAttemptedCasesAsDenominator()
    {
        var metrics = BenchmarkMetricsCalculator.Calculate(
        [
            CreateResult("success", 4, 4, ReferenceLabelStatus.Approved),
            CreateResult("failed", 4, null, ReferenceLabelStatus.Approved, EvaluationStatus.Failed),
            CreateResult("timeout", 4, null, ReferenceLabelStatus.Approved, EvaluationStatus.TimedOut)
        ]);

        Assert.Equal(2, metrics.FailureRate.Numerator);
        Assert.Equal(3, metrics.FailureRate.Denominator);
        Assert.Equal(2d / 3, metrics.FailureRate.Value!.Value, 10);
        Assert.Equal(1, metrics.FailedEvaluations);
        Assert.Equal(1, metrics.TimedOutEvaluations);
    }

    [Fact]
    public void Metrics_HandleEmptyResultsWithoutInventingValues()
    {
        var metrics = BenchmarkMetricsCalculator.Calculate([]);

        Assert.Null(metrics.FailureRate.Value);
        Assert.Equal(0, metrics.FailureRate.Denominator);
        Assert.Null(metrics.ExactMatchAccuracy);
        Assert.Null(metrics.WithinOnePointAccuracy);
        Assert.Null(metrics.MeanAbsoluteError);
        Assert.Null(metrics.Latency.MeanMilliseconds);
        Assert.Null(metrics.Latency.MedianMilliseconds);
        Assert.Null(metrics.Latency.P95Milliseconds);
    }

    [Fact]
    public void Metrics_CalculateLatencyStatisticsAndRequireEnoughSamplesForP95()
    {
        var smallSample = BenchmarkMetricsCalculator.Calculate(
        [
            CreateResult("one", 4, 4, ReferenceLabelStatus.Approved, latencyMilliseconds: 10),
            CreateResult("two", 4, 4, ReferenceLabelStatus.Approved, latencyMilliseconds: 20)
        ]);
        var fullSample = BenchmarkMetricsCalculator.Calculate(Enumerable.Range(1, 20)
            .Select(index => CreateResult(
                $"case-{index}",
                4,
                4,
                ReferenceLabelStatus.Approved,
                latencyMilliseconds: index))
            .ToArray());

        Assert.Equal(15, smallSample.Latency.MeanMilliseconds);
        Assert.Equal(15, smallSample.Latency.MedianMilliseconds);
        Assert.Null(smallSample.Latency.P95Milliseconds);
        Assert.Equal(10.5, fullSample.Latency.MeanMilliseconds);
        Assert.Equal(10.5, fullSample.Latency.MedianMilliseconds);
        Assert.Equal(19, fullSample.Latency.P95Milliseconds);
        Assert.Equal(20, fullSample.Latency.EvaluationCount);
    }

    [Fact]
    public async Task ResultStore_SerializesAndLoadsRunWithoutOverwriting()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"benchmark-results-{Guid.NewGuid():N}");
        try
        {
            const string vacancyUrl = "https://careers.example.test/jobs/123";
            var result = CreateRun(
                "dataset-hash",
                "profile-hash",
                "model-a",
                "prompt-v1",
                4,
                vacancyUrl);
            var secondResult = CreateRun("dataset-hash", "profile-hash", "model-a", "prompt-v1", 4);

            var firstPath = await BenchmarkResultStore.SaveAsync(result, directory, CancellationToken.None);
            var secondPath = await BenchmarkResultStore.SaveAsync(secondResult, directory, CancellationToken.None);
            var loaded = await BenchmarkResultStore.LoadAsync(firstPath, CancellationToken.None);

            Assert.NotEqual(firstPath, secondPath);
            Assert.Equal(result.RunId, loaded.RunId);
            Assert.Equal(result.DatasetHash, loaded.DatasetHash);
            var caseResult = Assert.Single(loaded.CaseResults);
            Assert.Equal(4, caseResult.PredictedRating);
            Assert.Equal(".NET Engineer", caseResult.VacancyTitle);
            Assert.Equal("Build C# services.", caseResult.VacancyDescription);
            Assert.Equal(vacancyUrl, caseResult.VacancyUrl);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task ResultStore_LoadsExistingResultsWithoutNewVacancyContextFields()
    {
        var result = CreateRun("dataset-hash", "profile-hash", "model-a", "prompt-v1", 4);
        var resultJson = JsonSerializer.SerializeToNode(result, BenchmarkJson.SerializerOptions)!.AsObject();
        var caseResultJson = resultJson["caseResults"]!.AsArray()[0]!.AsObject();
        caseResultJson.Remove("vacancyTitle");
        caseResultJson.Remove("vacancyDescription");
        caseResultJson.Remove("vacancyUrl");
        var path = Path.Combine(Path.GetTempPath(), $"legacy-benchmark-{Guid.NewGuid():N}.json");

        try
        {
            await File.WriteAllTextAsync(
                path,
                resultJson.ToJsonString(BenchmarkJson.SerializerOptions));

            var loaded = await BenchmarkResultStore.LoadAsync(path, CancellationToken.None);
            var loadedCaseResult = Assert.Single(loaded.CaseResults);

            Assert.Equal(string.Empty, loadedCaseResult.VacancyTitle);
            Assert.Equal(string.Empty, loadedCaseResult.VacancyDescription);
            Assert.Null(loadedCaseResult.VacancyUrl);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ResultStore_RejectsInvalidOptionalVacancyUrl()
    {
        var result = CreateRun(
            "dataset-hash",
            "profile-hash",
            "model-a",
            "prompt-v1",
            4,
            "relative/jobs/123");

        var exception = await Assert.ThrowsAsync<BenchmarkValidationException>(() =>
            BenchmarkResultStore.SaveAsync(
                result,
                Path.Combine(Path.GetTempPath(), $"invalid-result-{Guid.NewGuid():N}"),
                CancellationToken.None));

        Assert.Contains("vacancyUrl", exception.Message);
    }

    [Fact]
    public void Comparison_RequiresCompatibleDatasetAndProfileAndReportsPromptDifferences()
    {
        var baseline = CreateRun("dataset-a", "profile-a", "model-a", "prompt-v1", 4);
        var compared = CreateRun("dataset-a", "profile-a", "model-b", "prompt-v2", 5);

        var comparison = BenchmarkComparison.Compare([baseline, compared]);

        Assert.True(comparison.PromptVersionsDiffer);
        Assert.Equal(2, comparison.Runs.Count);
        Assert.Single(comparison.DeltasFromFirstRun);
        Assert.Equal("case-one", Assert.Single(comparison.PredictionDisagreements).CaseId);

        var incompatibleDataset = CreateRun("dataset-b", "profile-a", "model-c", "prompt-v1", 4);
        Assert.Throws<BenchmarkValidationException>(() =>
            BenchmarkComparison.Compare([baseline, incompatibleDataset]));

        var incompatibleProfile = CreateRun("dataset-a", "profile-b", "model-c", "prompt-v1", 4);
        Assert.Throws<BenchmarkValidationException>(() =>
            BenchmarkComparison.Compare([baseline, incompatibleProfile]));
    }

    [Fact]
    public async Task Runner_PropagatesCancellationInsteadOfRecordingFailure()
    {
        using var cancellationSource = new CancellationTokenSource();
        var service = new FakeJobMatchingService((_, _, _) =>
        {
            cancellationSource.Cancel();
            return Task.FromCanceled<JobMatchResult>(cancellationSource.Token);
        });
        var runner = new BenchmarkRunner(service);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runner.RunAsync(
            new BenchmarkDatasetSnapshot(
                new BenchmarkDataset { DatasetVersion = "test-v1", Cases = [CreateCase("case-one")] },
                "dataset-hash"),
            new CandidateProfileSnapshot(CreateProfile(), new string('0', 64)),
            "model-a",
            null,
            cancellationSource.Token));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("https://careers.example.test/jobs/real-role")]
    public async Task Runner_PreservesVacancyContextAndOptionalUrl(string? vacancyUrl)
    {
        JobVacancy? evaluatedVacancy = null;
        var service = new FakeJobMatchingService((vacancy, _, _) =>
        {
            evaluatedVacancy = vacancy;
            return Task.FromResult(new JobMatchResult(
                4,
                ["C#"],
                [],
                [],
                "Good match.",
                "model-a",
                "prompt-v1",
                DateTimeOffset.UtcNow));
        });
        var runner = new BenchmarkRunner(service);

        var result = await runner.RunAsync(
            new BenchmarkDatasetSnapshot(
                new BenchmarkDataset
                {
                    DatasetVersion = "test-v1",
                    Cases = [CreateCase("case-one", vacancyUrl: vacancyUrl)]
                },
                "dataset-hash"),
            new CandidateProfileSnapshot(CreateProfile(), new string('0', 64)),
            "model-a",
            null,
            CancellationToken.None);

        Assert.NotNull(evaluatedVacancy);
        Assert.Equal(vacancyUrl ?? string.Empty, evaluatedVacancy.Url);
        var caseResult = Assert.Single(result.CaseResults);
        Assert.Equal(".NET Engineer", caseResult.VacancyTitle);
        Assert.Equal("Build C# services.", caseResult.VacancyDescription);
        Assert.Equal(vacancyUrl, caseResult.VacancyUrl);
    }

    [Fact]
    public void CommandLine_UsesSelectedModel()
    {
        var options = BenchmarkCommandLine.Parse(["--model", "qwen3:8b"]);

        Assert.Equal("qwen3:8b", options.Model);
    }

    private static BenchmarkCase CreateCase(
        string id,
        ReferenceLabelStatus labelStatus = ReferenceLabelStatus.Provisional,
        string? vacancyUrl = null) => new()
        {
            Id = id,
            VacancyTitle = ".NET Engineer",
            Company = "Synthetic Company",
            Location = "Remote",
            IsRemote = true,
            Description = "Build C# services.",
            VacancyUrl = vacancyUrl,
            ExpectedRating = 4,
            LabelStatus = labelStatus,
            Rationale = "Synthetic test rationale."
        };

    private static BenchmarkCase WithExpectedRating(BenchmarkCase benchmarkCase, int rating) => new()
    {
        Id = benchmarkCase.Id,
        VacancyTitle = benchmarkCase.VacancyTitle,
        Company = benchmarkCase.Company,
        Location = benchmarkCase.Location,
        IsRemote = benchmarkCase.IsRemote,
        Description = benchmarkCase.Description,
        VacancyUrl = benchmarkCase.VacancyUrl,
        ExpectedRating = rating,
        LabelStatus = benchmarkCase.LabelStatus,
        Rationale = benchmarkCase.Rationale
    };

    private static BenchmarkCaseResult CreateResult(
        string caseId,
        int expectedRating,
        int? predictedRating,
        ReferenceLabelStatus labelStatus,
        EvaluationStatus status = EvaluationStatus.Succeeded,
        double latencyMilliseconds = 10,
        string? vacancyUrl = null) => new()
        {
            CaseId = caseId,
            VacancyTitle = ".NET Engineer",
            VacancyDescription = "Build C# services.",
            VacancyUrl = vacancyUrl,
            Tags = [],
            LabelStatus = labelStatus,
            ExpectedRating = expectedRating,
            PredictedRating = predictedRating,
            EvaluationStatus = status,
            ModelName = "model-a",
            PromptVersion = "prompt-v1",
            Explanation = status == EvaluationStatus.Succeeded ? "Explanation" : null,
            MatchedSkills = [],
            MissingRequiredSkills = [],
            MissingOptionalSkills = [],
            LatencyMilliseconds = latencyMilliseconds,
            ErrorMessage = status == EvaluationStatus.Succeeded ? null : "Safe error"
        };

    private static BenchmarkRunResult CreateRun(
        string datasetHash,
        string profileHash,
        string model,
        string promptVersion,
        int rating,
        string? vacancyUrl = null)
    {
        var caseResult = CreateResult(
            "case-one",
            4,
            rating,
            ReferenceLabelStatus.Approved,
            vacancyUrl: vacancyUrl);
        return new BenchmarkRunResult
        {
            SchemaVersion = BenchmarkRunResult.CurrentSchemaVersion,
            RunId = Guid.NewGuid(),
            DatasetVersion = "test-v1",
            DatasetHash = datasetHash,
            BenchmarkProfileHash = profileHash,
            ModelName = model,
            PromptVersion = promptVersion,
            ExecutedAt = DateTimeOffset.UtcNow,
            AggregateMetrics = BenchmarkMetricsCalculator.Calculate([caseResult]),
            CaseResults = [caseResult]
        };
    }

    private static CandidateProfile CreateProfile() => new()
    {
        ProfessionalTitle = "Backend Engineer",
        YearsProfessionalExperience = 5,
        TechnicalSkills = [new CandidateSkill { Name = "C#" }]
    };

    private static string FindSolutionRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AIJobSearchAgent.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate the solution root for benchmark input tests.");
    }

    private sealed class FakeJobMatchingService(
        Func<JobVacancy, CandidateProfile, CancellationToken, Task<JobMatchResult>> evaluate)
        : IJobMatchingService
    {
        public Task<JobMatchResult> EvaluateAsync(
            JobVacancy vacancy,
            CandidateProfile profile,
            CancellationToken cancellationToken) => evaluate(vacancy, profile, cancellationToken);
    }
}
