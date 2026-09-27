# ADR-005: Fast unit tests on xUnit

Status: Accepted

Date: 2026-08-11

## Context

AI has many external boundaries: OpenAI endpoints, MCP transports, processes, the file system, browser APIs, and the protected credential store. Tests that use these resources directly will be slow, flaky, and dependent on the environment.

## Decision

The automated test suite consists only of fast unit tests. It uses xUnit, Shouldly, and Moq. Every external dependency is exposed through an interface and replaced with a mock, fake, or fixture. Integration, end-to-end, and live tests are not created.

## Consequences

Positive:

- fast feedback loop;
- stable CI;
- no credentials or external services required;
- precise verification of domain and application behavior;
- failures are easy to localize to a single module.

Negative:

- wiring and real transport/OS adapters are not verified by the automated test suite;
- adapters must stay thin;
- strict code review, static analysis, and manual acceptance of the published application are required.

## Rules

- Domain tests do not use mocks.
- Application tests use Moq for ports.
- Assertions are written with Shouldly.
- Time, IDs, delay, filesystem, HTTP, MCP, and browser abstractions are controlled by the test.
- `Thread.Sleep`, the network, real processes, and the real disk are forbidden.

