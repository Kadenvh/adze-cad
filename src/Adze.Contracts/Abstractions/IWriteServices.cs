using System.Threading;
using Adze.Contracts.Enums;
using Adze.Contracts.Models;

namespace Adze.Contracts.Abstractions;

public interface IStateSnapshotService
{
    StateSnapshot CaptureBefore(WriteTargetDescriptor target);

    StateSnapshot CaptureAfter(WriteTargetDescriptor target);
}

public interface IStateDiffService
{
    StateDiff Compare(StateSnapshot before, StateSnapshot after);
}

public interface IVerificationPolicy
{
    VerificationDecision Evaluate(string toolName, WriteVerification verification, SessionContext refreshedContext);
}

public interface IApprovalCoordinator
{
    ApprovalDecision RequestApproval(WritePreview preview, CancellationToken cancellationToken);
}

public enum ToolExecutionPolicy
{
    Allow,
    RequireConfirmation,
    Deny
}

public sealed class PolicyEvaluation
{
    public ToolExecutionPolicy Policy { get; set; } = ToolExecutionPolicy.Allow;

    public string Reason { get; set; } = string.Empty;

    public ToolUnlockTier RequiredTier { get; set; } = ToolUnlockTier.Baseline;

    public ToolUnlockTier CurrentTier { get; set; } = ToolUnlockTier.Baseline;
}

public interface IAgentPolicyEngine
{
    PolicyEvaluation Evaluate(string toolName, SessionContext context, string userId);
}

public interface IWriteTool<TParams>
{
    WritePreview Preview(SessionContext context, TParams parameters);

    WriteApplyResult Apply(object application, TParams parameters);

    WriteVerification Verify(SessionContext refreshedContext, WriteApplyResult applyResult);

    string BuildUndoLabel(TParams parameters);
}
