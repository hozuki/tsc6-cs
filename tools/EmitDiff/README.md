# EmitDiff

The emission-level differential (docs/architecture.md §7's outer loop, §17.5's acceptance ring, M7): emit the files of a
corpus twice — once with the port's `Program.Emit` and once with the reference `lib/typescript.js` — capture the
JavaScript each emitter wrote, hash it byte for byte, and report the files whose hashes differ together with their first
differing line pair. It is the shape `tools/BindDiff` and `tools/TypeDiff` record, at the last layer of the pipeline:
same manifest, same FNV-1a hash over UTF-16 code units, same per-batch `node` process, same "write a full dump only for
diverging files", same self-reported peak.

## Command lines

The whole differential (the port half needs M7's transformer chain and emitter; see *the M7 boundary* below):

```
dotnet run --project tools/EmitDiff -- --cases <TypeScript>/tests/cases/conformance --filter "es6/binaryAndOctalIntegerLiteral/**"
```

The reference half on its own — it needs nothing but `node`, and this is how the format is measured and re-measured:

```
node tools/EmitDiff/ts-emit.cjs \
  --manifest <manifest.json> --out <dir> --js <TypeScript>/lib/typescript.js
```

A manifest is `{"root": "<corpus root>", "files": [{"path": "<root-relative>", "kind": "TS|TSX|JS|JSX"}]}`; the driver
writes one per batch under `<out>/manifests/`, and leaves the full one at `<out>/manifest.json`. With no `--port-hashes`
file present every file counts as diverged and no dump is written, which is what a reference-only run reports.

Options of the driver: `--cases` (required), `--filter` (glob over the root-relative path, `*` does not cross `/`, `**`
does), `--limit`, `--out` (default `<repo>/artifacts/emitdiff`), `--js`, `--node`, `--node-script`, `--node-batch`
(default 250), `-h`/`--help`.

Both halves are handed the **same option object and the same one-file host** — `target: ESNext`, `module: ESNext`,
`allowJs: true`, `newLine: LineFeed`, `suppressOutputPathCheck: true` (`Program.cs`'s `PortOptions` and `ts-emit.cjs`'s
`options`, which must be changed together), with a program of exactly one source file and no default library and no
module resolution, exactly like `tools/TypeDiff`'s host. Each file is emitted on its own, by both sides, through the
`writeFile` callback and nothing else (program.ts:2598-2637's `writeFile: writeFileCallback || writeFile`), so neither
half implements an emit host.

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

* **The dump is the emitted JavaScript.** Everything around it is emit behaviour too — the skipped flag, the number of
  outputs, and each output's name, order and length — which is why those are compared rather than normalized away.
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
* **Declaration emit, source maps and `.tsbuildinfo`.** The frozen option set asks for none of them, so a run produces one
  `.js` per input (or none, for a `.d.ts` input). Adding `--declaration` or `--source-map` means adding it to **both**
  option objects and extending the dump's per-output framing to say which output a text belongs to; the format above
  already allows it (`begin`/`end` pairs repeat), but nothing is guessed about it today.
* **The option matrix** `target`/`module`/`jsx`/`useDefineForClassFields` (§17.5's next step) is *not* wired to `--…`
  flags yet: the option object is frozen at the five slots above and both halves must be changed together to move it.

## The M7 boundary

The port half cannot emit anything yet: `Program.Emit` is one of M7's loud, named blockers
(`src/TypeScript.Compiler/Program/Program.EmitBoundary.cs`, docs/architecture.md §15.15 ruling 1), and the next thing its
body will need is the transformer list. A port-side `NotSupportedException` is therefore *data*, not a harness failure —
each file it happens on gets one line of `port-crashes.txt`, counts against the verdict, and never reaches the
comparison:

```
CRASH es6/binaryAndOctalIntegerLiteral/binaryIntegerLiteral.ts  port-crash: NotSupportedException: emit (program.ts:2688) is not ported yet: the transformer chain and the emitter are M7's (docs/architecture.md §15.15). @ Program.Emit (Program.EmitBoundary.cs:30)
```

(Measured on a clean `git archive HEAD` snapshot with `--filter "**/binaryIntegerLiteral.ts"`: `SUMMARY files=1 same=0
diverged=0 port-crash=1 ts-error=0 unpaired=0`, `VERDICT fail`, exit `1`, while the reference half of the same run
emitted 1 file / 8964 bytes from `lib/typescript.js`.) Once `Program.Emit`'s body lands, the same record names the next
unported member — `getTransformers` and its transformer list — because the harness prints whatever the port threw, by
type and message, and a file whose emit throws is never counted as `same`.

There is deliberately **no** pre-flight probe and **no** special exit code for this: unlike `tools/TypeDiff`'s
`Checker.CreateTypeChecker` boundary (one capability, all-or-nothing, hence exit `4`), emit failure is per file even after
M7 lands, and the per-file crash lines are the report of where the port stops. Nothing here is stubbed on either side —
there is no fake emitter, no placeholder dump, and no path on which a port that failed to emit can read as agreement.
