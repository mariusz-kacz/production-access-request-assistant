using System.Runtime.CompilerServices;
using System.Text.Json;
using GovernedAccess.Core.Preparations.Contracts;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Protocol;

namespace GovernedAccess.Web.Ai;

internal sealed class AgentExecutionBudget(AgentExecutionLimits limits)
{
    private const string EnvironmentSearchToolName =
        "search_production_environments";

    private readonly Dictionary<string, int> toolCallCounts =
        new(StringComparer.Ordinal);
    private readonly HashSet<string> searchResultEnvironmentIds =
        new(StringComparer.Ordinal);
    private readonly object sync = new();
    private int providerIterationCount;
    private int toolCallCount;

    internal int ProviderIterationCount
    {
        get
        {
            lock (sync)
            {
                return providerIterationCount;
            }
        }
    }

    internal int ToolCallCount
    {
        get
        {
            lock (sync)
            {
                return toolCallCount;
            }
        }
    }

    internal bool LimitExceeded { get; private set; }

    internal void BeginProviderIteration()
    {
        lock (sync)
        {
            if (providerIterationCount >= limits.MaximumProviderIterations)
            {
                LimitExceeded = true;
                throw new AgentExecutionBudgetException();
            }

            providerIterationCount++;
        }
    }

    internal void BeginToolCall(string toolName)
    {
        lock (sync)
        {
            toolCallCounts.TryGetValue(toolName, out var perToolCount);
            if (toolCallCount >= limits.MaximumToolCalls
                || perToolCount >= limits.MaximumCallsPerTool)
            {
                LimitExceeded = true;
                throw new AgentExecutionBudgetException();
            }

            toolCallCount++;
            toolCallCounts[toolName] = perToolCount + 1;
        }
    }

    internal void ObserveToolResult(string toolName, object? result)
    {
        if (!string.Equals(
                toolName,
                EnvironmentSearchToolName,
                StringComparison.Ordinal))
        {
            return;
        }

        var structuredContent = result switch
        {
            CallToolResult callToolResult => callToolResult.StructuredContent,
            _ => result,
        };
        if (structuredContent is null)
        {
            return;
        }

        var root = JsonSerializer.SerializeToElement(structuredContent);
        if (root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("structuredContent", out var wrappedContent))
        {
            root = wrappedContent;
        }

        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("environments", out var environments)
            || environments.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        lock (sync)
        {
            foreach (var environment in environments.EnumerateArray())
            {
                if (environment.ValueKind == JsonValueKind.Object
                    && environment.TryGetProperty(
                        "environmentId",
                        out var environmentId)
                    && environmentId.ValueKind == JsonValueKind.String
                    && environmentId.GetString() is { Length: > 0 } id)
                {
                    searchResultEnvironmentIds.Add(id);
                }
            }
        }
    }

    internal bool IsEnvironmentSelectionAllowed(TurnProposal proposal)
    {
        if (proposal.Patch?.Environment is not SetEnvironmentOperation
            {
                Reference: ExactEnvironmentId exactEnvironment,
            })
        {
            return true;
        }

        lock (sync)
        {
            if (!toolCallCounts.ContainsKey(EnvironmentSearchToolName))
            {
                return true;
            }

            return searchResultEnvironmentIds.Count == 1
                && searchResultEnvironmentIds.Contains(exactEnvironment.Id);
        }
    }
}

internal sealed class BudgetedAgentTool(
    AIFunction innerFunction,
    AgentExecutionBudget budget)
    : DelegatingAIFunction(innerFunction)
{
    protected override async ValueTask<object?> InvokeCoreAsync(
        AIFunctionArguments arguments,
        CancellationToken cancellationToken)
    {
        budget.BeginToolCall(Name);
        var result = await InnerFunction.InvokeAsync(arguments, cancellationToken);
        budget.ObserveToolResult(Name, result);
        return result;
    }
}

internal sealed class ProviderIterationBudgetChatClient(
    IChatClient innerClient,
    AgentExecutionBudget budget)
    : DelegatingChatClient(innerClient)
{
    public override Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        budget.BeginProviderIteration();
        return base.GetResponseAsync(messages, options, cancellationToken);
    }

    public override async IAsyncEnumerable<ChatResponseUpdate>
        GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        budget.BeginProviderIteration();
        await foreach (var update in base.GetStreamingResponseAsync(
            messages,
            options,
            cancellationToken))
        {
            yield return update;
        }
    }
}

internal sealed class AgentExecutionBudgetException : Exception;
