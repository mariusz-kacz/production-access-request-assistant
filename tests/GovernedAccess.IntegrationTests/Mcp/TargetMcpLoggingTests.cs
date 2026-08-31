using System.Collections.Concurrent;
using GovernedAccess.IntegrationTests.Infrastructure;
using GovernedAccess.Mcp;
using Microsoft.Extensions.Logging;

namespace GovernedAccess.IntegrationTests.Mcp;

public sealed class TargetMcpLoggingTests
{
    [Fact]
    public async Task TargetToolLogsOnlySafeOperationalMetadata()
    {
        const string query = "alpha EU primary";
        using var logs = new CapturingLoggerProvider();
        await using var host = await TargetMcpTestHost.CreateSeededAsync(
            logs,
            TestContext.Current.CancellationToken);
        await using var client = await host.CreateClientAsync(
            "governed-access-target-logging-tests",
            TestContext.Current.CancellationToken);
        var tools = await client.ListToolsAsync(
            cancellationToken: TestContext.Current.CancellationToken);
        var tool = Assert.Single(
            tools,
            candidate => candidate.Name == "search_production_environments");

        _ = await tool.CallAsync(
            new Dictionary<string, object?>
            {
                ["query"] = query,
            },
            cancellationToken: TestContext.Current.CancellationToken);

        var entries = logs.Entries
            .Where(candidate =>
                candidate.Category == typeof(TargetMcpToolExecutor).FullName)
            .ToArray();
        Assert.Equal([3010, 3011], entries.Select(candidate => candidate.EventId.Id));
        var entry = Assert.Single(
            entries,
            candidate => candidate.EventId.Id == 3011);
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Equal(
            "search_production_environments",
            entry.Properties["ToolName"]);
        Assert.Equal("Succeeded", entry.Properties["Outcome"]);
        Assert.True(Convert.ToDouble(
            entry.Properties["DurationMilliseconds"],
            System.Globalization.CultureInfo.InvariantCulture) >= 0);
        var captured = string.Join(
            " ",
            entries.SelectMany(candidate =>
                candidate.Properties
                    .Select(property => $"{property.Key}={property.Value}")
                    .Prepend(candidate.Message)));
        Assert.DoesNotContain(query, captured, StringComparison.Ordinal);
        Assert.DoesNotContain("Primary Production EU", captured, StringComparison.Ordinal);
        Assert.DoesNotContain("client-alpha", captured, StringComparison.Ordinal);
    }

    private sealed record CapturedLog(
        string Category,
        LogLevel Level,
        EventId EventId,
        string Message,
        IReadOnlyDictionary<string, object?> Properties);

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly ConcurrentQueue<CapturedLog> entries = new();

        internal IReadOnlyCollection<CapturedLog> Entries => entries.ToArray();

        public ILogger CreateLogger(string categoryName) =>
            new CapturingLogger(categoryName, entries);

        public void Dispose()
        {
        }
    }

    private sealed class CapturingLogger(
        string category,
        ConcurrentQueue<CapturedLog> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull =>
            null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var properties = state is IEnumerable<KeyValuePair<string, object?>> values
                ? values.ToDictionary(
                    pair => pair.Key,
                    pair => pair.Value,
                    StringComparer.Ordinal)
                : new Dictionary<string, object?>(StringComparer.Ordinal);
            entries.Enqueue(
                new CapturedLog(
                    category,
                    logLevel,
                    eventId,
                    formatter(state, exception),
                    properties));
        }
    }
}
