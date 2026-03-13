namespace Adze.Contracts.Enums;

public enum ApprovalState
{
    Draft = 0,
    PreviewReady = 1,
    AwaitingConfirmation = 2,
    Approved = 3,
    Executing = 4,
    Verifying = 5,
    Completed = 6,
    RolledBack = 7,
    Failed = 8
}
