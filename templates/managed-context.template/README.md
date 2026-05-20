# Managed bounded-context template (C# / WP8.1 RT)

This template is the canonical layout for a **managed** bounded context inside
Vianigram (e.g. `Vianigram.Account`, `Vianigram.Chats`, `Vianigram.Settings`).
It mirrors the structure used by the managed contexts that live in the
sibling `vianium-managed-kernel\` repo and is designed for Hexagonal/DDD
layering.

## How to use

1. Copy the entire `managed-context.template` folder into `Core/`.
2. Rename it to `Vianigram.<ContextName>` (e.g. `Vianigram.Account`).
3. In every `.cs` you add, set the namespace to `Vianigram.<ContextName>.<Layer>`.
4. Create a `Vianigram.<ContextName>.csproj` cloned from `Vianigram.Kernel.csproj`:
   - generate a fresh `<ProjectGuid>`
   - set `<RootNamespace>` and `<AssemblyName>` to `Vianigram.<ContextName>`
   - add a `ProjectReference` to `Vianigram.Kernel`
5. Add the project to `Vianigram.sln` under the `Vianigram` solution folder.
6. Replace each `.gitkeep` with real source files as the layer is implemented.

## Folder responsibilities

| Folder                     | Contents                                                                                  |
| -------------------------- | ----------------------------------------------------------------------------------------- |
| `Domain/`                  | Pure domain types: entities, value objects, domain events, domain services, domain errors |
| `Application/UseCases/`    | One class per use case; coordinates domain + outbound ports; returns `Result<T, Error>`   |
| `Ports/Inbound/`           | Interfaces this context exposes to the outside world (typically `I<Context>Api`)          |
| `Ports/Outbound/`          | Interfaces this context requires (storage, network, time, capability checks)              |
| `Infrastructure/`          | Concrete adapters implementing outbound ports (storage, ACL, native bridges)              |
| `Api/V1/`                  | Stable, versioned facade over inbound ports — what the composition root wires             |

## Conventions

- Domain code never references `System.Diagnostics`, the network, or storage. It
  depends only on `Vianigram.Kernel` and pure types.
- Application use cases never throw across boundaries; they return
  `Result<T, Error>`.
- The composition root is the only place that constructs concrete infrastructure
  types. Ports are resolved by the root, never `new`'d inside the context.
- Bumping `Api/V1` to `Api/V2` is allowed; both can coexist while consumers migrate.
