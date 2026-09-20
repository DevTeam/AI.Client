namespace AI.Client.Domain.Runs;

public enum RunFailureKind { None, Transient, BranchChanged, BranchDeleted, ParentMissing, Storage, ContextWindow }
