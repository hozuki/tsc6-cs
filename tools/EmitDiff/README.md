# EmitDiff

The emission-level differential (docs/architecture.md §7's outer loop, §17.5's acceptance ring, M7): emit the files of a
corpus twice — once with the port's `Program.Emit` and once with the reference `lib/typescript.js` — capture every output
each emitter wrote (the JavaScript, and the `.js.map` or inlined map when a `--source-map`-family flag asks for one), hash
it byte for byte, and report the files whose hashes differ together with their first differing line pair. It is the shape
`tools/BindDiff` and `tools/TypeDiff` record, at the last layer of the pipeline: same manifest, same FNV-1a hash over
UTF-16 code units, same per-batch `node` process, same "write a full dump only for diverging files", same self-reported
peak.

## Command lines

The whole differential (both halves are live: `Program.Emit`, the transformer chain and the emitter all landed with M7):

```
dotnet run --project tools/EmitDiff -- --cases <TypeScript>/tests/cases/conformance --filter "es6/binaryAndOctalIntegerLiteral/**"
```

The same run with source maps on — every file's `.js`, its `.js.map` and the `//# sourceMappingURL=` comment inside the
`.js` are compared byte for byte, per file:

```
dotnet run --project tools/EmitDiff -- \
  --cases <TypeScript>/tests/cases/conformance --source-map --inline-sources --source-root ../src --map-root ../maps
```

The reference half on its own — it needs nothing but `node`, and this is how the format is measured and re-measured (the
source-map flags are its own, and it accepts them without a port to compare against):

```
node tools/EmitDiff/ts-emit.cjs \
  --manifest <manifest.json> --out <dir> --js <TypeScript>/lib/typescript.js
```

A manifest is `{"root": "<corpus root>", "files": [{"path": "<root-relative>", "kind": "TS|TSX|JS|JSX"}]}`; the driver
writes one per batch under `<out>/manifests/`, and leaves the full one at `<out>/manifest.json`. With no `--port-hashes`
file present every file counts as diverged and no dump is written, which is what a reference-only run reports.

Options of the driver: `--cases` (required), `--filter` (glob over the root-relative path, `*` does not cross `/`, `**`
does), `--limit`, `--parallel` (port-half files compiled at once, default `ProcessorCount`, `1` = serial), `--out`
(default `<repo>/artifacts/emitdiff`), `--js`, `--node`, `--node-script`, `--node-batch`
(default 250), the source-map family `--source-map`, `--inline-source-map`, `--inline-sources`, `--source-root <dir>`,
`--map-root <dir>`, and `-h`/`--help`.

The port half fans out over the corpus: every file is an independent program, so `--parallel` workers each compile one,
and the index files are still written in file order — a parallel run's artifacts are byte-identical to a serial one's.
Peak memory grows with the worker count (each worker holds one program), which the report's own peak line shows.

Both halves are handed the **same option object and the same one-file host** — `target: ESNext`, `module: ESNext`,
`allowJs: true`, `newLine: LineFeed`, `suppressOutputPathCheck: true` (`Program.cs`'s `PortOptions` and `ts-emit.cjs`'s
`options`, which must be changed together) plus whatever the source-map flags add, with a program of exactly one source
file and no default library and no module resolution, exactly like `tools/TypeDiff`'s host. Each file is emitted on its
own, by both sides, through the `writeFile` callback and nothing else (program.ts:2598-2637's
`writeFile: writeFileCallback || writeFile`), so neither half implements an emit host.

The source-map flags are the five `CompilerOptions` members the emitter's source-map path reads, and they are the same
five on both sides:

| flag | option | what it adds to the compared outputs |
|---|---|---|
| `--source-map` | `sourceMap` | a second output, `<name>.js.map` (emitter.ts:535-537), and the `//# sourceMappingURL=` comment inside the `.js` (emitter.ts:1053-1055) |
| `--inline-source-map` | `inlineSourceMap` | no second output; the comment carries the whole map as a `data:application/json;base64,` URL instead (emitter.ts:1055-1059) |
| `--inline-sources` | `inlineSources` | each source's text as `sourcesContent` (emitter.ts:6306-6311) |
| `--source-root <dir>` | `sourceRoot` | the map's `sourceRoot` (normalized with a trailing separator) and the directory its `sources` are relative to (emitter.ts:1026-1035) |
| `--map-root <dir>` | `mapRoot` | the path the comment names, i.e. where the map is expected to be published (emitter.ts:1057-1065) |

Without them the option object is byte-for-byte what it always was, so a run with no flag is comparable with every
earlier run's verdict. With `--source-map` the emitter writes the map **before** the JavaScript on both sides
(`printSourceFileOrBundle` writes the `.js.map` and then the `.js`, emitter.ts:1099-1121), so the dump's first `begin`
record of a file is its map and the second is the `.js` — the order is emit behaviour and is therefore compared, not
sorted. The dump's format needed no extension for this: its `begin`/`end` pair already repeats once per output file, and
each record carries the output's own name and length.


Outputs, all in `--out`:

| file | contents |
|---|---|
| `port-hashes.txt` / `ts-hashes.txt` | `<rel>\t<16-hex FNV-1a hash of the dump>\t<dump length>` per file |
| `port-crashes.txt` / `ts-errors.txt` | one line per file that threw: `<rel>\tport-crash: <Type>: <message> @ <frames>` |
| `port/<rel>.js` / `ts/<rel>.js` | the diverging files' full dumps (the same bytes that were hashed) |
| `report.txt` | the `DIVERGE`/`CRASH`/`TS-ERROR` lines, `TS-SIDE`, the peak, `SUMMARY` |
| `divergences.jsonl` | the same divergences as JSON objects, for a downstream tool |
| `ts-summary.txt` | one line per node batch: `files=/same=/diverged=/ts-error=/emitted=/bytes=/typescript=/maxRSS=` |

Exit codes: `0` every hash matched **and** nothing crashed, `1` divergences or crashes, `2` bad arguments, `3` node could
not run — the same contract the three sibling tools carry, and the same one-line audit trail on stderr:

```
emitdiff: VERDICT fail same=0/1 diverged=0 port-crash=1 ts-error=0 unpaired=0 exit=1
```

`same` alone never makes a run clean: an unpaired file (a hash on one side only), a port crash, a reference error and a
divergence each mean something was *not* compared, and a run that compared nothing at all must never read as agreement.

## The canonical dump (frozen v1)

Identical text on both sides for equal emitted output; LF only, one record per line, numbers in invariant culture (the
emitted text itself is carried verbatim, so its own whitespace, including trailing whitespace, is compared rather than
normalized):

```
// EmitDiff skipped=<0|1> files=<n>
// EmitDiff begin <escaped output file name>
<the emitted text, byte for byte>
// EmitDiff end <escaped output file name> len=<utf16 length>
…repeated for every file the emitter wrote, in the order it wrote them…
```

Rules:

* **The dump is the emitted text.** Everything around it is emit behaviour too — the skipped flag, the number of
  outputs, and each output's name, order and length — which is why those are compared rather than normalized away. With a
  source-map flag on, the output records are the emitter's own: the `.js.map` written before the `.js` for `--source-map`,
  or that `.js` with the map inlined in it for `--inline-source-map`.
* **The `// EmitDiff` prefix is a JavaScript comment**, so a dump is itself parseable JavaScript and the report's
  `line=<n>` numbers the file written under `<out>/port` or `<out>/ts`. The prefix cannot collide with emitted text: a
  line of output would have to start with exactly that comment.
* **`len=` rides on the closing line, not the opening one.** A divergence inside a file's text almost always changes its
  length too, and a length in the `begin` line would be the *first* difference of every such pair — the report would
  point at a number instead of at the emitted JavaScript it exists to point at. On the `end` line it is still compared, so
  a difference that is only a trailing newline shows up as a length (`output length mismatch (same output name,
  different lengths)`) instead of hiding behind the framing.
* **A text that does not end in a newline gets one in the dump** so its `end` framer cannot glue onto its last line;
  `len=` is the text's own length, so the framing byte is never counted.
* **Nothing is sorted.** Emitted files print in the order the emitter wrote them (the reference's `emitFiles` walks the
  program's source files), and the file list is the sorted corpus-relative path, so two runs over the same corpus produce
  byte-identical hashes and divergence lines; only the self-reported peak numbers can differ (they are measurements, not
  results), and nothing carries a timestamp.
* **The output file name is the emitter's own**, escaped the way `tools/TypeDiff` escapes names: a space (the dump's
  field separator), any control character and `DEL` become `\uXXXX`, and `\`, `\n`, `\r`, `\t` are backslash-escaped.
  Both halves name the output from the same `getOutputPathsFor` logic (the input's name and `/` as the current
  directory), so a name difference is a real port divergence and not a spelling choice of the harness.
* **The comparison reports the class of the first difference and the pair itself.** `class=` is a property of the two
  differing lines, not a guess: line 1 is the run's header (`emit summary mismatch`), a `begin` line is an
  `output file name mismatch`, a length on an `end` line is an `output length mismatch`, a line count that differs is a
  `line-count mismatch`, and two text lines are `emitted text differs` — or `emitted text differs in whitespace only`
  when they agree once whitespace is removed, which for emitted JavaScript is nearly always indentation or a line
  terminator rather than a different program. `col=` is the 1-based UTF-16 column of the first differing character, and
  a line longer than 200 characters is printed as a window around that column (`...` marks what was cut) so the
  character the two sides part company on is never the part that got cut. The whole lines are in the two dump files.

## What is compared, and what is not

Compared: the emitted text of every file each emitter wrote, its name, its order, its length, and the emit-skipped flag.

Not compared, deliberately:

* **Diagnostics.** Neither half looks at the program's diagnostics, so the two hosts' differently-named default library
  (`lib.d.ts` on the node side, `…/lib.esnext.full.d.ts` on the port's) cannot show up as a divergence — the same
  asymmetry `tools/TypeDiff`'s README records for the sides it *does* compare diagnostics on. Emit runs whether or not
  there are type errors (`noEmitOnError` is unset), and the transforms that do read the checker — `const enum` inlining,
  `import()` rewriting — read the same one-file program on both sides.
* **Declaration emit and `.tsbuildinfo`.** The frozen option set asks for neither, so a run produces one `.js` per input
  (or none, for a `.d.ts` input) — plus the `.js.map` when `--source-map` asked for one. Adding `--declaration` means
  adding it to **both** option objects; the dump needs no change for a further output, because its `begin`/`end` pair
  already repeats per output and `--source-map` is what proved it.
* **The option matrix** `target`/`module`/`jsx`/`useDefineForClassFields` (§17.5's next step) is *not* wired to `--…`
  flags yet: the option object is the five frozen slots (plus the source-map family above) and both halves must be
  changed together to move it.

Compared, when asked for, is the whole source-map half: the `.js.map` (or the inlined `data:` URL), the
`//# sourceMappingURL=` comment inside the `.js`, the map's `sources`/`sourcesContent`/`sourceRoot`, its VLQ `mappings`
and both outputs' order — all of it goes through the same `writeFile` capture and the same byte-for-byte hash as the
JavaScript, so a map that differs by one VLQ digit is a divergence like any other.

## The M7 boundary (retired)

`Program.Emit` and M7's transformer chain landed (commit `1d31730`), and the emitter's last named blocker —
`createSourceMapGenerator`, the entry the source-map half hangs off — landed with this tool's `--source-map` work. The
harness's per-file crash record did not change, and neither did what a crash means:

* A port-side exception is still **data, not a harness failure** — one line in `port-crashes.txt`, counted against the
  verdict, and the file never reaches the comparison. That is what an unported member looks like from here, and it is how
  the source-map half reported its own blocker before it landed (`port-crash: NotSupportedException:
  createSourceMapGenerator …`, `same=36/5908 port-crash=5872` on the conformance corpus, where the 36 files that still
  compared are the `.d.ts` inputs that write no output at all).
* Nothing is stubbed on either side: no fake emitter, no placeholder dump, and no path on which a port that failed to
  emit can read as agreement — a run whose port half throws on every file exits `1` with `same=0`.
* The option object both halves get is still the one thing the harness must keep identical by hand, and a drift shows up
  as a `.js` divergence on the first file that touches the drifted option.

