using GovernedAccess.Core.Application;
using GovernedAccess.Core.Domain.AccessRequests;
using GovernedAccess.Core.Domain.Preparations;

namespace GovernedAccess.Core.Preparations;

public interface IPreparationConfirmationService
{
    Task<PreparationConfirmationResult> ConfirmAsync(
        PreparationConfirmationCommand command,
        CancellationToken cancellationToken);
}

public sealed record PreparationConfirmationCommand
{
    public PreparationConfirmationCommand(
        PreparationBinding binding,
        Guid preparationId,
        string correlationId)
    {
        ArgumentNullException.ThrowIfNull(binding);
        if (preparationId == Guid.Empty)
        {
            throw new ArgumentException(
                "A preparation identifier is required for confirmation.",
                nameof(preparationId));
        }

        Binding = binding;
        PreparationId = preparationId;
        CorrelationId = MaterialChangeAttribution.NormalizeCorrelationId(correlationId);
    }

    public PreparationBinding Binding { get; }

    public Guid PreparationId { get; }

    public string CorrelationId { get; }
}

public abstract record PreparationConfirmationResult;

public sealed record PreparationConfirmationSubmitted(
    AccessRequest Request,
    bool WasAlreadySubmitted) : PreparationConfirmationResult;

public sealed record PreparationConfirmationRevalidationFailed(
    PreparationTurnResult Revalidation) : PreparationConfirmationResult;

public sealed record PreparationConfirmationSourceUnavailable(
    ApplicationFailure Failure) : PreparationConfirmationResult;

public sealed record PreparationConfirmationFailed(
    ApplicationFailure Failure) : PreparationConfirmationResult;
