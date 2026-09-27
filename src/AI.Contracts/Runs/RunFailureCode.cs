namespace AI.Contracts.Runs;

public enum RunFailureCode { None, Transient, BranchChanged, BranchDeleted, ParentMissing, Storage, ContextWindow }
