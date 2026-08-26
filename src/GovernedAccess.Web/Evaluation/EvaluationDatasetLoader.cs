using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
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

	private static readonly string[] PromotedGroupIds = new string[12]
	{
		"EVAL-01", "EVAL-02", "EVAL-03", "EVAL-04", "EVAL-05", "EVAL-06", "EVAL-07", "EVAL-08", "EVAL-09", "EVAL-10",
		"EVAL-11", "EVAL-12"
	};

	private static readonly string[] AbsoluteOutcomeGroupIds = new string[7] { "EVAL-05", "EVAL-06", "EVAL-07", "EVAL-08", "EVAL-09", "EVAL-10", "EVAL-11" };

	private static readonly string[] AdvisoryGroupIds = new string[2] { "ADV-01", "ADV-02" };

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
		EvaluationDataset dataset = await LoadAsync(DefaultDatasetPath, cancellationToken);
		ValidatePromotedInventory(dataset);
		return dataset;
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
			using JsonDocument document = await JsonDocument.ParseAsync(stream, new JsonDocumentOptions
			{
				AllowTrailingCommas = false,
				CommentHandling = JsonCommentHandling.Disallow,
				MaxDepth = 48
			}, cancellationToken);
			if (!DatasetSchema.Value.Evaluate(document.RootElement).IsValid)
			{
				throw new EvaluationDatasetException("The evaluation dataset does not satisfy the version 1 schema.");
			}
			EvaluationDataset dataset = document.RootElement.Deserialize<EvaluationDataset>(SerializerOptions) ?? throw new EvaluationDatasetException("The evaluation dataset could not be deserialized.");
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

	private static void ValidatePromotedInventory(EvaluationDataset dataset)
	{
		string[] promotedIds = (from @group in dataset.Groups
			where @group.Promoted
			select @group.Id).ToArray();
		string[] absoluteOutcomeIds = (from @group in dataset.Groups
			where @group.AbsoluteOutcomeGate
			select @group.Id).ToArray();
		string[] advisoryIds = (from @group in dataset.Groups
			where !@group.Promoted
			select @group.Id).ToArray();
		if (!promotedIds.SequenceEqual<string>(PromotedGroupIds, StringComparer.Ordinal) || !absoluteOutcomeIds.SequenceEqual<string>(AbsoluteOutcomeGroupIds, StringComparer.Ordinal) || !advisoryIds.SequenceEqual<string>(AdvisoryGroupIds, StringComparer.Ordinal))
		{
			throw new EvaluationDatasetException("The default evaluation dataset must contain the fixed promoted, absolute-gate, and advisory inventories.");
		}
	}

	private static void ValidateSemantics(EvaluationDataset dataset)
	{
		if (dataset.SchemaVersion != 1 || dataset.Groups.Count == 0 || HasDuplicates(dataset.Groups.Select((EvaluationGroup group) => group.Id)))
		{
			throw new EvaluationDatasetException("The evaluation dataset has an invalid group inventory.");
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
	}

	private static void ValidateExpectation(EvaluationInterpretationExpectation expectation)
	{
		bool expectsSuccess = expectation.DialogueAct.HasValue;
		bool expectsFailure = expectation.Failure.HasValue;
		bool flag = expectsSuccess == expectsFailure;
		bool flag2 = flag;
		if (!flag2)
		{
			int maximumToolCalls = expectation.MaximumToolCalls;
			bool flag3 = ((maximumToolCalls < 0 || maximumToolCalls > 4) ? true : false);
			flag2 = flag3;
		}
		if (flag2 || expectation.AllowedTools.Except<string>(TargetAgentMcpCatalog.ToolNames, StringComparer.Ordinal).Any() || expectation.RequiredTools.Except<string>(expectation.AllowedTools, StringComparer.Ordinal).Any() || HasDuplicates(expectation.AllowedTools) || HasDuplicates(expectation.RequiredTools))
		{
			throw new EvaluationDatasetException("A evaluation turn has an invalid interpretation or tool expectation.");
		}
		bool hasProposal = expectation.Proposal is not null;
		if (expectation.DialogueAct == DialogueAct.UpdateDraft != hasProposal || expectation.DialogueAct == DialogueAct.DiscussDraft != expectation.DiscussionTopic.HasValue)
		{
			throw new EvaluationDatasetException("A evaluation turn has an invalid dialogue-act payload expectation.");
		}
	}

	private static bool HasDuplicates(IEnumerable<string> values)
	{
		HashSet<string> observed = new HashSet<string>(StringComparer.Ordinal);
		return values.Any((string value) => !observed.Add(value));
	}
}
