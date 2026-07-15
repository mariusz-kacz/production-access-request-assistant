using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace GovernedAccess.Web.Ai;

public enum DeterministicChatMode
{
    Valid,
    Incomplete,
    Malformed,
    Unsupported,
    Timeout,
    Cancellation,
    Unavailable
}

public sealed class DeterministicChatClient(DeterministicChatMode mode) : IChatClient
{
    private const string ValidResponse =
        """
        {"clientId":"client-alpha","environmentId":"PROD-ALPHA-EU","requestedRole":"ProductionReadOnly","durationMinutes":240,"justification":"Investigate the active production incident.","incidentId":"INC-1042"}
        """;

    private const string IncompleteResponse =
        """
        {"clientId":"client-alpha","environmentId":"PROD-ALPHA-EU","requestedRole":"ProductionReadOnly","durationMinutes":null,"justification":null,"incidentId":null}
        """;

    private const string MalformedResponse =
        """
        {"clientId":"client-alpha"
        """;

    private const string UnsupportedResponse =
        """
        {"clientId":"client-alpha","environmentId":"PROD-ALPHA-EU","requestedRole":"ProductionAdministrator","durationMinutes":240,"justification":"Investigate the active production incident.","incidentId":"INC-1042"}
        """;

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);

        if (mode is DeterministicChatMode.Timeout or DeterministicChatMode.Cancellation)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }

        if (mode is DeterministicChatMode.Unavailable)
        {
            throw new HttpRequestException("The deterministic chat client is unavailable.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        return new ChatResponse(new ChatMessage(ChatRole.Assistant, GetResponseText()));
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ChatResponse response = await GetResponseAsync(messages, options, cancellationToken);

        foreach (ChatMessage message in response.Messages)
        {
            yield return new ChatResponseUpdate(message.Role, message.Text);
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);

        return serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;
    }

    public void Dispose()
    {
    }

    private string GetResponseText() => mode switch
    {
        DeterministicChatMode.Valid => ValidResponse,
        DeterministicChatMode.Incomplete => IncompleteResponse,
        DeterministicChatMode.Malformed => MalformedResponse,
        DeterministicChatMode.Unsupported => UnsupportedResponse,
        DeterministicChatMode.Timeout or DeterministicChatMode.Cancellation =>
            throw new InvalidOperationException("A cancelled deterministic response cannot produce content."),
        DeterministicChatMode.Unavailable =>
            throw new InvalidOperationException("An unavailable deterministic response cannot produce content."),
        _ => throw new InvalidOperationException($"Unsupported deterministic chat mode '{mode}'.")
    };
}
