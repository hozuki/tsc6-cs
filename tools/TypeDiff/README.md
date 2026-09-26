# TypeDiff

The checker-level differential (docs/architecture.md §7's outer loop, M6-7): check the files of a corpus twice — once
with the port's `Program` + `Checker` and once with the reference `lib/typescript.js` — dump what each type checker
wrote for a file in one canonical line format, hash it, and report the files whose hashes differ together with their
first differing line pair. It is `tools/BindDiff`'s shape one layer up: same manifest, same FNV-1a hash over UTF-16 code
units, same per-batch `node` process, same "write a full dump only for diverging files", same self-reported peak.

## Command lines

The whole differential (the port half needs the checker's assembly slice; see *the M6-5 boundary* below):

```
dotnet run --project tools/TypeDiff -- --cases <TypeScript>/tests/cases/conformance --filter "classes/**"
```

The reference half on its own — it needs nothing but `node`, and this is how the format is measured and re-measured:

```
node tools/TypeDiff/ts-type-dump.cjs \
  --manifest <manifest.json> --out <dir> \
  --js <TypeScript>/lib/typescript.js [--strict]
```

A manifest is `{"root": "<corpus root>", "files": [{"path": "<root-relative>", "kind": "TS|TSX|JS|JSX"}]}`; the driver
writes one per batch under `<out>/manifests/`. With no `--port-hashes` file present every file counts as diverged and no
dump is written, which is what a reference-only run reports.

Options of the driver: `--cases` (required), `--filter` (glob over the root-relative path, `*` does not cross `/`, `**`
does), `--limit`, `--out` (default `<repo>/artifacts/typediff`), `--js`, `--node`, `--node-script`, `--node-batch`
(default 250), `--strict`. Both halves are handed the same options: `target: ESNext`, `allowJs: true`, and `strict`
under `--strict`; each file is a one-file program with a host that serves that file and nothing else (no default
library, no module resolution).

Outputs, all in `--out`: `port-hashes.txt` / `ts-hashes.txt` (`<rel>\t<16-hex-hash>\t<length>`), `port-crashes.txt` /
`ts-errors.txt`, `port/<rel>.txt` and `ts/<rel>.txt` (the diverging files' full dumps), `ts-summary.txt` (one line per
node batch: `files=/same=/diverged=/ts-error=/gave-up=/typescript=/strict=/maxRSS=`), `report.txt` and
`divergences.jsonl`. Exit codes: `0` every hash matched, `1` divergences or crashes, `2` bad arguments, `3` node could
not run, `4` the port cannot create a checker yet.

## The canonical dump (frozen v1)

Identical text on both sides for equal checker state; LF only, no trailing spaces, one record per line, numbers in
invariant culture:

```
N<depth> <kind>(<pos>,<end>) sym=<#s|-> type=<#t|-> sig=<#g|->
X0 config=<[#d,…]|-> syntactic=<[#d,…]|-> options=<[#d,…]|-> global=<[#d,…]|-> semantic=<[#d,…]|->
S#<id> flags=<n> flagsName=<names|-> name=<escaped> decls=<n> members=<[name,…]|-> exports=<[name,…]|->
T#<id> flags=<n> flagsName=<names|-> id=<n> obj=<n|-> sym=<#s|-> alias=<#s|-> types=<[#t,…]|-> props=<[name,…]|-> sigs=<[#g,…]|->
G#<id> decl=<kind(pos,end)|-> minArgs=<n> params=<[#s,…]|-> ret=<#t|-> tp=<[#t,…]|->
D#<id> code=<n> start=<n|-> len=<n|-> cat=<n>
```

Rules:

* **One record per entity per file.** An `N` line is one node in `forEachChild` pre-order (depth 0 is the source file),
  with `getSymbolAtLocation`/`getTypeAtLocation` for it and `getResolvedSignature` for the call-like kinds
  (`CallExpression`, `NewExpression`, `TaggedTemplateExpression`, `JsxOpeningElement`, `JsxSelfClosingElement` — the
  reference's `isCallLikeExpression` minus `Decorator`). The five `X0` lists are the program's diagnostics for that file
  in `getPreEmitDiagnostics`' order (config, syntactic, options, global, semantic). An `S` line is a symbol (its
  `SymbolFlags` word, that word's members, its escaped name, its declaration count and its `members`/`exports` keys), a
  `T` line a type (`TypeFlags`, the checker's own type id, `ObjectFlags`, symbol, alias symbol, a union's/intersection's
  members, the properties the checker resolves, its call/construct signatures), a `G` line a signature, a `D` line a
  diagnostic.
* **Every record prints once, every reference is `#id`.** Ids are per file per record kind, assigned in order of first
  reference, starting at `#1`; absent is `-`. Nested descriptions would make the dump grow with the number of
  references rather than with the graph (docs/architecture.md §15.13), and per-reference renumbering would re-print a
  shared subgraph once per reference.
* **Nothing carries a process-global number.** The reference bakes `getSymbolId` into two of its synthetic names
  (`utilities.ts:5450`'s `__#<id>@<description>`, `checker.ts:20345`'s `__@<name>@<id>`); those ids are normalized to
  `?`, which is what makes a file's dump identical whether it was checked alone or after a thousand others. (Measured:
  one 1311-file batch and six 250-file batches produce byte-identical hash files, and a reversed-order run produces the
  same per-file hashes.)
* **`flagsName`** is the flag word's set bits rendered as members: single-bit members only, ascending bit order, and for
  a bit several members share (`AliasExcludes = Alias`) the lexicographically smallest name — so the text does not
  depend on either side's declaration order. `|?` marks bits no single-bit member covers.
* **`obj`** is `ObjectFlags` for the models that carry the slot (object, union/intersection, intrinsic, substitution)
  and `-` when it is unset; the reference leaves the slot `undefined` where nothing wrote it and the port's models
  declare it as a non-nullable enum, so both sides read zero as `-`.
* **A node the reference's own API cannot answer for prints `sym=! type=! sig=!`.** `getTypeAtLocation` dereferences a
  module source file's missing parent and a parse-recovery declaration can have no symbol; `!` is data, so both halves
  must still agree on where the reference gave up, and each batch's `gave-up=` count reports how many records carried
  it. Over `tests/cases/conformance/{types,classes}` (1311 files) the reference gives up 51 times in all: once per
  module source file and twice in a `// @filename:`-concatenated conformance input.
* **Names** (an escaped name, a table key, a property name) are escaped: a space, any control character and `DEL` become
  `\uXXXX`, and `\`, `\n`, `\r`, `\t` are backslash-escaped. UTF-16 code units on both sides, so a surrogate pair passes
  through untouched.
* **Nothing is sorted except the file list.** The `members`/`exports` keys and a type's properties print in the
  compiler's own insertion order, because that order is itself checker state (§15.15's `SymbolTable` finding).

## The M6-5 boundary

The port half calls `program.GetTypeChecker()`, whose factory — `Checker.CreateTypeChecker(ITypeCheckerHost)` — and the
`Checker : ITypeChecker` assembly are M6-5's (docs/architecture.md §15.15 ruling 4). Until they land there is no checker
to create, so the driver checks the model itself (`typeof(ITypeChecker).IsAssignableFrom(typeof(Checker))`) before it
writes anything, prints a message naming M6-5, and exits `4`. Nothing is stubbed: that probe *is* the boundary, and the
moment M6-5 lands the port pass runs like any other caller.
