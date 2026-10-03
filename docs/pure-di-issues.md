# Pure.DI observations and issues

This file collects complaints, generator failures, and configuration limitations separately from the project's DI conventions. Observations are from the refactoring on 2026-10-03 with the project's pinned Pure.DI / Pure.DI.MS 2.5.4. The local Pure.DI reference documentation may describe a newer source revision; no claim here applies automatically to that revision.

## Generated enumerable escapes its declaration scope

Status: Reproduced during this refactoring. Generator defect; a valid service graph produced invalid C#.

After stateless server services and MCP connection factories were changed to transient, Host and Desktop compilation failed in generated `ServerComposition.g.cs`:

```text
error CS0103: The name 'perBlockIEnumerableIMcpServerConnection2' does not exist in the current context
```

The affected dependency was `CompositeToolSessionFactory(IEnumerable<IMcpServerConnection> connections, ...)`, with tagged transient connection implementations, anonymous ASP.NET roots, singleton run services and deferred factories in the same graph. Making the composite factory singleton avoided this error, but retaining a stateless singleton is an undesirable workaround.

Changing that constructor to `IReadOnlyCollection<IMcpServerConnection>` eliminated this diagnostic. The next build exposed the same problem in the tool collection:

```text
error CS0103: The name 'perBlockIEnumerableIAppTool2' does not exist in the current context
```

This second diagnostic occurred in Host, Desktop, `StartupComposition`, and `AppToolsComposition`. Its source was `AppMcpServerHost(IEnumerable<IAppTool> tools, ...)`, also populated by tagged transient implementations. Switching that collection to `IReadOnlyCollection<IAppTool>` eliminated its diagnostic but exposed the same scope problem in a deferred factory:

```text
error CS0103: The name 'perBlockFuncIToolSessionFactory2' does not exist in the current context
```

Final workaround: retain `.Singleton<CompositeToolSessionFactory>()` and the original enumerable constructor contracts. Its connections and their stateless dependencies remain transient and are retained by this owner. This singleton is a generator compatibility exception, not a need for shared mutable state. Revisit it after upgrading Pure.DI. These observations describe the production graph, not an independently minimized reproduction.

## Simplified bindings expose disposal interfaces

Status: Reproduced during this refactoring. API usability issue in the installed version; explicit bindings remain supported.

Moving `DesktopUnreadCountPublisher` out of the shared singleton binding group into `.Transient<DesktopUnreadCountPublisher>()` caused:

```text
error DIW000: The binding for System.IAsyncDisposable has been overridden.
```

Using `.Singleton<ChatReplySuggestions>()` separately from other server services produced the same warning. The project treats warnings as errors. Registering unrelated services by their disposal interface is not useful in this graph and makes the simplified API less predictable. Workaround: bind only `IUnreadCountPublisher` and `IChatReplySuggestions` explicitly. Disposal is still tracked by Pure.DI.

## Automatic contract inference can override a composite service

Status: Reproduced during this refactoring. Configuration limitation; inheritance-based contract inference is expected API behavior.

Replacing the explicit file-key-store binding with `.Transient<FileMasterKeyStore>()` also exposed its `IMasterKeyStore` contract. The composite `KeyringOrFileMasterKeyStore` then produced:

```text
error DIW000: The binding for AI.Infrastructure.Credentials.IMasterKeyStore has been overridden.
```

Workaround: retain `.Bind<IFileMasterKeyStore>().To<FileMasterKeyStore>()`. The file store is a dependency of the composite, not an alternative implementation to inject as the main master-key store.

## Simplified bindings omit inherited adapter contracts

Status: Reproduced by the existing tool-presentation tests. Configuration limitation consistent with direct-contract inference in the local documentation.

The specialized adapters derive from `BuiltInToolPresentationAdapter`, which implements `IToolPresentationAdapter`. Replacing explicit `Bind<IToolPresentationAdapter>(Tag.Unique)` declarations with simplified transient bindings did not populate the requested `IReadOnlyCollection<IToolPresentationAdapter>`. Compilation succeeded, but the presentation tests fell back to generic titles, severity and summaries. The collection contained none of the inherited specialized contracts.

Workaround: retain explicit interface bindings for these adapters. A successful build cannot detect a missing optional collection entry; behavior tests are necessary. A diagnostic for bindings that leave an intended collection empty would help uncover this class of configuration error.

## Constructor selection needs an explicit factory

Status: Reproduced during this refactoring. Constructor-selection limitation, not established as a generator defect.

With simplified bindings, the generator selected the string constructor of `JsonLineFileLoggerProvider` even though this graph supplies `IProjectStorageLocation`:

```text
error DIE000: Unable to resolve "string" in JsonLineFileLoggerProvider(string rootDirectory<--string).
```

The type also has a production constructor `(IProjectStorageLocation location, int retentionDays = 14)`. Workaround: `.Singleton((IProjectStorageLocation location) => new JsonLineFileLoggerProvider(location))` makes the intended constructor explicit without a class-to-class dependency.

## Anonymous and named roots cannot duplicate one contract

Status: Reproduced during this refactoring. Configuration limitation; the diagnostic itself is valid.

A Pure.DI-only fixture that inherited an anonymous `IChatService` root could not add its named counterpart:

```text
error DIE005: The composition root "Chats" duplicates the previously declared root "".
```

The limitation makes shared setups containing MS DI roots inconvenient for direct composition consumers. Workaround: separate reusable server bindings (`AI.Server.Composition`) from MS DI roots (`AI.Server.AspNetComposition`). Pure.DI-only fixtures now declare their own named roots; Host, Desktop and startup tests opt into ASP.NET roots.

## Lightweight anonymous roots and shared deferred dependencies

Status: Existing project workaround, preserved; not independently reproduced during this refactoring.

The original server setup records that Pure.DI 2.5.4 could use the shared `AppDataChangeSignal` before initialization when building deferred `AppWrites` factories, giving `AppWrites` a null dependency. Keep `.Hint(Hint.LightweightAnonymousRoot, "Off")` until a supported version is verified with the production graph.

## Several DependsOn names in one call

Status: Existing project workaround, preserved; not independently reproduced during this refactoring.

The original Host/Desktop setups record DIE043 for `.DependsOn("a", "b")` in Pure.DI 2.5.4. The project keeps one call per setup. Recheck this restriction when updating the package rather than assuming the newer local reference documentation describes the pinned binary exactly.

## Verification after applying the workarounds

The final graph builds with `dotnet build AI.slnx --no-restore -m:1 /nodeReuse:false`: zero errors and zero warnings. Host, Desktop and Build command-line roots also handle `--help` successfully.

The compiled test assemblies passed through their native runners: Server 788 passed / 1 skipped, Web 427 passed, TextCorrection 175 passed, CSharp MCP 14 passed. The skipped test checks Unix file-owner permissions and is skipped on Windows. These checks include production ASP.NET startup, external MCP discovery, preview tickets across requests, specialized tool presentation, shared correction preparation, and chat execution.
