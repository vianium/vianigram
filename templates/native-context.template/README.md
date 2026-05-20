# Native bounded-context template (C++/CX, WinRT Component)

This template is the canonical layout for a **native** bounded context.
Most native contexts in the Vianium family now live in their own sibling
repos (e.g. `vianium-mtproto`, `vianium-crypto`, `vianium-voip`); inside
this Vianigram repo it applies primarily to `Vianigram.Core.Media`. The
template mirrors the structure used by the foundation siblings
(`vianium-net`, `vianium-tls`, `vianium-http`).

Build target: WP8.1, MSBuild 14.0, platform toolset `v120_wp81`,
`AppContainerApplication=true`, `Keyword=WindowsRuntimeComponent`.

## How to use

1. Copy the entire `native-context.template` folder into `Core/` (or into
   a new sibling repo if extracting a new bounded context).
2. Rename it to `Vianigram.Core.<ContextName>` (or `Vianium.<ContextName>`
   if it is going to live in its own sibling repo).
3. Author a `<ProjectName>.vcxproj` cloned from
   `..\vianium-net\Vianium.Core.Net.vcxproj`:
   - generate a fresh `<ProjectGuid>`
   - set `<ProjectName>` and `<RootNamespace>` to the new context name
   - configure `<TargetName>` (the produced WinMD)
   - update `<AdditionalIncludeDirectories>` for any cross-project
     headers (sibling repos referenced via `..\vianium-<name>\...`)
4. Add the project to `Vianigram.sln` under the `Native` solution folder
   (only for contexts living in this repo), with
   `Debug|Win32`, `Debug|ARM`, `Release|Win32`, `Release|ARM`
   configurations.
5. Replace each `.gitkeep` with real `.h`/`.cpp` files as the layer is
   implemented.

## Folder responsibilities

| Folder              | Contents                                                                                         |
| ------------------- | ------------------------------------------------------------------------------------------------ |
| `src/domain/`       | Pure C++ domain: value objects, domain entities, domain errors. No WinRT, no `^`, no ref classes |
| `src/application/`  | Pure C++ use cases coordinating domain + outbound ports. Returns `expected<T, Error>` style      |
| `src/ports/`        | Pure C++ port interfaces (abstract base classes) for outbound dependencies                       |
| `src/infrastructure/` | Concrete adapters implementing ports. May use Win32, WinHTTP, BCrypt, etc.                     |
| `src/api/v1/`       | WinRT-projected API surface (`ref class`, `^`, `Platform::String^`). The only WinRT layer         |
| `src/internal/`     | Translation utilities between WinRT API surface and pure-C++ application layer                   |

## Conventions

- Only `src/api/v1/` files use `CompileAsWinRT=true`-specific syntax. Domain,
  application, ports and infrastructure are pure C++ for portability and ease of
  unit testing.
- Domain types must never include `<windows.h>` or `<winrt/...>`.
- Cross-context calls go through WinRT projections — never raw C++ headers
  exported across project boundaries.
- Every public WinRT type lives under `Vianigram.<ContextName>` (for
  in-repo contexts) or `Vianium.<ContextName>` (for sibling repos)
  namespace and is versioned (`Api/V1`, `Api/V2`, ...).
