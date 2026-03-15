using System.Linq;
using Adze.Contracts.Abstractions;
using Adze.Contracts.Models;

namespace Adze.Broker.Orchestration;

public sealed class DefaultVerificationPolicy : IVerificationPolicy
{
    public VerificationDecision Evaluate(string toolName, WriteVerification verification, SessionContext refreshedContext)
    {
        // If the change was not confirmed in the post-apply read, suggest rollback
        if (!verification.ChangeConfirmed)
        {
            return new VerificationDecision
            {
                Outcome = VerificationOutcome.SuggestRollback,
                Reason = "The requested change was not observed after apply. The target value may not have been set correctly."
            };
        }

        // If rebuild failed, suggest rollback
        if (!verification.RebuildSucceeded)
        {
            return new VerificationDecision
            {
                Outcome = VerificationOutcome.SuggestRollback,
                Reason = "Rebuild failed after applying the change. Rolling back is recommended to restore a valid model state."
            };
        }

        // If there are unexpected changes beyond what was requested, flag it
        if (verification.UnexpectedChanges.Count > 0)
        {
            string unexpectedSummary = string.Join(", ",
                verification.UnexpectedChanges.Select(c => c.Path));

            return new VerificationDecision
            {
                Outcome = VerificationOutcome.SuggestRollback,
                Reason = "Unexpected changes detected after apply: " + unexpectedSummary +
                         ". Review these changes before proceeding."
            };
        }

        // If rebuild warnings exist, accept but note them
        if (verification.RebuildWarnings.Count > 0)
        {
            return new VerificationDecision
            {
                Outcome = VerificationOutcome.Accepted,
                Reason = "Change verified successfully. Rebuild warnings: " +
                         string.Join("; ", verification.RebuildWarnings)
            };
        }

        // Clean success
        return new VerificationDecision
        {
            Outcome = VerificationOutcome.Accepted,
            Reason = "Change verified successfully."
        };
    }
}
