# tl-codegen

A .NET 4.6.1 console tool that reads a Telegram TL schema file (`.tl`) and emits
C++ headers + sources for the TL serializer that ships in the sibling repo
`vianium-mtproto` (under `src\tl\`). Vianigram consumes the resulting WinMD;
the generated sources never land in this repo.

This is the schema codegen described in
[`docs/native-port/02-tl.md`](../../docs/native-port/02-tl.md).

## Build

```sh
cd tools/tl-codegen
dotnet build
```

The executable lands at `bin/Debug/tl-codegen.exe` (or `bin/Release/tl-codegen.exe`).

## Usage

```sh
.\bin\Debug\tl-codegen.exe \
    --schema schemas/scheme-layer-214.tl \
    --header ../../../vianium-mtproto/src/tl/infrastructure/generated/tl_layer_214.h \
    --source ../../../vianium-mtproto/src/tl/infrastructure/generated/tl_layer_214.cpp \
    --namespace vianium::tl::layer214 \
    --layer 214
```

### Flags

| Flag | Required | Description |
|------|----------|-------------|
| `--schema` | yes | Path to the input `.tl` schema. |
| `--header` | yes | Path to the C++ header to emit. |
| `--source` | yes | Path to the C++ source to emit. |
| `--namespace` | no | C++ namespace (default `vianium::tl`). Use `::` separator. |
| `--layer` | no | Layer number, embedded as `kSchemaLayer` in the header (default `214`). |

## Layout

```
tools/tl-codegen/
├── README.md
├── tl-codegen.csproj
├── schemas/
│   └── scheme-layer-214.tl     # auth-flow subset of the layer 214 schema
└── src/
    ├── Program.cs              # CLI entry point + arg parsing
    ├── TlLexer.cs              # tokenizer for TL grammar
    ├── TlAst.cs                # AST node definitions
    ├── TlParser.cs             # token stream -> AST
    └── TlEmitter.cs            # AST -> C++ header / source
```

## Generated code shape

For each TL type, the emitter produces a struct:

```cpp
// auth.sentCode#5e002502
struct Tl_auth_sentCode {
    static constexpr uint32_t kConstructorId = 0x5e002502u;
    Tl_auth_SentCodeType type;
    std::string phone_code_hash;
    Tl_auth_CodeType next_type;   // valid if (flags & (1u << 1))
    int32_t timeout;              // valid if (flags & (1u << 2))

    bool Serialize(TlWriter& w) const;
    static bool Deserialize(TlReader& r, Tl_auth_sentCode& out);
};
```

`Serialize` writes `kConstructorId` first (boxed encoding) and then each
field in declaration order. Optional fields are gated on the matching
`flags` bit. Vectors emit the `0x1cb5c415` magic followed by the count.

`Deserialize` reads the constructor id, verifies it matches
`kConstructorId`, and then reads each field in declaration order.

## Scope

The schema in `schemas/scheme-layer-214.tl` covers the **auth flow** only:
~10 types and 4 functions, enough for `auth.sendCode` and the MTProto
DH handshake. Adding the remaining ~800 types is Phase 1 work and is
intentionally out of scope for this initial commit. Append more
declarations to the `.tl` file and re-run the tool.

## Constructor id verification

The codegen accepts the constructor id declared in the `.tl` file as the
ground truth. CRC32-based verification (per the TL spec) is not yet
implemented — track that work under issue tag `tl-codegen/ctor-hash-verify`.

## Layer upgrade

To add layer 220 (or any future layer):

1. Drop `schemas/scheme-layer-220.tl` next to the layer 214 file.
2. Run the codegen with `--layer 220 --header tl_layer_220.h --source tl_layer_220.cpp`.
3. Add the new `.cpp` to the sibling `vianium-mtproto\Vianium.Mtproto.vcxproj`.
4. Wire it into `TlSchema::LoadLayer(220)` in
   `..\vianium-mtproto\src\tl\application\use_cases\load_schema_use_case.cpp`.
