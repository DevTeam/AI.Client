# Unit testing strategy

Status: Accepted

## Markdown renderer

Markdown rendering is isolated behind `IMarkdownRenderer`. The implementation disables source HTML in Markdig and sanitizes generated output before it is rendered as `MarkupString`. Future renderer tests must stay pure and use only string input/output; they must not require a browser runtime.

## Required stack

- xUnit — test framework and runner.
- Shouldly — all result and state assertions.
- Moq — interface dependency mocks.

No other assertion framework or mocking framework is added without a separate architectural decision.

## Core rules

Automated tests must be:

- unit;
- fast;
- deterministic;
- independent of each other;
- independent of run order and parallelization;
- independent of the network, disk, browser, processes, credentials, locale, timezone, and current time;
- reproducible locally and in CI without additional configuration.

Not allowed:

- integration and end-to-end tests;
- live OpenAI/MCP calls;
- launching Kestrel, a browser, or an MCP executable;
- reading and writing the real file system;
- `Thread.Sleep`, waiting on real time, and random retry delays;
- dependency on environment variables or the user profile;
- shared mutable state between tests.

Target guideline: the bulk of tests run in milliseconds, and the full suite runs in seconds.

Model quality comparisons are separate experiments under `evals/AI.Context.Evals`, outside
`AI.slnx` and normal unit-test runs. [ADR-011](decisions/ADR-011-budgeted-summary-requests.md)
permits explicit, opt-in endpoint calls there; the unit-test rules above remain unchanged.
Shared continuation fixtures keep budget/retention contract tests and live quality comparisons
aligned. Configuration and interpretation are in [context evaluation](31-context-evaluation.md).

## Test structure

The `Given`–`When`–`Then` style from `CSharpInteractive.Tests/CISettingsTests.cs` is used. One test verifies one observable behavior. The test class is `public`, dependencies are created as fields via `Mock<T>`, and the SUT is created in the instance method `CreateInstance`. `[Theory]` and `[InlineData]` are used for sets of boundary values. A name starts with `Should` and describes the observable behavior:

```csharp
[Fact]
public async Task ShouldNotCallMcpServerWhenToolIsDenied()
{
    // Given
    var connection = new Mock<IMcpConnection>();
    var policy = new Mock<IToolPolicyEvaluator>();
    policy
        .Setup(i => i.EvaluateAsync(It.IsAny<ToolInvocation>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(PolicyDecision.Deny);

    var agent = CreateInstance(connection.Object, policy.Object);

    // When
    var result = await agent.RunAsync(CreateRequest(), CancellationToken.None);

    // Then
    result.Status.ShouldBe(AgentRunStatus.Completed);
    connection.Verify(
        i => i.CallToolAsync(It.IsAny<ToolCall>(), It.IsAny<CancellationToken>()),
        Times.Never);
}
```

`CreateInstance` and object mothers/builders may be used to reduce noise, but must not hide the meaningful inputs of a test. Comments in test code are written in English.

## Test seams

External effects are accessible only through interfaces:

```text
IAIEndpoint
IMcpConnection
IFileSystem
IAtomicFileWriter
ICredentialStore
IClock
IIdGenerator
IDelay
IBrowserStorage
```

Domain tests do not use mocks. Application tests use Moq for ports. For pure algorithms, a simple fake or value object is preferred over mock setup when it is easier to read.

## Domain tests

- Project invariants.
- Chat/branch invariants.
- Tool policy precedence.
- Copy/remap between projects.
- AgentRun state transitions.
- Building the root → branch head path.
- Detecting unreachable nodes as a pure operation on the graph.

Domain tests create only in-memory objects and do not depend on Infrastructure.

## Storage tests

Storage logic is tested on top of `IFileSystem`/`IAtomicFileWriter` with an in-memory fake or Moq:

- serialization and deserialization of schema versions;
- plan for atomic node/ref update;
- failure between writing a node and moving a ref;
- revision conflict;
- orphan detection;
- corrupted JSON and hash mismatch;
- migration/rollback decision.

Real temporary directories and OS-specific atomic rename are not used in automated tests. Thin platform adapters stay minimal and are verified through code review, static analysis, and manual acceptance.

## Application tests

- Agent loop through mocked AI/MCP ports.
- `Deny`, `Ask`, and `Allow`.
- Approval lifecycle.
- Cancellation at each state transition.
- Max iterations, calls, and deadline through a fake clock/delay.
- Retry of an allowed idempotent read.
- Forbidding retry of a completed side effect.
- Fork context construction.
- Filtering of system MCP tools.
- Reset of the policy on a schema hash change.

Streaming scenarios are represented by a pre-built `IAsyncEnumerable<AgentEvent>` without the network and without real delays.

## MCP tests

MCP orchestration is tested through a mock/fake transport, not a real server:

- capability negotiation result mapping;
- paginated `tools/list`;
- handling of `notifications/tools/list_changed`;
- identical tool names from different servers;
- invalid input/output schema;
- alias mapping;
- structured and unstructured results;
- OAuth challenge decision as a pure transformation of response metadata.

Launching a `stdio` process and a Streamable HTTP server in the test suite is forbidden.

## FileSystem security tests

Path and policy logic must be isolated from `System.IO` and verified as units:

- `..` traversal;
- absolute path outside root;
- symlink/junction metadata returned by a fake filesystem;
- case/canonicalization mismatch;
- UNC path;
- alternate data streams on Windows;
- delete when only edit is allowed;
- oversized input/result;
- stale edit hash;
- excluded glob;
- broad root directory without confirmation;
- re-check of the policy right before commit.

Every test explicitly sets platform/path semantics through a mock or value object, so the result does not depend on the OS where the runner is launched.

## Provider adapter tests

Adapters receive pre-built JSON/SSE fixtures through a mocked `HttpMessageHandler` or a custom transport interface:

- Responses API event mapping;
- Chat Completions fallback mapping;
- streaming frame fragmentation;
- multiple function calls;
- preservation of call ID and result;
- provider error/rate limit mapping;
- continuation fallback to the full local context.

Live API tests are absent.

## UI tests

Razor presentation logic is, where possible, extracted into plain view models/presenters and tested with xUnit. bUnit is allowed for isolated verification of a Razor component as a unit-level renderer, with the following:

- all services are provided through Moq;
- `IJSRuntime` is replaced with a mock;
- the browser and the Host are not started;
- HTTP and real timers are not used;
- assertions are written with Shouldly.

Playwright and other end-to-end tools are not used in the test suite.

## Determinism

- `IClock` returns a fixed time.
- `IIdGenerator` returns pre-defined IDs.
- `IDelay` completes immediately and records the requested delay.
- Cancellation is initiated by the test at the precise state transition.
- Culture and path comparison are passed explicitly.
- Collections are compared without assuming order when order is not part of the contract.

## Moq verification

Verify the meaningful interaction at the module boundary: whether an MCP tool was called, whether a node was saved, whether an approval was requested. Do not verify every internal operation or call order unless the order is part of the behavior.

`VerifyNoOtherCalls` is applied selectively: excessive verification makes tests brittle during safe refactoring.

## Quality gates

- All tests use xUnit.
- All assertions use Shouldly.
- Interface mocks are created through Moq.
- Test projects contain no network/process/browser test fixtures.
- Tests do not require credentials or environment configuration.
- A repeated run produces the same result.
- The full suite stays fast; noticeable slowdown is treated as a regression.
- Build and test pass identically locally and in CI.
