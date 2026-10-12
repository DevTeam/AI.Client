# Pure.DI observations and issues

This file collects complaints, generator failures, and configuration limitations separately from the project's DI conventions. The original observations below were made with Pure.DI / Pure.DI.MS 2.5.4. The project now pins 2.5.5; because those packages are not yet available for restore, the changes were verified on 2026-10-13 against local Pure.DI generator source at `ea3452cf0`. The older observations remain as history; their current status is summarized here.

| Observation | Current status with local Pure.DI source |
| --- | --- |
| Enumerable escapes its scope | Fixed in Pure.DI; `CompositeToolSessionFactory` is transient. |
| Simplified bindings expose `IAsyncDisposable` | Fixed in Pure.DI; `ChatReplySuggestions` and `DesktopUnreadCountPublisher` use lifetime-specific bindings. |
| Constructor selection | Fixed in Pure.DI; `JsonLineFileLoggerProvider` uses a lifetime-specific binding. |
| `DependsOn` with multiple names | Fixed in Pure.DI; Host, Desktop, and startup tests use one call. |
| Shared singleton initialized inside deferred policy collection | Fixed in Pure.DI; `GuideChatKindPolicy` injects `IProjectService` directly. |
| Lightweight anonymous roots | Still fails in the production graph with `CS0103` for `perBlockFuncIChatKindPolicyRegistry`; keep the hint disabled. |
| Contract inference, inherited adapters, duplicate roots | Configuration limits; retain the explicit bindings and separate root setup. |

## Generated enumerable escapes its declaration scope

Status: Reproduced with 2.5.4; fixed in the local generator source.

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

The 2.5.4 workaround was `.Singleton<CompositeToolSessionFactory>()` with the original enumerable constructor contracts. It is no longer used: the current graph registers this stateless factory as transient. These observations describe the production graph, not an independently minimized reproduction.

## Simplified bindings expose disposal interfaces

Status: Reproduced with 2.5.4; fixed in the local generator source.

Moving `DesktopUnreadCountPublisher` out of the shared singleton binding group into `.Transient<DesktopUnreadCountPublisher>()` caused:

```text
error DIW000: The binding for System.IAsyncDisposable has been overridden.
```

Using `.Singleton<ChatReplySuggestions>()` separately from other server services produced the same warning. The project treats warnings as errors. Pure.DI now excludes `IAsyncDisposable` from inferred contracts, so both services can use lifetime-specific bindings. Disposal is still tracked by Pure.DI.

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

Status: Reproduced with 2.5.4; fixed in the local generator source.

With simplified bindings, the generator selected the string constructor of `JsonLineFileLoggerProvider` even though this graph supplies `IProjectStorageLocation`:

```text
error DIE000: Unable to resolve "string" in JsonLineFileLoggerProvider(string rootDirectory<--string).
```

The type also has a production constructor `(IProjectStorageLocation location, int retentionDays = 14)`. The current generator selects the resolvable production constructor, so `.Singleton<JsonLineFileLoggerProvider>()` is used.

## Anonymous and named roots cannot duplicate one contract

Status: Reproduced during this refactoring. Configuration limitation; the diagnostic itself is valid.

A Pure.DI-only fixture that inherited an anonymous `IChatService` root could not add its named counterpart:

```text
error DIE005: The composition root "Chats" duplicates the previously declared root "".
```

The limitation makes shared setups containing MS DI roots inconvenient for direct composition consumers. Workaround: separate reusable server bindings (`AI.Server.Composition`) from MS DI roots (`AI.Server.AspNetComposition`). Pure.DI-only fixtures now declare their own named roots; Host, Desktop and startup tests opt into ASP.NET roots.

## Lightweight anonymous roots and shared deferred dependencies

Status: Reproduced on 2026-10-12 with local Pure.DI master after the other graph changes.

The original server setup records that Pure.DI 2.5.4 could use the shared `AppDataChangeSignal` before initialization when building deferred `AppWrites` factories, giving `AppWrites` a null dependency. Removing `.Hint(Hint.LightweightAnonymousRoot, "Off")` with the current local generator instead fails to compile `StartupComposition.g.cs`: `perBlockFuncIChatKindPolicyRegistry` is declared inside a nested factory and referenced by the outer lightweight root initializer. Keep the hint until both generation and runtime behavior are verified.

## Several DependsOn names in one call

Status: Fixed in local Pure.DI source and removed in AI.Client.

The original Host/Desktop setups record DIE043 for `.DependsOn("a", "b")` in Pure.DI 2.5.4. Host, Desktop, and startup tests now use one call with both setup names.

## Historical verification with 2.5.4

The final graph builds with `dotnet build AI.slnx --no-restore -m:1 /nodeReuse:false`: zero errors and zero warnings. Host, Desktop and Build command-line roots also handle `--help` successfully.

The compiled test assemblies passed through their native runners: Server 788 passed / 1 skipped, Web 427 passed, TextCorrection 175 passed, CSharp MCP 14 passed. The skipped test checks Unix file-owner permissions and is skipped on Windows. These checks include production ASP.NET startup, external MCP discovery, preview tickets across requests, specialized tool presentation, shared correction preparation, and chat execution.
