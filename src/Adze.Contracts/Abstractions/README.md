# Adze.Contracts/Abstractions

Cross-layer service interfaces. These live in Contracts so they can be referenced by any project without circular dependencies.

| Interface | Purpose |
|-----------|---------|
| `IWriteTool<TParams>` | Preview/Apply/Verify/BuildUndoLabel lifecycle for write tools |
| `IStateSnapshotService` | Capture before/after state for write verification |
| `IStateDiffService` | Compare two snapshots to compute changes |
| `IVerificationPolicy` | Evaluate whether a write succeeded or should be rolled back |
| `IApprovalCoordinator` | Block background thread until user approves/cancels a write |
| `ITrustService` | Check current trust tier, authorize write tools, evaluate recipe promotion |
| `IUiThreadInvoker` | Marshal actions to the UI/STA thread for COM calls; testable via `SynchronousUiThreadInvoker` |
