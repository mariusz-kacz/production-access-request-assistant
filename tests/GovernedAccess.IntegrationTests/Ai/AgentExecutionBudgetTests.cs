using GovernedAccess.Web.Ai;

namespace GovernedAccess.IntegrationTests.Ai;

public sealed class AgentExecutionBudgetTests
{
    [Fact]
    public void BudgetOverrunsAreRejectedWithoutCountingRejectedWork()
    {
        AssertProviderIterationLimit();
        AssertTotalToolCallLimit();
        AssertPerToolCallLimit();
    }

    private static void AssertProviderIterationLimit()
    {
        var budget = new AgentExecutionBudget(AgentExecutionLimits.Default);
        for (var iteration = 0; iteration < 6; iteration++)
        {
            budget.BeginProviderIteration();
        }

        Assert.Throws<AgentExecutionBudgetException>(
            budget.BeginProviderIteration);
        Assert.Equal(6, budget.ProviderIterationCount);
        Assert.True(budget.LimitExceeded);
    }

    private static void AssertTotalToolCallLimit()
    {
        var budget = new AgentExecutionBudget(AgentExecutionLimits.Default);
        budget.BeginToolCall("tool-1");
        budget.BeginToolCall("tool-2");
        budget.BeginToolCall("tool-3");
        budget.BeginToolCall("tool-4");

        Assert.Throws<AgentExecutionBudgetException>(
            () => budget.BeginToolCall("tool-5"));
        Assert.Equal(4, budget.ToolCallCount);
        Assert.True(budget.LimitExceeded);
    }

    private static void AssertPerToolCallLimit()
    {
        var budget = new AgentExecutionBudget(AgentExecutionLimits.Default);
        budget.BeginToolCall("search_production_environments");

        Assert.Throws<AgentExecutionBudgetException>(
            () => budget.BeginToolCall("search_production_environments"));
        Assert.Equal(1, budget.ToolCallCount);
        Assert.True(budget.LimitExceeded);
    }
}
