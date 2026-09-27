# BaselineDiff

The baseline differential (M7-C's outer ring): replay a corpus with the **official test harness's option model** — the
per-file `// @` directives, the harness's own defaults, and the configurations `CompilerTest.varyBy` derives from them —
render each case's baseline texts exactly as `tests/baselines/reference` holds them, and compare the port's bytes
against the checked-in baseline.

The three rings that came before it all share one fixed option object (`tools/TypeDiff`, `tools/EmitDiff`) or no program
at all, so none of them exercises a `// @` directive. This is the only ring whose third party is not the reference
compiler but a file.

## Command lines

The whole differential:

```
dotnet run --project tools/BaselineDiff -- \
  --cases D:/Source/GitHub/TypeScript/tests/cases/conformance \
  --baselines D:/Source/GitHub/TypeScript/tests/baselines/reference \
  --node-parallel 8
```

A shard (the driver walks the corpus in sorted order, so a shard is reproducible):

```
dotnet run --project tools/BaselineDiff -- --cases <corpus> --baselines <baselines> --filter "es6/**" --limit 200
```

The reference half on its own — it needs nothing but `node`, and this is how the harness model is measured and
re-measured:

```
node tools/BaselineDiff/ts-baseline.cjs \
  --manifest <manifest.json> --out <dir> --js <TypeScript>/lib/typescript.js \
  --baselines <TypeScript>/tests/baselines/reference --hashes <f> --cases <f> --errors <f> --summary <f> [--dumps <dir>]
```

Options: `--cases` (required), `--baselines` (required), `--filter` (glob over the root-relative path, `*` does not
cross `/`, `**` does), `--limit`, `--out` (default `<repo>/artifacts/baselinediff`), `--js`, `--lib` (mounted at `/.ts`,
default `<workspace>/lib`), `--test-lib` (mounted at `/.lib`, default `<workspace>/tests/lib`), `--workspace`,
`--node`, `--node-script`, `--node-batch` (default 250), `--node-parallel` (default 1),
`--artifacts js,errors,types,symbols`, `--types-underlines true|false`, `-h`/`--help`.

`--types-underlines false` renders `.types` the way a baseline set whose generator does not fill the underline
annotation compares: tsgo's `testdata/baselines` is such a set (its harness prints the line when
`result.underline` is non-empty, but the producer never sets it — `internal/testutil/tsbaseline/type_symbol_baseline.go`
lines 403/413, "!!! TODO: port underline printer"). The port's own rendering keeps TS's behaviour by default, so the
flag exists to compare against those baselines, not to change what the port produces.

## The three-way comparison

| verdict | what it means |
|---|---|
| **port vs baseline** | the ring's verdict. A divergence is a port defect, or a divergence inside a baseline written by something other than this reference version. |
| **reference vs baseline** | the harness model's own check. The reference half runs the same model with `lib/typescript.js`; if it cannot reproduce the checked-in bytes, the model is wrong and every port verdict from that case is worth nothing. Reported separately, never folded into the port's. |
| **extra-baseline** | a checked-in baseline whose stem no derived case named — the configuration model is missing a case the harness ran. Only checked on a whole-corpus run (a shard's baselines mostly belong to files it never derived). |

## The harness model

The model is the reference's own, not a second opinion:

| part | source |
|---|---|
| `// @` directives, unit split, `@link` | `src/harness/harnessIO.ts:1219-1390` (`TestCaseParser`) |
| the option declarations and value conversion | `src/harness/harnessIO.ts:255-325` (+ the harness's 15 own options) over `src/compiler/commandLineParser.ts:1184-2900` |
| the option model table | derived from `lib/typescript.js` 6.0.3's own `optionDeclarations` into `tools/BaselineDiff/BaselineDiffOptions.cs` (143 entries with kind, `isFilePath`, `isCommandLineOnly`, the `affects*` flags and the custom-type tables) — the port half reads it, `ts-baseline.cjs` reads the live table |
| `target`/`module`/`lib`/`strict`/`newLine` defaults | `src/harness/harnessIO.ts:333-380` + `src/harness/compilerImpl.ts:265-300` (`newLine=CRLF`, `noErrorTruncation`, `skipDefaultLibCheck`, `noResolve:false`) |
| the varying configurations and the stem | `src/testRunner/compilerRunner.ts:129-350` + `src/harness/harnessIO.ts:1078-1175` (`<base>(key=value,…).ts`, keys and values lower-cased and sorted) |
| the virtual file system | `src/harness/vfsUtil.ts:25-30, 1261-1300, 1573-1580`: documents under `/.src`, `/.ts` mounted at `<TypeScript>/lib` (the harness mounts `built/local`; the checkout has no `built/`), `/.lib` at `tests/lib`, case-sensitive unless `@useCaseSensitiveFileNames` |
| the host | `src/harness/fakesHosts.ts:244-430` — `jsDocParsingMode: ParseForTypeErrors`, `setParentNodes: true` (light mode is off), outputs collected through `writeFile` |
| `.errors.txt` | `src/harness/harnessIO.ts:494-661` (`getErrorBaseline`) |
| `.js` (and the declaration output inside it) | `src/harness/harnessIO.ts:910-1005` (`doJsEmitBaseline`) |
| `.types` / `.symbols` | `src/harness/typeWriter.ts` + `src/harness/harnessIO.ts:663-860` (incl. the `=== Performance Stats ===` block) |
| the header path | the case file's path relative to the workspace root (`tests/cases/conformance/…`) — what `CompilerBaselineRunner` enumerates |

Two asymmetries are deliberate and recorded: the reference half's host reads `lib/` where the harness reads
`built/local` (same sources, no build in the checkout), and the port half reads its own `ICompilerOptions` slots by
reflection because the port's model is a generated interface, not a JS object literal.

## The hash-file format

Both halves write one line per (baseline stem, artifact):

```
<stem>\t<kind>\t<state>\t<hash>\t<length>
```

* `state` is `text` (a baseline text was produced), `null` (the generator answered `null`, which the baseline machinery
  compares as the literal `<no content>` — its hash is on the line), `skipped` (the harness does not check this baseline
  for this case: an all-`.d.ts` test has no `.js` baseline, `@noTypesAndSymbols` turns both type baselines off),
  `error` or `unmodelled`.
* Only `text` and `null` can compare as agreement. A crash, a model gap, an unpaired row and an extra baseline each
  mean something was *not* compared, and a run that compared nothing must never read as clean.
* The expected side is read from the baseline directory by the driver, with the UTF-8 BOM stripped exactly as the
  harness's `sys.readFile` strips it; an absent baseline is `<no content>`.

## Outputs

| file | contents |
|---|---|
| `port-hashes.txt` / `ts-hashes.txt` | the (stem, kind) verdict lines of each half |
| `port-errors.txt` / `ts-errors.txt` | one line per case that failed or could not be modelled |
| `report.txt` | the `DIVERGE`/`REF-DIVERGE`/`PORT`/`TS` lines, the per-artifact table, `BASELINE-SIDE`, `TS-SIDE`, the peak, `SUMMARY` |
| `divergences.jsonl` | the same divergences as JSON objects |
| `port/<baseline>` / `ts/<baseline>` | the diverging artifacts' full texts (the port's is re-derived for that one case) |
| `ts-summary.txt` | one line per node batch |
| `manifest.json`, `node/` | what the reference half was handed (removed again when the run ends) |

Exit codes: `0` every artifact matched, `1` divergences or failures, `2` bad arguments, `3` node could not run — the
same contract the sibling tools carry, and the same one-line audit trail on stderr:

```
baselinediff: VERDICT fail cases=... same=... port-diverge=... port-crash=... port-unmodelled=... ref-diverge=... ref-crash=... unpaired=... extra-baseline=... exit=1
```

## What is not modelled (and is reported, never defaulted)

| gap | cases | why |
|---|---|---|
| `tsconfig.json` as a test unit | 10 conformance files | the port has no `parseJsonSourceFileConfigFileContent` (M8) |
| `// @link:` symlinks | 0 in the conformance corpus | the harness applies them with `fs.apply` |
| `@paths` / `@plugins` | 0 in the corpus | object-typed options; the reference's own `optionValue` throws on them |
| `@pretty: true` | 1 file | needs `getErrorSummaryText`/`getErrorCountForSummary`/`getFilesInErrorForSummary`, not ported |
| the `noCheck` comparison's diff | 9 `.js` baselines | the harness prints `diff`'s `createTwoFilesPatch`; jsdiff is the reference's npm dependency and is not vendored, so the case is reported as unmodelled rather than printed without its patch |

## Corpus facts this ring established

* **There are no standalone `.d.ts` conformance baselines.** Directly under `tests/baselines/reference` the artifacts
  are `.types` 14,016, `.symbols` 14,016, `.js` 13,806 and `.errors.txt` 9,055 (plus 227 `.js.map`/`.sourcemap.txt` and
  some `.trace.json`). The 912 `.d.ts` files under `tests/baselines` all belong to the projects/transpile/api runners.
  Declaration output is carried **inside** the `.js` baseline: `doJsEmitBaseline` appends every emitted `.d.ts` after
  the JavaScript, then the `//// [DtsFileErrors]` section.
* A `.js`/`.types`/`.symbols` baseline opens with the source file's path relative to the **workspace root**
  (`//// [tests/cases/conformance/…] ////`), not the corpus-relative path and not the configured stem.
* A test with no diagnostics has **no** `.errors.txt` at all — the harness passes `null` and the baseline machinery
  compares `<no content>`, so an absent file is the correct answer, not a missing comparison.
* 207 `.types` baselines carry a `=== Performance Stats ===` block. Its counters are the **program's own checker**
  (read before and after the types walk), not process-global, so they are reproducible per case; the thresholds are
  1000/1000/1000/1000/1000/1500/45000 and the values are rounded to a human logarithm and printed with
  `toLocaleString("en-US")`.

## Measuring the harness model

The reference half is the model's own check, and it is the cheapest thing to run:

```
node tools/BaselineDiff/ts-baseline.cjs --manifest <m> --out <o> \
  --js <TypeScript>/lib/typescript.js --baselines <TypeScript>/tests/baselines/reference
```

Over a 148-file / 182-case sample of `tests/cases/conformance` (every 40th file, sorted) it reproduces the checked-in
baselines **byte for byte**: `files=148 cases=182 same=689 diverged=0 unmodelled=0 errors=0 extra=0` — `.js` 182/182,
`.errors.txt` 182/182, `.types` 182/182, `.symbols` 182/182. Eight harness-modelling bugs were found by that check and
fixed (a reference-vs-baseline mismatch is a model bug, not a port bug): the header path of the `.js`/`.types`
baselines, `doErrorBaseline`'s `null` for an empty diagnostics list, the `.json` output classification, the host's
`jsDocParsingMode`/`setParentNodes`, the virtual file system's directory layer (without it `import './a'` cannot resolve
and a `.types` walk prints `any`), `readDirectory`, the type/symbol walker's symbol rendering, and the performance-stats
block.

## Running it cheaply

* The port pass is single-threaded and in-process; `--limit`/`--filter` shard it. The reference half is batched (250
  files per node process) and batches are independent, so `--node-parallel 8` is legitimate: the per-batch outputs are
  concatenated **in batch order**, so the report does not depend on which batch finished first.
* Nothing carries a timestamp and both halves walk the corpus in sorted order, so two runs over the same corpus produce
  the same hashes, the same divergence lines and the same `SUMMARY`; only the self-reported peak numbers (the port's
  working set and each node batch's `maxRSS`) can differ, because they are measurements.
