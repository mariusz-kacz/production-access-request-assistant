using GovernedAccess.Core.Domain.Preparations;
using GovernedAccess.Core.Preparations;

namespace GovernedAccess.Web.Teams;

internal sealed class TargetRequestConfirmationAdapter(
    IPreparationConfirmationService confirmationService) :
    ITargetRequestConfirmation
{
    public async Task<TargetConfirmationResult> ConfirmAsync(
        PreparationBinding binding,
        Guid preparationId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var result = await confirmationService.ConfirmAsync(
            new PreparationConfirmationCommand(
                binding,
                preparationId,
                correlationId),
            cancellationToken);
        return result switch
        {
            PreparationConfirmationSubmitted submitted =>
                TargetConfirmationResult.Submitted(
                    submitted.Request.Id,
                    submitted.Request.Status,
                    submitted.WasAlreadySubmitted),
            PreparationConfirmationRevalidationFailed revalidationFailed =>
                TargetConfirmationResult.RevalidationFailed(
                    revalidationFailed.Revalidation),
            PreparationConfirmationSourceUnavailable =>
                TargetConfirmationResult.SourceUnavailable(),
            PreparationConfirmationFailed failed =>
                TargetConfirmationResult.Failed(failed.Failure),
            _ => throw new InvalidOperationException(
                "The preparation-confirmation result is unsupported."),
        };
    }
}
