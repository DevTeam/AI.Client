namespace AI.Application.Tests.Chat;

using AI.Application.Chat;

/// <summary>Shared continuation fixtures for contract tests and opt-in model evaluations.</summary>
internal sealed record ContextRetentionScenario(string Name, IReadOnlyList<ChatCompletionMessage> Messages,
    IReadOnlyList<string> RequiredFacts)
{
    public static IReadOnlyList<ContextRetentionScenario> All { get; } =
    [
        new("debugging", [new("user", "Repair the parser. Constraint: preserve PUBLIC_API; never alter SNAPSHOT_42."),
            new("assistant", "Decision: use INSTANCE_SERVICE because lifecycle requires RUN_SCOPE. "
                + "Evidence: failure CS1001 in src/Parser.cs:42. Remaining work: verify CANCELLATION.")],
            ["PUBLIC_API", "SNAPSHOT_42", "INSTANCE_SERVICE", "RUN_SCOPE", "CS1001", "src/Parser.cs:42", "CANCELLATION"]),
        new("pending_work", [new("user", "Keep the branch RELEASE_7. Do not publish; deployment requires APPROVAL_PENDING."),
            new("assistant", "Evidence: unit tests PASS_18; integration suite is NOT_RUN. "
                + "Failure: connection failed with ECONNREFUSED. Remaining work: retry SERVICE_8080 after it starts.")],
            ["RELEASE_7", "APPROVAL_PENDING", "PASS_18", "NOT_RUN", "ECONNREFUSED", "SERVICE_8080"]),
        new("corrected_constraint", [new("user", "Initial proposal: keep LEGACY_MODE."),
            new("user", "Correction: LEGACY_MODE is rejected; use ACTIVE_MODE and keep DATA_SCHEMA_3."),
            new("assistant", "Decision: ACTIVE_MODE prevents DUPLICATE_WRITES. Remaining work: validate MIGRATION_ROLLBACK.")],
            ["LEGACY_MODE", "rejected", "ACTIVE_MODE", "DATA_SCHEMA_3", "DUPLICATE_WRITES", "MIGRATION_ROLLBACK"])
    ];
}
