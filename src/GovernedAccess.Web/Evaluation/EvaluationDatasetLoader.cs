using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using GovernedAccess.Core.Preparations.Contracts;
using GovernedAccess.Web.Ai;
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
	private const string SchemaResourceName = "GovernedAccess.Web.Evaluation.evaluation-dataset.schema.json";

	private static readonly Lazy<JsonSchema> DatasetSchema = new Lazy<JsonSchema>(LoadSchema, LazyThreadSafetyMode.ExecutionAndPublication);

	private static readonly JsonSerializerOptions SerializerOptions = new JsonSerializerOptions
	{
		PropertyNameCaseInsensitive = false,
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
		Converters = { (JsonConverter)new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) }
	};

	internal static string DefaultDatasetPath => Path.Combine(AppContext.BaseDirectory, "Evaluation", "Datasets", "deterministic-intake-v1.json");

	internal static async Task<EvaluationDataset> LoadDefaultAsync(CancellationToken cancellationToken)
	{
		return await LoadAsync(DefaultDatasetPath, cancellationToken);
	}

	internal static async Task<EvaluationDataset> LoadAsync(string path, CancellationToken cancellationToken)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(path);
		EvaluationDataset result;
		await using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan))
		{
			result = await LoadAsync(stream, cancellationToken);
		}
		return result;
	}

	internal static async Task<EvaluationDataset> LoadAsync(Stream stream, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(stream);
		if (!stream.CanRead)
		{
			throw new ArgumentException(
				"The evaluation dataset stream must be readable.",
				nameof(stream));
		}
		try
		{
			using MemoryStream buffer = new MemoryStream();
			await stream.CopyToAsync(buffer, cancellationToken);
			byte[] datasetBytes = buffer.ToArray();
			using JsonDocument document = JsonDocument.Parse(datasetBytes, new JsonDocumentOptions
			{
				AllowTrailingCommas = false,
				CommentHandling = JsonCommentHandling.Disallow,
				MaxDepth = 48
			});
			if (!DatasetSchema.Value.Evaluate(document.RootElement).IsValid)
			{
				throw new EvaluationDatasetException("The evaluation dataset does not satisfy the version 2 schema.");
			}
			EvaluationDataset dataset = (document.RootElement.Deserialize<EvaluationDataset>(SerializerOptions) ?? throw new EvaluationDatasetException("The evaluation dataset could not be deserialized.")) with
			{
				Sha256 = Convert.ToHexString(SHA256.HashData(datasetBytes)).ToLowerInvariant()
			};
			ValidateSemantics(dataset);
			return dataset;
		}
		catch (JsonException innerException)
		{
			throw new EvaluationDatasetException("The evaluation dataset is not valid JSON.", innerException);
		}
	}

	private static JsonSchema LoadSchema()
	{
		using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("GovernedAccess.Web.Evaluation.evaluation-dataset.schema.json") ?? throw new InvalidOperationException("Embedded evaluation schema 'GovernedAccess.Web.Evaluation.evaluation-dataset.schema.json' was not found.");
		using StreamReader reader = new StreamReader(stream);
		return JsonSchema.FromText(reader.ReadToEnd());
	}

	private static void ValidateSemantics(EvaluationDataset dataset)
	{
		if (dataset.SchemaVersion != 2 || dataset.Groups.Count == 0 || HasDuplicates(dataset.Groups.Select((EvaluationGroup group) => group.Id)))
		{
			throw new EvaluationDatasetException("The evaluation dataset has an invalid group inventory.");
		}
		if (!dataset.Groups.Any(static group => group.Promoted)
			|| dataset.Groups.Any(static group =>
				(group.Promoted
					? !group.Id.StartsWith("EVAL-", StringComparison.Ordinal)
					: !group.Id.StartsWith("ADV-", StringComparison.Ordinal))
					|| (group.AbsoluteOutcomeGate && !group.Promoted)))
		{
			throw new EvaluationDatasetException(
				"The evaluation dataset must contain promoted EVAL groups and may use ADV groups only for non-promoted guidance quality.");
		}
		EvaluationVariation[] variations = dataset.Groups.SelectMany((EvaluationGroup group) => group.Variations).ToArray();
		if (variations.Length == 0 || HasDuplicates(variations.Select((EvaluationVariation variation) => variation.Id)))
		{
			throw new EvaluationDatasetException("Evaluation variation identifiers must be globally unique.");
		}
		EvaluationTurn[] turns = variations.SelectMany((EvaluationVariation variation) => variation.Turns).ToArray();
		if (turns.Length == 0 || HasDuplicates(turns.Select((EvaluationTurn turn) => turn.Id)))
		{
			throw new EvaluationDatasetException("Evaluation turn identifiers must be globally unique.");
		}
		EvaluationTurn[] array = turns;
		foreach (EvaluationTurn turn in array)
		{
			ValidateExpectation(turn.Expected);
		}
		foreach (EvaluationVariation variation in variations)
		{
			ValidateExpectation(variation.ExpectedFinal);
		}
	}

	private static void ValidateExpectation(EvaluationInterpretationExpectation expectation)
	{
		bool invalid = !IsValidInterpretation(
			expectation.DialogueAct,
			expectation.DiscussionTopic,
			expectation.Failure,
			expectation.Proposal is not null);
		if (!invalid)
		{
			int maximumToolCalls = expectation.MaximumToolCalls;
			invalid = maximumToolCalls < 0 || maximumToolCalls > 4;
		}
		if (invalid || expectation.AllowedTools.Except<string>(AgentMcpCatalog.ToolNames, StringComparer.Ordinal).Any() || expectation.RequiredTools.Except<string>(expectation.AllowedTools, StringComparer.Ordinal).Any() || HasDuplicates(expectation.AllowedTools) || HasDuplicates(expectation.RequiredTools))
		{
			throw new EvaluationDatasetException("An evaluation turn has an invalid interpretation or tool expectation.");
		}

		if (expectation.AcceptableInterpretations is not { Count: > 0 } acceptable)
		{
			return;
		}

		var primary = new EvaluationInterpretationSnapshot(
			expectation.DialogueAct,
			expectation.DiscussionTopic,
			expectation.Failure);
		if (!acceptable.Contains(primary)
			|| acceptable.Distinct().Count() != acceptable.Count
			|| acceptable.Any(alternative => !IsValidInterpretation(
				alternative.DialogueAct,
				alternative.DiscussionTopic,
				alternative.Failure,
				expectation.Proposal is not null)))
		{
			throw new EvaluationDatasetException(
				"An evaluation turn has invalid acceptable interpretations.");
		}
	}

	private static bool IsValidInterpretation(
		DialogueAct? dialogueAct,
		DiscussionTopic? discussionTopic,
		AgentInterpretationFailure? failure,
		bool hasProposal)
	{
		bool expectsSuccess = dialogueAct.HasValue;
		bool expectsFailure = failure.HasValue;
		return expectsSuccess != expectsFailure
			&& (dialogueAct == DialogueAct.UpdateDraft) == hasProposal
			&& (dialogueAct == DialogueAct.DiscussDraft) == discussionTopic.HasValue;
	}

	private static void ValidateExpectation(EvaluationCanonicalExpectation expectation)
	{
		if (expectation.AcceptableOutcomes is not { Count: > 0 } acceptable)
		{
			return;
		}

		if (!acceptable.Contains(expectation.Outcome)
			|| acceptable.Distinct().Count() != acceptable.Count)
		{
			throw new EvaluationDatasetException(
				"An evaluation variation has invalid acceptable outcomes.");
		}
	}

	private static bool HasDuplicates(IEnumerable<string> values)
	{
		HashSet<string> observed = new HashSet<string>(StringComparer.Ordinal);
		return values.Any((string value) => !observed.Add(value));
	}
}
