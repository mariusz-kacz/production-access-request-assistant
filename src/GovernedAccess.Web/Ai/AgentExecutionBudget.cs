using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace GovernedAccess.Web.Ai;

internal sealed class AgentExecutionBudget(AgentExecutionLimits limits)
{
    private readonly Dictionary<string, int> toolCallCounts =
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
        return await InnerFunction.InvokeAsync(arguments, cancellationToken);
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
