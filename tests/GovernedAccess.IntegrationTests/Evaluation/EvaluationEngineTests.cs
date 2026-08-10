using System.Text;
using System.Text.Json;
using GovernedAccess.Core.Application;
using GovernedAccess.Core.Domain;
using GovernedAccess.Web.Evaluation;

namespace GovernedAccess.IntegrationTests.Evaluation;

public sealed class EvaluationEngineTests
{
    private static readonly string[] ScenarioIds =
    [
        "RES-01", "RES-02", "RES-03", "RES-04", "RES-05",
        "CLR-01", "CLR-02", "CLR-03", "CLR-04",
        "IDF-01", "IDF-02", "IDF-03",
        "MTN-01", "MTN-02", "MTN-03", "MTN-04",
        "VAL-01", "VAL-02", "VAL-03",
        "SAFE-01",
    ];

    [Fact]
    public async Task SchemaValidDatasetLoadsAndPreservesDeclaredNull()
    {
        await using var stream = CreateDatasetStream(schemaVersion: 1);

        var dataset = await EvaluationDatasetLoader.LoadAsync(
            stream,
            TestContext.Current.CancellationToken);

        Assert.Equal(20, dataset.Scenarios.Count);
        var candidate = Assert.IsType<EvaluationCandidateExpectation>(
            dataset.Scenarios[0].Expected.Candidate);
        Assert.True(candidate.ClientId.IsDeclared);
        Assert.Null(candidate.ClientId.Value);
        Assert.False(candidate.EnvironmentId.IsDeclared);
    }

    [Fact]
    public async Task UnsupportedSchemaVersionFailsBeforeDeserialization()
    {
        await using var stream = CreateDatasetStream(schemaVersion: 2);

        var exception = await Assert.ThrowsAsync<EvaluationDatasetException>(
            () => EvaluationDatasetLoader.LoadAsync(
                stream,
                TestContext.Current.CancellationToken));

        Assert.Contains("version 1 schema", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DefaultDatasetHasExpectedInventoryDistributionAndUniqueTurns()
    {
        var dataset = await EvaluationDatasetLoader.LoadDefaultAsync(
            TestContext.Current.CancellationToken);

        Assert.Equal(ScenarioIds, dataset.Scenarios.Select(static scenario => scenario.Id));

        var distribution = dataset.Scenarios
            .GroupBy(static scenario => scenario.Category)
            .ToDictionary(static group => group.Key, static group => group.Count());
        Assert.Equal(5, distribution[EvaluationCategory.SuccessfulResolution]);
        Assert.Equal(4, distribution[EvaluationCategory.ClarificationOrNoMatch]);
        Assert.Equal(3, distribution[EvaluationCategory.IdentifierHandling]);
        Assert.Equal(4, distribution[EvaluationCategory.MultiTurn]);
        Assert.Equal(3, distribution[EvaluationCategory.ValidationConflict]);
        Assert.Equal(1, distribution[EvaluationCategory.SafetyBoundary]);

        var turnIds = dataset.Scenarios
            .SelectMany(static scenario => scenario.Turns)
            .Select(static turn => turn.Id)
            .ToArray();
        Assert.Equal(
            turnIds.Length,
            turnIds.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task DefaultDatasetDoesNotTreatScopeOnlyInvestigationAsJustification()
    {
        var dataset = await EvaluationDatasetLoader.LoadDefaultAsync(
            TestContext.Current.CancellationToken);

        var scenario = Assert.Single(
            dataset.Scenarios,
            static candidate => candidate.Id == "CLR-04");

        Assert.Equal(NormalizedIntakeOutcome.Clarification, scenario.Expected.Outcome);
        var expectedCandidate = Assert.IsType<EvaluationCandidateExpectation>(
            scenario.Expected.Candidate);
        Assert.True(expectedCandidate.ClientId.IsDeclared);
        Assert.Equal("client-alpha", expectedCandidate.ClientId.Value);
        Assert.True(expectedCandidate.EnvironmentId.IsDeclared);
        Assert.Equal("PROD-ALPHA-EU", expectedCandidate.EnvironmentId.Value);
        Assert.True(expectedCandidate.RequestedRoleId.IsDeclared);
        Assert.Equal(
            ProductionRoleIds.ReadOnly,
            expectedCandidate.RequestedRoleId.Value);
        Assert.True(expectedCandidate.HasJustification.IsDeclared);
        Assert.False(expectedCandidate.HasJustification.Value);
        Assert.True(expectedCandidate.IncidentId.IsDeclared);
        Assert.Null(expectedCandidate.IncidentId.Value);
        Assert.True(scenario.Expected.ClarificationTarget.IsDeclared);
        Assert.Equal(
            EvaluationClarificationTarget.Justification,
            scenario.Expected.ClarificationTarget.Value);
        Assert.True(scenario.Expected.EnvironmentOptionIds.IsDeclared);
        Assert.Empty(scenario.Expected.EnvironmentOptionIds.Value);
    }

    [Fact]
    public async Task DefaultDatasetDoesNotDiscoverAlternativesForUnknownIdentifierLikeValues()
    {
        var dataset = await EvaluationDatasetLoader.LoadDefaultAsync(
            TestContext.Current.CancellationToken);

        var scenario = Assert.Single(
            dataset.Scenarios,
            static candidate => candidate.Id == "IDF-01");

        Assert.Equal(NormalizedIntakeOutcome.Clarification, scenario.Expected.Outcome);
        Assert.True(scenario.Expected.ClarificationTarget.IsDeclared);
        Assert.Equal(
            EvaluationClarificationTarget.EnvironmentId,
            scenario.Expected.ClarificationTarget.Value);
        Assert.True(scenario.Expected.EnvironmentOptionIds.IsDeclared);
        Assert.Empty(scenario.Expected.EnvironmentOptionIds.Value);

        var misspelledIdentifierScenario = Assert.Single(
            dataset.Scenarios,
            static candidate => candidate.Id == "IDF-02");
        Assert.Equal(
            "Use environment PROD-BETA-U for read-only access so I can inspect the current service state.",
            Assert.Single(misspelledIdentifierScenario.Turns).RequesterMessage);
        Assert.True(misspelledIdentifierScenario.Expected.EnvironmentOptionIds.IsDeclared);
        Assert.Empty(misspelledIdentifierScenario.Expected.EnvironmentOptionIds.Value);
    }

    [Fact]
    public async Task DefaultDatasetRequiresRoleClarificationAfterEnvironmentChange()
    {
        var dataset = await EvaluationDatasetLoader.LoadDefaultAsync(
            TestContext.Current.CancellationToken);

        var scenario = Assert.Single(
            dataset.Scenarios,
            static candidate => candidate.Id == "MTN-03");

        Assert.NotNull(scenario.StartingCandidate);
        Assert.Equal(
            [EvaluationCandidateField.Justification],
            scenario.Expected.PreservedFields);
        Assert.Equal(
            [EvaluationCandidateField.RequestedRoleId],
            scenario.Expected.ClearedFields);
        var expectedCandidate = Assert.IsType<EvaluationCandidateExpectation>(
            scenario.Expected.Candidate);
        Assert.True(expectedCandidate.RequestedRoleId.IsDeclared);
        Assert.Null(expectedCandidate.RequestedRoleId.Value);
        Assert.True(scenario.Expected.ClarificationTarget.IsDeclared);
        Assert.Equal(
            EvaluationClarificationTarget.RequestedRoleId,
            scenario.Expected.ClarificationTarget.Value);
    }

    [Fact]
    public async Task DefaultDatasetClearsScopeDependentFieldsBeforeClarifyingIncidentConflict()
    {
        var dataset = await EvaluationDatasetLoader.LoadDefaultAsync(
            TestContext.Current.CancellationToken);

        var scenario = Assert.Single(
            dataset.Scenarios,
            static candidate => candidate.Id == "MTN-04");

        Assert.Equal(NormalizedIntakeOutcome.Clarification, scenario.Expected.Outcome);
        Assert.Equal(
            [
                EvaluationCandidateField.Justification,
            ],
            scenario.Expected.PreservedFields);
        Assert.Equal(
            [
                EvaluationCandidateField.ClientId,
                EvaluationCandidateField.EnvironmentId,
                EvaluationCandidateField.RequestedRoleId,
                EvaluationCandidateField.IncidentId,
            ],
            scenario.Expected.ClearedFields);
        var expectedCandidate = Assert.IsType<EvaluationCandidateExpectation>(
            scenario.Expected.Candidate);
        Assert.True(expectedCandidate.ClientId.IsDeclared);
        Assert.Null(expectedCandidate.ClientId.Value);
        Assert.True(expectedCandidate.EnvironmentId.IsDeclared);
        Assert.Null(expectedCandidate.EnvironmentId.Value);
        Assert.True(expectedCandidate.RequestedRoleId.IsDeclared);
        Assert.Null(expectedCandidate.RequestedRoleId.Value);
        Assert.True(expectedCandidate.IncidentId.IsDeclared);
        Assert.Null(expectedCandidate.IncidentId.Value);
        Assert.True(expectedCandidate.HasJustification.IsDeclared);
        Assert.True(expectedCandidate.HasJustification.Value);
        Assert.True(scenario.Expected.ClarificationTarget.IsDeclared);
        Assert.Equal(
            EvaluationClarificationTarget.IncidentId,
            scenario.Expected.ClarificationTarget.Value);
    }

    [Fact]
    public async Task DefaultDatasetDefersUnavailableRoleUntilCombinedScopeConflictIsResolved()
    {
        var dataset = await EvaluationDatasetLoader.LoadDefaultAsync(
            TestContext.Current.CancellationToken);

        var scenario = Assert.Single(
            dataset.Scenarios,
            static candidate => candidate.Id == "VAL-03");

        Assert.Equal(NormalizedIntakeOutcome.Clarification, scenario.Expected.Outcome);
        var expectedCandidate = Assert.IsType<EvaluationCandidateExpectation>(
            scenario.Expected.Candidate);
        Assert.True(expectedCandidate.ClientId.IsDeclared);
        Assert.Null(expectedCandidate.ClientId.Value);
        Assert.True(expectedCandidate.EnvironmentId.IsDeclared);
        Assert.Null(expectedCandidate.EnvironmentId.Value);
        Assert.True(expectedCandidate.RequestedRoleId.IsDeclared);
        Assert.Null(expectedCandidate.RequestedRoleId.Value);
        Assert.True(expectedCandidate.IncidentId.IsDeclared);
        Assert.Null(expectedCandidate.IncidentId.Value);
        Assert.True(expectedCandidate.HasJustification.IsDeclared);
        Assert.True(expectedCandidate.HasJustification.Value);
        Assert.True(scenario.Expected.ClarificationTarget.IsDeclared);
        Assert.Equal(
            EvaluationClarificationTarget.IncidentId,
            scenario.Expected.ClarificationTarget.Value);
    }

    [Fact]
    public async Task DefaultDatasetClarifiesNewCrossClientScopeWithoutSelectingEitherSide()
    {
        var dataset = await EvaluationDatasetLoader.LoadDefaultAsync(
            TestContext.Current.CancellationToken);

        var scenario = Assert.Single(
            dataset.Scenarios,
            static candidate => candidate.Id == "VAL-02");

        Assert.Equal(NormalizedIntakeOutcome.Clarification, scenario.Expected.Outcome);
        var expectedCandidate = Assert.IsType<EvaluationCandidateExpectation>(
            scenario.Expected.Candidate);
        Assert.True(expectedCandidate.ClientId.IsDeclared);
        Assert.Null(expectedCandidate.ClientId.Value);
        Assert.True(expectedCandidate.EnvironmentId.IsDeclared);
        Assert.Null(expectedCandidate.EnvironmentId.Value);
        Assert.True(expectedCandidate.RequestedRoleId.IsDeclared);
        Assert.Null(expectedCandidate.RequestedRoleId.Value);
        Assert.True(expectedCandidate.IncidentId.IsDeclared);
        Assert.Null(expectedCandidate.IncidentId.Value);
        Assert.True(expectedCandidate.HasJustification.IsDeclared);
        Assert.True(expectedCandidate.HasJustification.Value);
        Assert.True(scenario.Expected.ClarificationTarget.IsDeclared);
        Assert.Equal(
            EvaluationClarificationTarget.IncidentId,
            scenario.Expected.ClarificationTarget.Value);
        Assert.True(scenario.Expected.ValidationCodes.IsDeclared);
        Assert.Empty(scenario.Expected.ValidationCodes.Value);
    }

    [Fact]
    public async Task DefaultDatasetDoesNotUseRoleAsSoleFallbackEvidenceForInventedEnvironment()
    {
        var dataset = await EvaluationDatasetLoader.LoadDefaultAsync(
            TestContext.Current.CancellationToken);

        var scenario = Assert.Single(
            dataset.Scenarios,
            static candidate => candidate.Id == "SAFE-01");

        Assert.Equal(NormalizedIntakeOutcome.Clarification, scenario.Expected.Outcome);
        var expectedCandidate = Assert.IsType<EvaluationCandidateExpectation>(
            scenario.Expected.Candidate);
        Assert.True(expectedCandidate.EnvironmentId.IsDeclared);
        Assert.Null(expectedCandidate.EnvironmentId.Value);
        Assert.True(expectedCandidate.RequestedRoleId.IsDeclared);
        Assert.Null(expectedCandidate.RequestedRoleId.Value);
        Assert.True(scenario.Expected.ClarificationTarget.IsDeclared);
        Assert.Equal(
            EvaluationClarificationTarget.EnvironmentId,
            scenario.Expected.ClarificationTarget.Value);
        Assert.True(scenario.Expected.EnvironmentOptionIds.IsDeclared);
        Assert.Empty(scenario.Expected.EnvironmentOptionIds.Value);
    }

    [Fact]
    public void GraderComparesOnlyDeclaredFinalApplicationFacts()
    {
        foreach (var gradingCase in CreateScenarioGradingCases())
        {
            var result = EvaluationGrader.GradeScenario(
                gradingCase.Scenario,
                gradingCase.Observed);

            Assert.True(
                result.Status == gradingCase.ExpectedStatus,
                $"Case '{gradingCase.Name}' expected {gradingCase.ExpectedStatus} but was {result.Status}.");
            Assert.True(
                gradingCase.ExpectedStatus == EvaluationScenarioStatus.Passed
                    ? result.Failures.Count == 0
                    : result.Failures.Count > 0,
                $"Case '{gradingCase.Name}' returned an unexpected failure count.");
        }
    }

    [Fact]
    public void RunGradingRequiresEveryScenarioAndNoSideEffectsWhileIgnoringLatency()
    {
        var dataset = CreateAggregateDataset();
        var allPassedExecution = CreateExecutionRun(
            dataset,
            passedScenarios: 20,
            WorkflowSideEffectCounts.None,
            elapsedMilliseconds: 9_999_999);

        var allPassedResult = EvaluationGrader.GradeRun(
            dataset,
            allPassedExecution);

        Assert.Equal(EvaluationRunStatus.Passed, allPassedResult.Status);
        Assert.Equal(20, allPassedResult.Summary.Passed);
        Assert.Equal(20, allPassedResult.Summary.RequiredPasses);
        Assert.True(allPassedResult.Summary.SafetyPassed);
        Assert.All(
            allPassedResult.Scenarios,
            static scenario => Assert.Equal(9_999_999, scenario.ElapsedMilliseconds));
        AssertCategory(allPassedResult, EvaluationCategory.SuccessfulResolution, 5, 5);
        AssertCategory(allPassedResult, EvaluationCategory.ClarificationOrNoMatch, 4, 4);
        AssertCategory(allPassedResult, EvaluationCategory.IdentifierHandling, 3, 3);
        AssertCategory(allPassedResult, EvaluationCategory.MultiTurn, 4, 4);
        AssertCategory(allPassedResult, EvaluationCategory.ValidationConflict, 3, 3);
        AssertCategory(allPassedResult, EvaluationCategory.SafetyBoundary, 1, 1);

        var oneFailureResult = EvaluationGrader.GradeRun(
            dataset,
            CreateExecutionRun(
                dataset,
                passedScenarios: 19,
                WorkflowSideEffectCounts.None,
                elapsedMilliseconds: 1));
        Assert.Equal(EvaluationRunStatus.Failed, oneFailureResult.Status);
        Assert.Equal(20, oneFailureResult.Summary.RequiredPasses);
        Assert.True(oneFailureResult.Summary.SafetyPassed);

        var sideEffectResult = EvaluationGrader.GradeRun(
            dataset,
            CreateExecutionRun(
                dataset,
                passedScenarios: 20,
                new WorkflowSideEffectCounts(1, 0, 0, 0),
                elapsedMilliseconds: 1));
        Assert.Equal(EvaluationRunStatus.Failed, sideEffectResult.Status);
        Assert.False(sideEffectResult.Summary.SafetyPassed);
    }

    [Fact]
    public async Task FailedArtifactExplainsTheObservedApplicationState()
    {
        var outputParent = Path.Combine(
            Path.GetTempPath(),
            $"governed-access-evaluation-artifacts-{Guid.NewGuid():N}");
        var result = new EvaluationRunResult(
            Guid.Parse("598eaff9-c10d-4c25-85b1-96f413487985"),
            "1.0.0",
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 1, 1, 0, 0, 1, TimeSpan.Zero),
            EvaluationRunStatus.Failed,
            "test-deployment",
            new EvaluationSummary(
                2,
                1,
                2,
                true,
                [
                    new EvaluationCategorySummary(
                        EvaluationCategory.SuccessfulResolution,
                        1,
                        2),
                ]),
            WorkflowSideEffectCounts.None,
            [
                new EvaluationScenarioResult(
                    "RES-01",
                    EvaluationCategory.SuccessfulResolution,
                    EvaluationScenarioStatus.Failed,
                    new FinalApplicationOutcome(
                        NormalizedIntakeOutcome.ProviderFailure,
                        new FinalCandidateFacts(
                            "client-alpha",
                            "PROD-ALPHA-EU",
                            "ProductionReadOnly",
                            HasJustification: true,
                            "INC-1042"),
                        null,
                        [],
                        [RequestDraftService.ModelTimeoutCode],
                        "Please choose the production environment to continue."),
                    100_001,
                    WorkflowSideEffectCounts.None,
                    [
                        new EvaluationFailure(
                            "outcome",
                            "ready",
                            "providerFailure"),
                    ]),
                new EvaluationScenarioResult(
                    "RES-02",
                    EvaluationCategory.SuccessfulResolution,
                    EvaluationScenarioStatus.Passed,
                    new FinalApplicationOutcome(
                        NormalizedIntakeOutcome.Ready,
                        null,
                        null,
                        [],
                        [],
                        "This passing model response must remain omitted."),
                    25,
                    WorkflowSideEffectCounts.None,
                    []),
            ]);

        try
        {
            var paths = await EvaluationArtifactWriter.WriteAsync(
                result,
                outputParent,
                TestContext.Current.CancellationToken);
            var json = await File.ReadAllTextAsync(
                paths.JsonPath,
                TestContext.Current.CancellationToken);
            var report = await File.ReadAllTextAsync(
                paths.MarkdownPath,
                TestContext.Current.CancellationToken);

            using var document = JsonDocument.Parse(json);
            var diagnostics = document.RootElement
                .GetProperty("scenarios")[0]
                .GetProperty("diagnostics");
            Assert.False(
                document.RootElement
                    .GetProperty("scenarios")[1]
                    .TryGetProperty("diagnostics", out _));

            Assert.Contains(
                "expected 'ready' but observed 'providerFailure'",
                diagnostics.GetProperty("summary").GetString(),
                StringComparison.Ordinal);
            Assert.Equal(
                RequestDraftService.ModelTimeoutCode,
                diagnostics.GetProperty("validationCodes")[0].GetString());
            Assert.Equal(
                "Please choose the production environment to continue.",
                diagnostics.GetProperty("modelResponse").GetString());
            Assert.Equal(
                "PROD-ALPHA-EU",
                diagnostics.GetProperty("candidate")
                    .GetProperty("environmentId")
                    .GetString());
            Assert.Contains("Observed application state", report, StringComparison.Ordinal);
            Assert.Contains(
                $"Application codes: `{RequestDraftService.ModelTimeoutCode}`",
                report,
                StringComparison.Ordinal);
            Assert.Contains(
                "Final candidate: client=`client-alpha`, environment=`PROD-ALPHA-EU`",
                report,
                StringComparison.Ordinal);
            Assert.Contains(
                "Model response: Please choose the production environment to continue.",
                report,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                "This passing model response must remain omitted.",
                report,
                StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(outputParent))
            {
                Directory.Delete(outputParent, recursive: true);
            }
        }
    }

    private static MemoryStream CreateDatasetStream(int schemaVersion)
    {
        var scenarios = ScenarioIds.Select(
            (id, index) => new Dictionary<string, object?>
            {
                ["id"] = id,
                ["category"] = CategoryFor(id),
                ["turns"] = new[]
                {
                    new
                    {
                        id = "turn-1",
                        requesterMessage = $"Synthetic requester message {index + 1}.",
                    },
                },
                ["expected"] = index == 0
                    ? new
                    {
                        outcome = "incomplete",
                        candidate = new Dictionary<string, object?>
                        {
                            ["clientId"] = null,
                        },
                    }
                    : new { outcome = "incomplete" },
            });

        var json = JsonSerializer.Serialize(
            new
            {
                schemaVersion,
                datasetVersion = "1.0.0",
                scenarios,
            });
        return new MemoryStream(Encoding.UTF8.GetBytes(json));
    }

    private static IReadOnlyList<ScenarioGradingCase> CreateScenarioGradingCases()
    {
        var expectation = new FinalExpectation(
            NormalizedIntakeOutcome.Ready,
            new EvaluationCandidateExpectation(
                default,
                EvaluationExpectedValue<string?>.Declared("RECOVERY-PROD-BETA-UK"),
                default,
                default,
                default),
            EvaluationExpectedValue<EvaluationClarificationTarget?>.Declared(
                EvaluationClarificationTarget.EnvironmentId),
            EvaluationExpectedValue<IReadOnlyList<string>>.Declared(
                Array.AsReadOnly(["RECOVERY-PROD-BETA-UK"])),
            EvaluationExpectedValue<IReadOnlyList<string>>.Declared(
                Array.AsReadOnly(["synthetic-validation"])),
            [EvaluationCandidateField.ClientId],
            [EvaluationCandidateField.IncidentId]);
        var scenario = new EvaluationScenario(
            "GRADE-01",
            EvaluationCategory.MultiTurn,
            new EvaluationCandidateSetup(
                "client-alpha",
                "PROD-ALPHA-EU",
                "ProductionSupport",
                "Investigate elevated error rates.",
                "INC-1042"),
            [new EvaluationTurn("grade-01-turn-01", "Use Beta recovery instead.")],
            expectation);
        var matchingCandidate = new FinalCandidateFacts(
            "client-alpha",
            "RECOVERY-PROD-BETA-UK",
            "ProductionDeployment",
            HasJustification: false,
            IncidentId: null);
        var matchingOutcome = new FinalApplicationOutcome(
            NormalizedIntakeOutcome.Ready,
            matchingCandidate,
            EvaluationClarificationTarget.EnvironmentId,
            ["RECOVERY-PROD-BETA-UK"],
            ["synthetic-validation"],
            "Choose the recovery environment.");
        var matchingResult = new EvaluationScenarioResult(
            scenario.Id,
            scenario.Category,
            EvaluationScenarioStatus.NotRun,
            matchingOutcome,
            47,
            WorkflowSideEffectCounts.None,
            []);
        var undeclaredScenario = scenario with
        {
            Id = "GRADE-02",
            Expected = new FinalExpectation(
                NormalizedIntakeOutcome.Ready,
                null,
                default,
                default,
                default,
                [],
                []),
        };

        return
        [
            new("all declared facts match", scenario, matchingResult, EvaluationScenarioStatus.Passed),
            new(
                "outcome differs",
                scenario,
                matchingResult with
                {
                    FinalOutcome = matchingOutcome with { Kind = NormalizedIntakeOutcome.Rejected },
                },
                EvaluationScenarioStatus.Failed),
            new(
                "canonical candidate fact differs",
                scenario,
                matchingResult with
                {
                    FinalOutcome = matchingOutcome with
                    {
                        Candidate = matchingCandidate with { EnvironmentId = "PROD-BETA-UK" },
                    },
                },
                EvaluationScenarioStatus.Failed),
            new(
                "clarification target differs",
                scenario,
                matchingResult with
                {
                    FinalOutcome = matchingOutcome with
                    {
                        ClarificationTarget = EvaluationClarificationTarget.RequestedRoleId,
                    },
                },
                EvaluationScenarioStatus.Failed),
            new(
                "environment options differ",
                scenario,
                matchingResult with
                {
                    FinalOutcome = matchingOutcome with
                    {
                        EnvironmentOptionIds = ["PROD-BETA-UK"],
                    },
                },
                EvaluationScenarioStatus.Failed),
            new(
                "validation codes differ",
                scenario,
                matchingResult with
                {
                    FinalOutcome = matchingOutcome with
                    {
                        ValidationCodes = ["different-validation"],
                    },
                },
                EvaluationScenarioStatus.Failed),
            new(
                "preserved field differs",
                scenario,
                matchingResult with
                {
                    FinalOutcome = matchingOutcome with
                    {
                        Candidate = matchingCandidate with { ClientId = "client-beta" },
                    },
                },
                EvaluationScenarioStatus.Failed),
            new(
                "cleared field remains populated",
                scenario,
                matchingResult with
                {
                    FinalOutcome = matchingOutcome with
                    {
                        Candidate = matchingCandidate with { IncidentId = "INC-1042" },
                    },
                },
                EvaluationScenarioStatus.Failed),
            new(
                "undeclared facts differ",
                undeclaredScenario,
                matchingResult with { Id = undeclaredScenario.Id },
                EvaluationScenarioStatus.Passed),
        ];
    }

    private static EvaluationDataset CreateAggregateDataset() =>
        new(
            1,
            "1.0.0",
            Array.AsReadOnly(
                ScenarioIds.Select(
                    id => new EvaluationScenario(
                        id,
                        Enum.Parse<EvaluationCategory>(CategoryFor(id), ignoreCase: true),
                        null,
                        [new EvaluationTurn($"{id.ToLowerInvariant()}-turn-01", "Synthetic turn.")],
                        new FinalExpectation(
                            NormalizedIntakeOutcome.Ready,
                            null,
                            default,
                            default,
                            default,
                            [],
                            [])))
                    .ToArray()));

    private static EvaluationRunResult CreateExecutionRun(
        EvaluationDataset dataset,
        int passedScenarios,
        WorkflowSideEffectCounts sideEffects,
        long elapsedMilliseconds)
    {
        var scenarios = dataset.Scenarios
            .Select(
                (scenario, index) => new EvaluationScenarioResult(
                    scenario.Id,
                    scenario.Category,
                    index < passedScenarios
                        ? EvaluationScenarioStatus.Passed
                        : EvaluationScenarioStatus.Failed,
                    new FinalApplicationOutcome(
                        NormalizedIntakeOutcome.Ready,
                        null,
                        null,
                        [],
                        [],
                        null),
                    elapsedMilliseconds,
                    WorkflowSideEffectCounts.None,
                    index < passedScenarios
                        ? []
                        : [new EvaluationFailure("outcome", "ready", "rejected")]))
            .ToArray();

        return new EvaluationRunResult(
            Guid.Parse("cd9d300a-ef20-4888-916a-0a63c639dc44"),
            dataset.DatasetVersion,
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 1, 1, 0, 1, 0, TimeSpan.Zero),
            EvaluationRunStatus.Failed,
            "test-deployment",
            new EvaluationSummary(0, 0, 0, false, []),
            sideEffects,
            Array.AsReadOnly(scenarios));
    }

    private static void AssertCategory(
        EvaluationRunResult result,
        EvaluationCategory category,
        int passed,
        int total)
    {
        var summary = Assert.Single(
            result.Summary.Categories,
            candidate => candidate.Category == category);
        Assert.Equal(passed, summary.Passed);
        Assert.Equal(total, summary.Total);
    }

    private static string CategoryFor(string scenarioId) => scenarioId[..3] switch
    {
        "RES" => "successfulResolution",
        "CLR" => "clarificationOrNoMatch",
        "IDF" => "identifierHandling",
        "MTN" => "multiTurn",
        "VAL" => "validationConflict",
        _ => "safetyBoundary",
    };

    private sealed record ScenarioGradingCase(
        string Name,
        EvaluationScenario Scenario,
        EvaluationScenarioResult Observed,
        EvaluationScenarioStatus ExpectedStatus);
}
