using System;
using System.Threading;
using Adze.Contracts.Abstractions;
using Adze.Contracts.Models;

namespace Adze.Broker.Orchestration;

public sealed class WriteExecutionCoordinator
{
    private readonly IStateDiffService _diffService;
    private readonly IVerificationPolicy _verificationPolicy;

    public WriteExecutionCoordinator(
        IStateDiffService diffService,
        IVerificationPolicy verificationPolicy)
    {
        _diffService = diffService;
        _verificationPolicy = verificationPolicy;
    }

    /// <summary>
    /// Executes the full write lifecycle: preview → approve → apply → verify → trace.
    /// The caller must provide:
    ///   - captureBefore/captureAfter: delegates that capture state on the UI thread
    ///   - applyOnUiThread: delegate that runs Apply on the UI thread
    ///   - refreshContext: delegate that re-captures SessionContext after apply
    ///   - requestApproval: delegate that blocks until the user approves/cancels
    /// </summary>
    public WriteExecutionOutcome Execute<TParams>(
        IWriteTool<TParams> tool,
        TParams parameters,
        SessionContext context,
        Func<WriteTargetDescriptor, StateSnapshot> captureBefore,
        Func<WriteTargetDescriptor, StateSnapshot> captureAfter,
        Func<object, TParams, WriteApplyResult> applyOnUiThread,
        Func<SessionContext> refreshContext,
        Func<WritePreview, CancellationToken, ApprovalDecision> requestApproval,
        CancellationToken cancellationToken)
    {
        string toolName = GetToolName(tool, parameters);

        // Step 1: Preview
        WritePreview preview;
        try
        {
            preview = tool.Preview(context, parameters);
        }
        catch (Exception ex)
        {
            return new WriteExecutionOutcome
            {
                Status = WriteOutcomeStatus.ApplyFailed,
                ToolName = toolName,
                ErrorMessage = "Preview failed: " + ex.Message
            };
        }

        // Step 2: Request approval
        if (cancellationToken.IsCancellationRequested)
        {
            return Cancelled(toolName, preview);
        }

        ApprovalDecision approval;
        try
        {
            approval = requestApproval(preview, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return Cancelled(toolName, preview);
        }

        if (approval == ApprovalDecision.Cancel)
        {
            return Cancelled(toolName, preview);
        }

        if (approval == ApprovalDecision.Modify)
        {
            return new WriteExecutionOutcome
            {
                Status = WriteOutcomeStatus.Cancelled,
                ToolName = toolName,
                Preview = preview,
                ErrorMessage = "User requested modification. Re-submit with updated parameters."
            };
        }

        // Step 3: Capture before snapshot
        WriteTargetDescriptor target = BuildTarget(tool, parameters);
        StateSnapshot? beforeSnapshot = null;
        try
        {
            beforeSnapshot = captureBefore(target);
        }
        catch (Exception ex)
        {
            // Non-fatal — continue without snapshot
            System.Diagnostics.Trace.WriteLine("Before snapshot failed: " + ex.Message);
        }

        // Step 4: Apply on UI thread (inside undo scope)
        if (cancellationToken.IsCancellationRequested)
        {
            return Cancelled(toolName, preview);
        }

        WriteApplyResult applyResult;
        try
        {
            applyResult = applyOnUiThread(null!, parameters);
        }
        catch (Exception ex)
        {
            return new WriteExecutionOutcome
            {
                Status = WriteOutcomeStatus.ApplyFailed,
                ToolName = toolName,
                UndoLabel = tool.BuildUndoLabel(parameters),
                Preview = preview,
                BeforeSnapshot = beforeSnapshot,
                ErrorMessage = "Apply threw exception: " + ex.Message
            };
        }

        if (!applyResult.Success)
        {
            return new WriteExecutionOutcome
            {
                Status = WriteOutcomeStatus.ApplyFailed,
                ToolName = toolName,
                UndoLabel = applyResult.UndoLabel,
                Preview = preview,
                ApplyResult = applyResult,
                BeforeSnapshot = beforeSnapshot,
                ErrorMessage = applyResult.ErrorMessage
            };
        }

        // Step 5: Capture after snapshot
        StateSnapshot? afterSnapshot = null;
        try
        {
            afterSnapshot = captureAfter(target);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine("After snapshot failed: " + ex.Message);
        }

        // Step 6: Compute diff
        StateDiff? diff = null;
        if (beforeSnapshot != null && afterSnapshot != null)
        {
            diff = _diffService.Compare(beforeSnapshot, afterSnapshot);
        }

        // Step 7: Verify
        SessionContext refreshedContext;
        try
        {
            refreshedContext = refreshContext();
        }
        catch
        {
            refreshedContext = context;
        }

        WriteVerification verification = tool.Verify(refreshedContext, applyResult);
        VerificationDecision decision = _verificationPolicy.Evaluate(toolName, verification, refreshedContext);

        // Step 8: Build outcome
        var outcome = new WriteExecutionOutcome
        {
            Status = decision.ShouldRollback ? WriteOutcomeStatus.VerificationFailed : WriteOutcomeStatus.Success,
            ToolName = toolName,
            UndoLabel = applyResult.UndoLabel,
            Preview = preview,
            ApplyResult = applyResult,
            Verification = verification,
            Decision = decision,
            BeforeSnapshot = beforeSnapshot,
            AfterSnapshot = afterSnapshot,
            Diff = diff
        };

        return outcome;
    }

    private static WriteExecutionOutcome Cancelled(string toolName, WritePreview preview)
    {
        return new WriteExecutionOutcome
        {
            Status = WriteOutcomeStatus.Cancelled,
            ToolName = toolName,
            Preview = preview
        };
    }

    private static WriteTargetDescriptor BuildTarget<TParams>(IWriteTool<TParams> tool, TParams parameters)
    {
        if (parameters is SetCustomPropertyParameters propParams)
        {
            return new WriteTargetDescriptor
            {
                Kind = WriteTargetKind.CustomProperty,
                TargetName = propParams.PropertyName,
                ConfigurationName = propParams.ConfigurationName
            };
        }

        if (parameters is SetDimensionValueParameters dimParams)
        {
            return new WriteTargetDescriptor
            {
                Kind = WriteTargetKind.Dimension,
                TargetName = dimParams.DimensionFullName,
                ConfigurationName = dimParams.ConfigurationName
            };
        }

        if (parameters is SuppressFeatureParameters supParams)
        {
            return new WriteTargetDescriptor
            {
                Kind = WriteTargetKind.FeatureSuppression,
                TargetName = supParams.FeatureName,
                ConfigurationName = supParams.ConfigurationName
            };
        }

        if (parameters is UnsuppressFeatureParameters unsupParams)
        {
            return new WriteTargetDescriptor
            {
                Kind = WriteTargetKind.FeatureSuppression,
                TargetName = unsupParams.FeatureName,
                ConfigurationName = unsupParams.ConfigurationName
            };
        }

        return new WriteTargetDescriptor();
    }

    private static string GetToolName<TParams>(IWriteTool<TParams> tool, TParams parameters)
    {
        if (parameters is SetCustomPropertyParameters)
            return Contracts.Tooling.ToolNames.SetCustomProperty;
        if (parameters is SetDimensionValueParameters)
            return Contracts.Tooling.ToolNames.SetDimensionValue;
        if (parameters is SuppressFeatureParameters)
            return Contracts.Tooling.ToolNames.SuppressFeature;
        if (parameters is UnsuppressFeatureParameters)
            return Contracts.Tooling.ToolNames.UnsuppressFeature;

        return tool.GetType().Name;
    }
}
