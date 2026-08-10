using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Json.Schema;

namespace GovernedAccess.Web.Evaluation;

internal sealed class EvaluationDatasetException : Exception
{
    internal EvaluationDatasetException(string message)
        : base(message)
    {
    }

    internal EvaluationDatasetException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

internal static class EvaluationDatasetLoader
{
    private const string SchemaResourceName =
        "GovernedAccess.Web.Evaluation.evaluation-dataset.schema.json";

    private static readonly Lazy<JsonSchema> DatasetSchema = new(
        LoadSchema,
        LazyThreadSafetyMode.ExecutionAndPublication);

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters =
        {
            new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false),
        },
    };

    internal static string DefaultDatasetPath => Path.Combine(
        AppContext.BaseDirectory,
        "Evaluation",
        "Datasets",
        "intake-v1.json");

    internal static Task<EvaluationDataset> LoadDefaultAsync(
        CancellationToken cancellationToken) =>
        LoadAsync(DefaultDatasetPath, cancellationToken);

    internal static async Task<EvaluationDataset> LoadAsync(
        string path,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        return await LoadAsync(stream, cancellationToken);
    }

    internal static async Task<EvaluationDataset> LoadAsync(
        Stream stream,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanRead)
        {
            throw new ArgumentException("The dataset stream must be readable.", nameof(stream));
        }

        try
        {
            using var document = await JsonDocument.ParseAsync(
                stream,
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow,
                    MaxDepth = 32,
                },
                cancellationToken);

            if (!DatasetSchema.Value.Evaluate(document.RootElement).IsValid)
            {
                throw new EvaluationDatasetException(
                    "The evaluation dataset does not satisfy the version 1 schema.");
            }

            var dataset = document.RootElement.Deserialize<DatasetDto>(SerializerOptions)
                ?? throw new EvaluationDatasetException(
                    "The evaluation dataset could not be deserialized.");

            return Map(dataset);
        }
        catch (JsonException exception)
        {
            throw new EvaluationDatasetException(
                "The evaluation dataset is not valid JSON.",
                exception);
        }
    }

    private static JsonSchema LoadSchema()
    {
        using var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream(SchemaResourceName)
            ?? throw new InvalidOperationException(
                $"Embedded evaluation schema '{SchemaResourceName}' was not found.");
        using var reader = new StreamReader(stream);
        return JsonSchema.FromText(reader.ReadToEnd());
    }

    private static EvaluationDataset Map(DatasetDto dataset) =>
        new(
            dataset.SchemaVersion,
            dataset.DatasetVersion,
            Array.AsReadOnly(dataset.Scenarios.Select(Map).ToArray()));

    private static EvaluationScenario Map(ScenarioDto scenario) =>
        new(
            scenario.Id,
            scenario.Category,
            scenario.StartingCandidate is null
                ? null
                : new EvaluationCandidateSetup(
                    scenario.StartingCandidate.ClientId,
                    scenario.StartingCandidate.EnvironmentId,
                    scenario.StartingCandidate.RequestedRoleId,
                    scenario.StartingCandidate.Justification,
                    scenario.StartingCandidate.IncidentId),
            Array.AsReadOnly(
                scenario.Turns
                    .Select(static turn => new EvaluationTurn(turn.Id, turn.RequesterMessage))
                    .ToArray()),
            Map(scenario.Expected));

    private static FinalExpectation Map(ExpectationDto expectation) =>
        new(
            expectation.Outcome,
            expectation.Candidate is null ? null : Map(expectation.Candidate),
            MapNullableEnum<EvaluationClarificationTarget>(expectation.ClarificationTarget),
            MapStringList(expectation.EnvironmentOptionIds),
            MapStringList(expectation.ValidationCodes),
            expectation.PreservedFields ?? [],
            expectation.ClearedFields ?? []);

    private static EvaluationCandidateExpectation Map(CandidateExpectationDto candidate) =>
        new(
            MapNullableReference<string>(candidate.ClientId),
            MapNullableReference<string>(candidate.EnvironmentId),
            MapNullableReference<string>(candidate.RequestedRoleId),
            MapBoolean(candidate.HasJustification),
            MapNullableReference<string>(candidate.IncidentId));

    private static EvaluationExpectedValue<T?> MapNullableReference<T>(JsonElement element)
        where T : class
    {
        if (element.ValueKind == JsonValueKind.Undefined)
        {
            return default;
        }

        var value = element.ValueKind == JsonValueKind.Null
            ? default
            : element.Deserialize<T>(SerializerOptions);
        return EvaluationExpectedValue<T?>.Declared(value);
    }

    private static EvaluationExpectedValue<T?> MapNullableEnum<T>(JsonElement element)
        where T : struct
    {
        if (element.ValueKind == JsonValueKind.Undefined)
        {
            return default;
        }

        T? value = element.ValueKind == JsonValueKind.Null
            ? null
            : element.Deserialize<T>(SerializerOptions);
        return EvaluationExpectedValue<T?>.Declared(value);
    }

    private static EvaluationExpectedValue<bool> MapBoolean(JsonElement element) =>
        element.ValueKind == JsonValueKind.Undefined
            ? default
            : EvaluationExpectedValue<bool>.Declared(element.GetBoolean());

    private static EvaluationExpectedValue<IReadOnlyList<string>> MapStringList(
        JsonElement element) =>
        element.ValueKind == JsonValueKind.Undefined
            ? default
            : EvaluationExpectedValue<IReadOnlyList<string>>.Declared(
                Array.AsReadOnly(element.Deserialize<string[]>(SerializerOptions) ?? []));

    private sealed record DatasetDto(
        int SchemaVersion,
        string DatasetVersion,
        IReadOnlyList<ScenarioDto> Scenarios);

    private sealed record ScenarioDto(
        string Id,
        EvaluationCategory Category,
        CandidateSetupDto? StartingCandidate,
        IReadOnlyList<TurnDto> Turns,
        ExpectationDto Expected);

    private sealed record CandidateSetupDto(
        string? ClientId,
        string? EnvironmentId,
        string? RequestedRoleId,
        string? Justification,
        string? IncidentId);

    private sealed record TurnDto(string Id, string RequesterMessage);

    private sealed record ExpectationDto(
        NormalizedIntakeOutcome Outcome,
        CandidateExpectationDto? Candidate,
        JsonElement ClarificationTarget,
        JsonElement EnvironmentOptionIds,
        JsonElement ValidationCodes,
        IReadOnlyList<EvaluationCandidateField>? PreservedFields,
        IReadOnlyList<EvaluationCandidateField>? ClearedFields);

    private sealed record CandidateExpectationDto(
        JsonElement ClientId,
        JsonElement EnvironmentId,
        JsonElement RequestedRoleId,
        JsonElement HasJustification,
        JsonElement IncidentId);
}
