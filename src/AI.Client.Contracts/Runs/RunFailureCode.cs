namespace AI.Client.Contracts.Runs;

public enum RunFailureCode { None, Transient, BranchChanged, BranchDeleted, ParentMissing, Storage }
