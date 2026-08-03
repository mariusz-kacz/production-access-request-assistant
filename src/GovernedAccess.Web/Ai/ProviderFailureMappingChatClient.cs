using System.Runtime.CompilerServices;
using Azure;
using Azure.Identity;
using Microsoft.Extensions.AI;
using System.ClientModel;

namespace GovernedAccess.Web.Ai;

internal sealed class ProviderFailureMappingChatClient(IChatClient inner)
    : IChatClient
{
    private const string ProviderTimeoutMessage =
        "The request-preparation model timed out.";
    private const string ProviderUnavailableMessage =
        "The request-preparation model is unavailable.";

    private readonly IChatClient inner = inner
        ?? throw new ArgumentNullException(nameof(inner));

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);

        try
        {
            return await inner.GetResponseAsync(
                messages,
                options,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException exception)
        {
            throw new TimeoutException(ProviderTimeoutMessage, exception);
        }
        catch (TimeoutException exception)
        {
            throw new TimeoutException(ProviderTimeoutMessage, exception);
        }
        catch (Exception exception) when (IsDependencyFailure(exception))
        {
            throw new HttpRequestException(
                ProviderUnavailableMessage,
                exception);
        }
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);
        await using var enumerator = inner
            .GetStreamingResponseAsync(messages, options, cancellationToken)
            .GetAsyncEnumerator(cancellationToken);

        while (true)
        {
            ChatResponseUpdate update;
            try
            {
                if (!await enumerator.MoveNextAsync())
                {
                    yield break;
                }

                update = enumerator.Current;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException exception)
            {
                throw new TimeoutException(ProviderTimeoutMessage, exception);
            }
            catch (TimeoutException exception)
            {
                throw new TimeoutException(ProviderTimeoutMessage, exception);
            }
            catch (Exception exception) when (IsDependencyFailure(exception))
            {
                throw new HttpRequestException(
                    ProviderUnavailableMessage,
                    exception);
            }

            yield return update;
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);

        return serviceKey is null && serviceType.IsInstanceOfType(this)
            ? this
            : inner.GetService(serviceType, serviceKey);
    }

    public void Dispose() => inner.Dispose();

    private static bool IsDependencyFailure(Exception exception) =>
        exception is RequestFailedException
            or ClientResultException
            or AuthenticationFailedException
            or CredentialUnavailableException
            or HttpRequestException
            or IOException;
}
