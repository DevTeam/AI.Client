# Dependency injection with Pure.DI

Status: Accepted

Apply these rules when adding or changing services, composition setups, and composition consumers.

## Contracts and bindings

Services take interfaces in constructors and injection sites. Use concrete types for implementations in the composition, executable or framework entry points such as MCP `Program`, `BuildApplication`, Avalonia `App` and `MainWindow`, and data values. Do not introduce class-to-class bindings for application services. When several implementations share an interface, use tags or a distinct interface for a distinct responsibility.

Prefer the lifetime-specific API:

```csharp
DI.Setup()
    .Hint(Hint.Comments, "Off")
    .Hint(Hint.Resolve, "Off")
    .Transient<ProjectService, ChatService>()
    .Singleton<JsonProjectRepository, JsonChatRepository>()
    .Root<IProjectService>(nameof(Projects));
```

`Transient<Implementation>()` and `Singleton<Implementation>()` register the implementation and its supported direct abstract contracts. Inject the contracts rather than relying on the concrete registration. Use `Bind<IContract>().To<Implementation>()` when an interface is inherited through a base class, or automatic registration would expose unwanted contracts, override another implementation, or include a fallback implementation in a collection of specialized services. Explicit bindings are also appropriate for factories whose result must be restricted to one contract.

The specialized tool presentation adapters use `Tag.Unique` for collection injection. The generic fallback is explicitly bound to `IGenericToolPresentationAdapter` so it does not become an always-matching entry in that collection. Skill executors use tagged `ISkillExecutor` dependencies; bundled skills use a tagged `ISkillCatalog`. The application vocabulary is a tagged `IWordLexicon`, separate from the main Hunspell lexicon.

## Lifetimes

Use `Transient` by default. A stateless service can capture shared singleton dependencies without being a singleton itself. Do not cache an object in the composition simply because it happens to live for the duration of its owner.

Use `Singleton` when a graph must share identity or state: repository write gates, chat synchronization, dispatch queues, change/navigation signals, run registries, file-preview tickets, usage accounting, caches, or the running update service. Shared expensive caches, such as dictionaries and parsed bundled skills, also justify a singleton. Check all consumers before changing a lifetime: analysis and dictionary preparation must use the same lexicon/model caches, and API endpoints and background runs must use the same dispatcher and signals.

If a service is consumed only by one singleton, normally make that dependency transient. The owning singleton retains the one instance it needs, without an additional generated cache field. Check deferred factories first: each invocation of `Func<T>` can create another transient instance. Keep a singleton when those invocations must return the same object. Keep disposal ownership and concurrency requirements intact.

## Roots

Declare a root only when code outside the graph resolves it. Constructor dependencies and dependencies reached through factories do not need roots.

Prefer named roots for direct composition consumers. Use `nameof(Projects)` for a generated member named `Projects`, rather than a string literal. Place these roots in the final partial composition, where Pure.DI generates the member; an internal shared setup does not generate members that `nameof` could reference.

Anonymous roots are used for the `Pure.DI.MS` service-provider bridge, including Blazor injection, ASP.NET endpoint parameters and Microsoft DI hosted-service dependencies. Declare only contracts actually requested at that boundary. The server's shared bindings are in `AI.Server.Composition`; its ASP.NET roots are in `AI.Server.AspNetComposition`. Host, Desktop and startup tests use the latter and declare their own named `Server` root. Pure.DI-only test fixtures use the shared bindings and declare named roots for their own access.

## Generation hints and concurrency

- Set `.Hint(Hint.Comments, "Off")` on setups to suppress generated API documentation and reduce generation work. Source comments explaining composition decisions remain useful.
- Set `.Hint(Hint.Resolve, "Off")` when consumers use named members and no runtime resolution or MS DI bridge needs generated `Resolve` methods.
- Set `.Hint(Hint.ThreadSafe, "Off")` only when composition access, including deferred factories and disposal, is confined to one thread or externally serialized. Command-line/build compositions and desktop UI startup use this optimization. Server and standalone text-correction compositions keep thread safety because their roots or factories can be accessed concurrently. WebAssembly composition follows the current single-threaded browser model; reassess this hint if worker threads are enabled.
- Avoid enabling generated code formatting and diagnostic output in normal builds.

## Package-specific workarounds

The project pins Pure.DI 2.5.4. Current limitations, diagnostics and evidence are recorded separately in [Pure.DI observations and issues](pure-di-issues.md). Preserve the documented workarounds until their removal is verified against every linked consumer. Do not suppress binding diagnostics to conceal unintended contract overrides.

## Verification

Build all linked consumers after changing a shared setup. Exercise the service-provider bridge and existing startup, tool presentation, skill, chat execution, and text-correction tests. Successful generation alone does not prove that caches, signals, collection ordering, or disposal still behave correctly.

Pure.DI reference documentation is maintained in `C:\Projects\DevTeam\Pure.DI\README.md` and `C:\Projects\DevTeam\Pure.DI\readme`.
