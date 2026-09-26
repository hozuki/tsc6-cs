// The TypeScript half of tools/EmitDiff's emission differential: it loads the reference compiler once, builds a Program
// over every file of the manifest with it, runs `program.emit(undefined, writeFile)` with a callback that captures what
// the emitter wrote, hashes the canonical dump of that output (the same text tools/EmitDiff/Program.cs produces) with
// FNV-1a over UTF-16 code units, and writes a full dump only for the files whose hash differs from the port's
// (port-hashes.txt, written by the C# tool before this runs).
//
// usage: node ts-emit.cjs --manifest <json> --out <dir> --js <lib/typescript.js>
//
// Outputs, all in --out: ts-hashes.txt ("<rel>\t<hash>\t<length>" per file, manifest order), ts-errors.txt (only if a
// file could not be emitted), ts/<rel>.js (the diverging files' dumps) and one summary line on stdout.
//
// The canonical dump, frozen here and in tools/EmitDiff/Program.cs (LF only, no trailing spaces, one line per emitted
// file's begin framer, its text verbatim, and its end framer):
//
//   // EmitDiff skipped=<0|1> files=<n>
//   // EmitDiff begin <escaped output file name>
//   <the emitted text, byte for byte>
//   // EmitDiff end <escaped output file name> len=<utf16 length>
//   …repeated for every file the emitter wrote, in the order it wrote them…
//
// The `// EmitDiff` prefix is a JavaScript comment, so a dump is itself parseable JavaScript and the report's
// `line=<n>` numbers the file it wrote; the prefix can never collide with emitted text (a real line of output would
// have to start with exactly that comment). `len` is the text's UTF-16 length and it is on the *closing* line, so a
// difference that is only a trailing newline or a BOM still shows up (as that length) without masking the first
// differing line of the text itself.
//
// This is an *emit* differential: the dump is the emitted text, not a rendering of the tree. The port half is
// Program.cs's `EmitWithPort`, and both halves are handed the same option object and the same one-file host, so a
// differing dump is a difference in what the emitter wrote.
"use strict";

const fs = require("node:fs");
const path = require("node:path");

const FN_OFFSET = 14695981039346656037n;
const FN_PRIME = 1099511628211n;
const FN_MASK = 0xFFFFFFFFFFFFFFFFn;

let ts;

function fnv1a(text) {
    let hash = FN_OFFSET;
    for (let i = 0; i < text.length; i++) {
        hash = ((hash ^ BigInt(text.charCodeAt(i))) * FN_PRIME) & FN_MASK;
    }
    return hash.toString(16).padStart(16, "0");
}

// TS's sys.readFile strips a UTF-8 BOM before the text reaches the parser, so the reference side must too — otherwise
// every position in a BOM-carrying file shifts by one and the emit reports a divergence that is not in the port.
function stripBom(text) {
    return text.charCodeAt(0) === 0xFEFF ? text.slice(1) : text;
}

// An output file name is the only free text in the dump's headers; escape everything that would break the
// one-record-per-line shape, and spaces too so a header's fields stay whitespace-separated. UTF-16 code units, so the
// C# half (which iterates chars, not code points) produces the identical sequence.
function escapeText(text) {
    let out = "";
    for (let i = 0; i < text.length; i++) {
        const c = text.charCodeAt(i);
        if (c === 0x5C) out += "\\\\";
        else if (c === 0x0A) out += "\\n";
        else if (c === 0x0D) out += "\\r";
        else if (c === 0x09) out += "\\t";
        else if (c < 0x20 || c === 0x20 || c === 0x7F) out += "\\u" + c.toString(16).padStart(4, "0");
        else out += text[i];
    }
    return out;
}

// The dump's framing comment lines, one for the run and a begin/end pair around each emitted file's text. The
// length rides on the *end* line rather than the begin line: a divergence anywhere in a file's text almost always
// changes its length too, and a length in the begin line would then be the first difference of every such pair — the
// report would point at a number instead of at the emitted JavaScript it exists to point at. Both halves print the
// files in the order the emitter wrote them — the order is emitter behaviour, so it is compared rather than sorted away.
const PREFIX = "// EmitDiff ";

function frame(text) {
    return PREFIX + text + "\n";
}

function dump(result, outputs) {
    let out = frame(`skipped=${result.emitSkipped ? 1 : 0} files=${outputs.length}`);
    for (const output of outputs) {
        out += frame(`begin ${escapeText(output.fileName)}`);
        out += output.text;
        // A text that does not end in a newline would glue its end line onto its last line; `len=` is the text's own
        // length, so the framing newline never changes what the end line reports.
        if (output.text.length === 0 || output.text.charCodeAt(output.text.length - 1) !== 0x0A) out += "\n";
        out += frame(`end ${escapeText(output.fileName)} len=${output.text.length}`);
    }
    return out;
}

// The file name must be the corpus-relative path on both sides: the parser derives "is this a declaration file?" and
// the emit path derives the output's name from it, so a placeholder name would keep the port and the reference from
// producing the same output file name.
function emitFile(text, options, kind, fileName) {
    const scriptKind = ts.ScriptKind[kind] ?? ts.ScriptKind.TS;
    const host = {
        // setParentNodes: false is what the reference's own createCompilerHost parses with (program.ts:448-499's
        // createGetSourceFile passes the flag straight through, and the tsc command line leaves it unset), and the
        // printer walks children rather than parents — the opposite of what the checker half (ts-type-dump.cjs) needs.
        getSourceFile: (name, languageVersion) => (name === fileName ? ts.createSourceFile(name, text, languageVersion, false, scriptKind) : undefined),
        getDefaultLibFileName: () => "lib.d.ts",
        writeFile: () => {},
        getCurrentDirectory: () => "/",
        getDirectories: () => [],
        fileExists: (name) => name === fileName,
        readFile: (name) => (name === fileName ? text : undefined),
        getCanonicalFileName: (f) => f,
        useCaseSensitiveFileNames: () => true,
        getNewLine: () => "\n",
    };
    const program = ts.createProgram([fileName], options, host);
    const outputs = [];
    // The callback is the whole comparison: what the emitter hands writeFile *is* the emitted JavaScript, and the
    // program's own writeFile is only reached when no callback is supplied (program.ts:2598-2637).
    const result = program.emit(undefined, (name, data) => {
        outputs.push({ fileName: name, text: data });
    });
    return { text: dump(result, outputs), emitted: outputs.length, length: outputs.reduce((n, output) => n + output.text.length, 0) };
}

function writeDump(outDir, rel, text) {
    const target = path.join(outDir, rel + ".js");
    fs.mkdirSync(path.dirname(target), { recursive: true });
    fs.writeFileSync(target, text);
}

function main() {
    const args = process.argv.slice(2);
    let manifestPath, outDir, jsPath;
    // The driver runs this script once per *batch* so that node's heap does not grow with the corpus. The batch then
    // also needs to say where to read the port's hashes and where to put its own outputs, since several batches share
    // one output directory.
    let portHashesArg, hashesArg, errorsArg, dumpsDir;
    for (let i = 0; i < args.length; i++) {
        switch (args[i]) {
            case "--manifest": manifestPath = args[++i]; break;
            case "--out": outDir = args[++i]; break;
            case "--js": jsPath = args[++i]; break;
            case "--port-hashes": portHashesArg = args[++i]; break;
            case "--hashes": hashesArg = args[++i]; break;
            case "--errors": errorsArg = args[++i]; break;
            case "--dumps": dumpsDir = args[++i]; break;
            default: throw new Error("unknown argument '" + args[i] + "'");
        }
    }

    if (!manifestPath || !outDir || !jsPath) throw new Error("--manifest, --out and --js are required");
    ts = require(jsPath);

    const manifest = JSON.parse(fs.readFileSync(manifestPath, "utf8"));
    // The option set both halves are handed, frozen in tools/EmitDiff/Program.cs's PortOptions.
    //   allowJs: a `.js` root file is only a supported program root when it is on, and the corpus has a few.
    //   newLine: LineFeed — the emitted text is compared byte for byte, so neither side may take its line terminator
    //     from the OS (the reference's default is lineFeed too, but stating it keeps the two option sets identical).
    //   suppressOutputPathCheck: the port half cannot ask for an emit host on its own yet (its emitter is M7), and the
    //     reference's verifyCompilerOptions only asks for one when this flag is unset (program.ts:4392-4395); the flag
    //     is read nowhere else for these corpora, so setting it on both sides keeps the two option sets identical.
    const options = {
        target: ts.ScriptTarget.ESNext,
        module: ts.ModuleKind.ESNext,
        allowJs: true,
        newLine: ts.NewLineKind.LineFeed,
        suppressOutputPathCheck: true,
    };

    const portHashes = new Map();
    const portHashesPath = portHashesArg || path.join(outDir, "port-hashes.txt");
    if (fs.existsSync(portHashesPath)) {
        for (const line of fs.readFileSync(portHashesPath, "utf8").split("\n")) {
            const tab = line.indexOf("\t");
            if (tab > 0) portHashes.set(line.slice(0, tab), line.slice(tab + 1));
        }
    }

    const hashes = [];
    const errors = [];
    let same = 0, diverged = 0, emitted = 0, bytes = 0;
    for (const entry of manifest.files) {
        const text = stripBom(fs.readFileSync(path.join(manifest.root, entry.path), "utf8"));
        let result;
        try {
            result = emitFile(text, options, entry.kind, entry.path);
        } catch (e) {
            // The stack's first frames are the only thing that says *where* the reference gave up (a "cannot read
            // properties of undefined" from inside lib/typescript.js names neither the node nor the field), so they go
            // into the same one-line record the driver reads back.
            const frames = e && e.stack ? String(e.stack).split("\n").slice(1, 4).map((f) => f.trim()).join(" | ") : "";
            errors.push(`${entry.path}\tts-crash: ${e && e.message ? e.message : String(e)}${frames ? " @ " + frames : ""}`);
            continue;
        }

        const hash = fnv1a(result.text);
        hashes.push(`${entry.path}\t${hash}\t${result.text.length}`);
        emitted += result.emitted;
        bytes += result.length;
        const portHash = portHashes.get(entry.path);
        if (portHash === `${hash}\t${result.text.length}`) {
            same++;
        } else {
            diverged++;
            if (portHash !== undefined) writeDump(dumpsDir || path.join(outDir, "ts"), entry.path, result.text);
        }
    }

    // The driver creates --out before it starts a batch, but a standalone run should not have to.
    fs.mkdirSync(outDir, { recursive: true });
    fs.writeFileSync(hashesArg || path.join(outDir, "ts-hashes.txt"), hashes.map((h) => h + "\n").join(""));
    fs.writeFileSync(errorsArg || path.join(outDir, "ts-errors.txt"), errors.map((e) => e + "\n").join(""));
    // `files=` counts the emitted *outputs*, not the inputs: a run whose reference side emitted nothing compared no
    // JavaScript at all, and this is the number that says so. The peak RSS is reported from inside node too — the
    // driver cannot ask for it after the process exits (see --node-batch in Program.cs).
    const maxRssMiB = Math.round(process.resourceUsage().maxRSS / 1024);
    console.log(`ts-side files=${manifest.files.length} same=${same} diverged=${diverged} ts-error=${errors.length} emitted=${emitted} bytes=${bytes} typescript=${ts.version} maxRSS=${maxRssMiB}MiB`);
}

main();
