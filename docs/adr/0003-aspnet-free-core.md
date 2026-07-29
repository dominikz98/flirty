# ADR 0003 – ASP.NET-free core, web as an opt-in package

- **Status:** Accepted
- **Context issue:** #13 – project skeletons + solution wiring (implemented in #35/#36)
- **Affected:** `src/Flirty`, `src/Flirty.AspNetCore`, `src/Flirty.Samples`, `src/Flirty.Samples.Web`

## Context

Flirty should run in a console/worker app just as it does behind a WebAPI – an
onboarding dialog in a background service is just as valid a use case as a
chat widget in the browser. At the same time, consumers who *do* want HTTP should not have to write
endpoints by hand.

An ASP.NET reference in the core would have two effects, both of which land on the consumer: every
console app would pull in the shared framework `Microsoft.AspNetCore.App`, and the error semantics of the
engine would become HTTP-colored (status codes instead of exceptions), even though there is no HTTP there.

## Decision

`Flirty` is a **pure class library with no ASP.NET dependency**. Everything web-specific lives in the
**separate, opt-in package `Flirty.AspNetCore`**, the only project that sets
`<FrameworkReference Include="Microsoft.AspNetCore.App" />`.

The web layer stays deliberately **thin**: `MapFlirtyEndpoints` (runtime) and
`MapFlirtyAdminEndpoints` (admin CRUD, opt-in and securable via `RequireAuthorization()`) send
the commands/queries directly through `ISender` and only map to request/response DTOs. A single endpoint
filter (`src/Flirty.AspNetCore/FlirtyExceptionEndpointFilter.cs`) translates the engine's domain
exceptions into `ProblemDetails` – 404 (not
found), 400 (validation), 409 (state conflict). **There is no logic that is reachable only over HTTP.**

## Discarded alternatives

- **One package with an ASP.NET reference.** Console/worker consumers would pull in the ASP.NET shared framework,
  even though they never use it. The design damage weighs heavier: the engine would answer in
  HTTP terms (status codes, `ProblemDetails`), and that semantics would have to be artificially
  translated back in the console.
- **One package, endpoints via `#if` or an additional target framework.** The consumer would have to choose the
  variant at **build time**, the test matrix doubles, and the public API of the
  package would differ depending on the build – for a NuGet package the worst of all variants.
- **Reference ASP.NET "softly"** (e.g. `PrivateAssets`). Solves nothing: as soon as endpoint types appear in
  public signatures (`IEndpointRouteBuilder`), the dependency is real.
- **Ship no endpoints at all**, everyone builds them themselves. Exactly the repetitive work that Flirty
  is meant to take off your hands – endpoint mapping, DTOs and error mapping are the same for every consumer.

## Consequences

**Positive**

- The invariant is checked on **every build**, not merely asserted: `Flirty.Samples` is a
  runnable console sample whose only project reference is `Flirty`. Were an
  ASP.NET dependency to sneak into the core, it would show up there.
- The engine's error semantics are **transport-neutral** (exceptions); the translation to HTTP
  lives in exactly one place.
- Reinforced by [ADR 0002](./0002-mediator-as-in-process-bus.md): because the Mediator source
  generator only sees the core compilation, `Flirty.AspNetCore` **cannot** contribute handlers –
  the layer stays technically forced to be thin.

**Negative**

- Two packages have to be versioned, packaged and published (a shared,
  date-based version, see [NUGET-PACKAGING.md](../NUGET-PACKAGING.md)).
- The DTO/mapping layer in `Flirty.AspNetCore` is additional code that has to be carried along with every change to
  a runtime or admin command.

Details: [ARCHITECTURE.md](../ARCHITECTURE.md), [GETTING-STARTED-WebApi.md](../GETTING-STARTED-WebApi.md),
console side in [GETTING-STARTED-Console.md](../GETTING-STARTED-Console.md).
