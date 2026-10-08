# Operations and diagnostics

## Testing a saved endpoint profile

1. Start the Host configuration in Rider.
2. Create or select a project.
3. Add an endpoint profile with the `/v1` base URL and a model name.
4. Enter the API key and save endpoint profiles. The key is protected locally for the current Windows user and is not shown again.
5. Choose the profile in **Live chat**, enter a message, and select **Send**.

## Local automation

All repeatable operations are launched through a dedicated build application in the `build` directory. It follows the `dotnet-matrix/build` approach: a Pure.DI composition root creates interface targets, and the CLI selects the operation to run.

```powershell
dotnet run --project build -- build
dotnet run --project build -- test
dotnet run --project build -- test-all
dotnet run --project build -- verify
dotnet run --project build -- publish --output artifacts/publish
```

`test` runs fast unit tests, and `test-all` also runs tests marked `Category=Integration` or `Category=Slow`.
`verify` is the mandatory check before submitting changes: it stops at the first failure and runs the build before executing the fast unit tests. The output of child `dotnet` processes is written to `artifacts/logs`; the directory is not under source control.

## Verifying an OpenAI-compatible endpoint

1. Run `AI Host` in Rider or `dotnet run --project src/AI.Host`.
2. Open `http://localhost:52173`.
3. In the **Live chat** section, set the base URL in the form `https://host/v1`, a model, and, if the endpoint requires authentication, an API key.
4. Send a short message.

The base URL must already contain the API version prefix if the corporate gateway expects one. The client only appends `/chat/completions`. The API key is temporary: it is not saved after the page is refreshed.

## Rider

Versioned Rider configurations live in the `.run` directory and are available immediately after opening the solution:

- `AI Host` launches the Host in Development mode at `http://localhost:52173`;
- `Verify AI` runs the mandatory `verify` check;
- `Publish AI` publishes the Host to `artifacts/publish`.

Status: Accepted

## Local run

The user launches `AI.Host`. The Host:

1. Acquires an exclusive lock on the data directory.
2. Checks schema versions and unfinished atomic writes.
3. Brings up a loopback endpoint.
4. Serves the WASM UI.
5. Opens the browser once the endpoint is ready.

The PWA may cache the UI shell, but agent operations require the Host to be running.

## Configuration

Split into:

- non-secret Host configuration;
- protected credential store;
- project JSON documents;
- registered MCP executable registry;
- recoverable UI cache.

Data/log/cache paths are shown in the diagnostics UI.

## Logs

Structured logs contain a timestamp, level, component, project/chat/run IDs, and a correlation ID. Redaction is mandatory for:

- Authorization/API-key headers;
- OAuth tokens;
- credential fields;
- URL query secrets;
- tool arguments marked sensitive by local configuration.

Full file contents are not logged by default.

## Diagnostics bundle

Exports:

- Host/WASM/.NET versions;
- negotiated MCP protocol revisions and capabilities;
- endpoint capability snapshots without credentials;
- schema versions;
- redacted recent logs;
- failed run metadata;
- storage integrity report.

Before saving, the user sees the bundle contents.

## Backup

A backup is created from a consistent snapshot:

- stop new writes;
- wait for current atomic commits to finish;
- copy the project JSON tree;
- verify JSON schemas and content hashes;
- write a manifest with SHA-256.

Credentials are exported only through a separate, explicitly protected procedure and are not part of the regular project export.

## Recovery

- A `.tmp` without a final file is removed after inspection.
- Immutable orphan nodes are not attached automatically.
- A corrupted ref is restored only from a backup/audit with confirmation.
- An unknown schema version opens read-only.
- A corrupted content object marks dependent messages but is not removed automatically.

## Update

Before migration, the Host creates a backup. Migrations run sequentially and are verified before the active data directory is switched. A downgrade does not promise writes to a newer schema, but must preserve the data and report a read-only mode.

## MCP process management

- process lifetime is bound to the Host session or the configured keep-alive;
- stdout is reserved for the MCP stdio protocol;
- the diagnostics server writes to stderr;
- a crash is bounded by a backoff/restart budget;
- an infinite restart loop is forbidden;
- executable/schema changes require re-evaluation of trust and policies.

## Metrics without telemetry

Latency, token usage, tool count, retry count, context size, and storage size are available locally. External telemetry is disabled by default and is not part of the MVP.

