// The TypeScript half of tools/TypeDiff's checker differential: it loads the reference compiler once, builds a Program
// over every file of the manifest with it, creates the type checker (which is what runs the checker), dumps what the
// checker wrote for that file in the canonical line format (the same one Program.cs emits), hashes that text with
// FNV-1a over UTF-16 code units, and writes a full dump only for the files whose hash differs from the port's
// (port-hashes.txt, written by the C# tool before this runs).
//
// usage: node ts-type-dump.cjs --manifest <json> --out <dir> --js <lib/typescript.js> [--strict]
//
// Outputs, all in --out: ts-hashes.txt ("<rel>\t<hash>\t<length>" per file, manifest order), ts-errors.txt (only if a
// file could not be checked), ts/<rel>.txt (the diverging files), and one summary line on stdout.
//
// The canonical dump, frozen here and in tools/TypeDiff/Program.cs (LF only, no trailing spaces, one record per line,
// every id per-file and assigned in order of first reference, '#'-prefixed when referred to, '-' when absent):
//
//   N<depth> <kind>(<pos>,<end>) sym=<#s|-> type=<#t|-> sig=<#g|->
//   X0 config=<[#d,…]|-> syntactic=<[#d,…]|-> options=<[#d,…]|-> global=<[#d,…]|-> semantic=<[#d,…]|->
//   S#<id> flags=<n> flagsName=<names|-> name=<escaped> decls=<n> members=<[name,…]|-> exports=<[name,…]|->
//   T#<id> flags=<n> flagsName=<names|-> id=<n> obj=<n|-> sym=<#s|-> alias=<#s|-> types=<[#t,…]|-> props=<[name,…]|-> sigs=<[#g,…]|->
//   G#<id> decl=<kind(pos,end)|-> minArgs=<n> params=<[#s,…]|-> ret=<#t|-> tp=<[#t,…]|->
//   D#<id> code=<n> start=<n|-> len=<n|-> cat=<n>
//
// The node line's three fields are '!' (all three together) when the reference's own API gave up on that node.
//
// A node line is one node of the tree in forEachChild pre-order (depth 0 is the source file); sym/type are what
// getSymbolAtLocation/getTypeAtLocation answer for it, and sig is the resolved signature for the call-like kinds
// (getResolvedSignature). An S line is a symbol printed once per file (SymbolFlags, its escaped name, its declaration
// count, and the keys of its members/exports in the compiler's own order — never sorted, because insertion order is
// itself checker state); a T line is a type (TypeFlags, the checker's own type id, ObjectFlags, its symbol and alias
// symbol, a union/intersection's members, the properties the checker resolves for it, and its call/construct
// signatures); a G line is one signature (declaration, minArgumentCount, parameter symbols, return type, type
// parameters); a D line is one diagnostic (code, start, length, category) — the five X0 lists are the source file's
// config/syntactic/options/global/semantic diagnostics, each in the order the program returns them. Every line is
// printed once per file and every field that names another record is a '#id' reference, so the dump grows with the
// graph it describes rather than with the number of references to it (docs/architecture.md §15.13's memory lesson).
//
// A node the reference's own API cannot answer for — getTypeAtLocation dereferences a module source file's missing
// parent, and a parse-recovery declaration can have no symbol — prints all three of that node's fields as '!' instead
// of dropping the record or throwing: '!' is data, so both halves must still agree on where the reference gave up, and
// the batch summary reports how many records carried it.
"use strict";

const fs = require("node:fs");
const path = require("node:path");

const FN_OFFSET = 14695981039346656037n;
const FN_PRIME = 1099511628211n;
const FN_MASK = 0xFFFFFFFFFFFFFFFFn;

// getResolvedSignature takes a CallLikeExpression; Decorator is one of them in the reference, but neither side's
// decorator checking is comparable yet, so the call-like set here is the five kinds both halves agree on.
const CALL_LIKE = new Set();

let ts;

function fnv1a(text) {
    let hash = FN_OFFSET;
    for (let i = 0; i < text.length; i++) {
        hash = ((hash ^ BigInt(text.charCodeAt(i))) * FN_PRIME) & FN_MASK;
    }
    return hash.toString(16).padStart(16, "0");
}

// TS's sys.readFile strips a UTF-8 BOM before the text reaches the parser, so the reference side must too — otherwise
// every position in a BOM-carrying file shifts by one and the diff reports a divergence that is not in the port.
function stripBom(text) {
    return text.charCodeAt(0) === 0xFEFF ? text.slice(1) : text;
}

// A name the checker builds can embed a *process-global* symbol id, which is not comparable across two processes (or
// even two batch layouts): utilities.ts:5450's `__#<getSymbolId(class)>@<description>` for a private-name property, and
// checker.ts:20345's `__@<symbolName>@<getSymbolId(symbol)>` for a synthetic type's escaped name. The id is the only
// part that is not behaviour, so it is normalized to '?' and the rest of the name is still compared byte for byte —
// the same discipline that keeps the harness's own '#id's per-file (docs/architecture.md §15.13).
function normalizeName(text) {
    return text
        .replace(/__#\d+@/g, "__#?@")
        .replace(/^(__@[^@]*@)\d+$/, "$1?");
}

// Every free-text field (a symbol's escaped name, a member/export key, a property name) goes through this, so the
// normalization above and the escaping below are applied in exactly one place on each side.
function nameText(text) {
    return escapeText(normalizeName(text));
}

// Names (an escaped name, a member key) are the only free text in the dump; escape everything that would break the
// one-record-per-line shape, and spaces too so a record's fields stay whitespace-separated. UTF-16 code units, so the
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

// A composite flag word rendered as the members whose bits it contains: single-bit members only, ascending bit order,
// and for a bit several members share (TS's `AliasExcludes = Alias`, `IncludesMissingType = TypeParameter`) the
// lexicographically smallest name, so the text does not depend on either side's declaration order. '|?' marks bits no
// single-bit member covers. The port's half does exactly this over its generated enum.
function flagsName(enumObject, value) {
    if (!value) return "-";
    const byValue = new Map();
    let knownMask = 0;
    for (const name of Object.keys(enumObject)) {
        if (!Number.isNaN(Number(name))) continue; // the reverse map's own numeric keys
        const memberValue = enumObject[name];
        if (typeof memberValue !== "number" || memberValue <= 0) continue;
        if ((memberValue & (memberValue - 1)) !== 0) continue; // composite members are not names for a bit
        knownMask |= memberValue;
        const current = byValue.get(memberValue);
        if (current === undefined || name < current) byValue.set(memberValue, name);
    }

    const parts = [];
    for (const bit of [...byValue.keys()].sort((a, b) => a - b)) {
        if ((value & bit) !== 0) parts.push(byValue.get(bit));
    }
    if (parts.length === 0) return "-";
    return parts.join("|") + (((value & ~knownMask) >>> 0) !== 0 ? "|?" : "");
}

// Every record is printed once per file and referred to as '#id' afterwards, from a single id map per kind per file:
// one S line per symbol, one T line per type, one G line per signature, one D line per diagnostic. Nested text would
// make the dump grow with the reference count instead of the graph (docs/architecture.md §15.13).
function createDumper(out, checker) {
    const symbolIds = new Map();
    const typeIds = new Map();
    const signatureIds = new Map();
    const diagnosticIds = new Map();
    let nextSymbol = 1;
    let nextType = 1;
    let nextSignature = 1;
    let nextDiagnostic = 1;

    function list(items, key) {
        return items && items.length ? "[" + items.map(key).join(",") + "]" : "-";
    }

    function symbolKey(symbol) {
        if (!symbol) return "-";
        const existing = symbolIds.get(symbol);
        if (existing !== undefined) return "#" + existing;
        const id = nextSymbol++;
        symbolIds.set(symbol, id);

        const name = symbol.escapedName ?? "";
        const members = symbol.members ? [...symbol.members.keys()].map(nameText) : [];
        const exports = symbol.exports ? [...symbol.exports.keys()].map(nameText) : [];
        out.push(`S#${id} flags=${symbol.flags} flagsName=${flagsName(ts.SymbolFlags, symbol.flags)}`
            + ` name=${nameText(String(name))} decls=${symbol.declarations ? symbol.declarations.length : 0}`
            + ` members=${members.length ? "[" + members.join(",") + "]" : "-"}`
            + ` exports=${exports.length ? "[" + exports.join(",") + "]" : "-"}`);
        return "#" + id;
    }

    function signatureKey(signature) {
        if (!signature) return "-";
        const existing = signatureIds.get(signature);
        if (existing !== undefined) return "#" + existing;
        const id = nextSignature++;
        signatureIds.set(signature, id);

        const declaration = signature.declaration
            ? `${signature.declaration.kind}(${signature.declaration.pos},${signature.declaration.end})`
            : "-";
        const parameters = list(signature.parameters, symbolKey);
        const typeParameters = list(signature.typeParameters, typeKey);
        const returns = typeKey(checker.getReturnTypeOfSignature(signature));
        out.push(`G#${id} decl=${declaration} minArgs=${signature.minArgumentCount}`
            + ` params=${parameters} ret=${returns} tp=${typeParameters}`);
        return "#" + id;
    }

    function typeKey(type) {
        if (!type) return "-";
        const existing = typeIds.get(type);
        if (existing !== undefined) return "#" + existing;
        const id = nextType++;
        typeIds.set(type, id);

        // TS keeps an `objectFlags` slot on every type object and leaves it undefined where nothing set it; the port's
        // models declare it as a non-nullable enum, so "unset" reads as 0 there. Both sides normalize unset/zero to
        // '-', which is what keeps a type whose slot the reference never wrote from reporting a false divergence.
        const objectFlags = typeof type.objectFlags === "number" && type.objectFlags !== 0 ? String(type.objectFlags) : "-";
        const symbol = symbolKey(type.symbol);
        const alias = symbolKey(type.aliasSymbol);
        const members = list(Array.isArray(type.types) ? type.types : undefined, typeKey);
        const properties = checker.getPropertiesOfType(type).map((p) => nameText(String(p.escapedName)));
        const signatures = list(
            [...checker.getSignaturesOfType(type, ts.SignatureKind.Call), ...checker.getSignaturesOfType(type, ts.SignatureKind.Construct)],
            signatureKey);
        out.push(`T#${id} flags=${type.flags} flagsName=${flagsName(ts.TypeFlags, type.flags)}`
            + ` id=${typeof type.id === "number" ? type.id : 0} obj=${objectFlags}`
            + ` sym=${symbol} alias=${alias} types=${members}`
            + ` props=${properties.length ? "[" + properties.join(",") + "]" : "-"} sigs=${signatures}`);
        return "#" + id;
    }

    function diagnosticKey(diagnostic) {
        const existing = diagnosticIds.get(diagnostic);
        if (existing !== undefined) return "#" + existing;
        const id = nextDiagnostic++;
        diagnosticIds.set(diagnostic, id);
        out.push(`D#${id} code=${diagnostic.code}`
            + ` start=${typeof diagnostic.start === "number" ? diagnostic.start : "-"}`
            + ` len=${typeof diagnostic.length === "number" ? diagnostic.length : "-"}`
            + ` cat=${diagnostic.category}`);
        return "#" + id;
    }

    function diagnosticList(diagnostics) {
        return list(diagnostics, diagnosticKey);
    }

    // The reference's own queries can give up on a node: getTypeAtLocation dereferences slots that a parse-recovery
    // node or a lazily-built node does not have (an external module's source file has no parent, and an odd
    // declaration has no symbol), and a query's expansion can hit the same thing. A node the reference cannot answer
    // for is printed with '!' in all three of its fields rather than dropped or thrown: '!' is data, so the two halves
    // still have to agree on *where* the reference gave up, and a divergence is still caught at that record. The count
    // goes into the run summary, so a rise in it is visible.
    let gaveUp = 0;

    function node(n, depth, sourceFileDiagnostics) {
        let record;
        try {
            const symbol = symbolKey(checker.getSymbolAtLocation(n));
            const type = typeKey(checker.getTypeAtLocation(n));
            const signature = CALL_LIKE.has(n.kind) ? signatureKey(checker.getResolvedSignature(n)) : "-";
            record = `N${depth} ${n.kind}(${n.pos},${n.end}) sym=${symbol} type=${type} sig=${signature}`;
        } catch {
            gaveUp++;
            record = `N${depth} ${n.kind}(${n.pos},${n.end}) sym=! type=! sig=!`;
        }
        out.push(record);

        if (sourceFileDiagnostics !== undefined) {
            out.push(`X0 config=${diagnosticList(sourceFileDiagnostics.config)}`
                + ` syntactic=${diagnosticList(sourceFileDiagnostics.syntactic)}`
                + ` options=${diagnosticList(sourceFileDiagnostics.options)}`
                + ` global=${diagnosticList(sourceFileDiagnostics.global)}`
                + ` semantic=${diagnosticList(sourceFileDiagnostics.semantic)}`);
        }

        ts.forEachChild(n, (child) => node(child, depth + 1, undefined));
    }

    return { node, diagnosticList, gaveUp: () => gaveUp };
}

// The file name must be the corpus-relative path on both sides: the parser derives "is this a declaration file?" from
// the name, so a placeholder name would keep the port and the reference from writing the same source-file flags.
function dump(text, options, kind, fileName) {
    const scriptKind = ts.ScriptKind[kind] ?? ts.ScriptKind.TS;
    const host = {
        getSourceFile: (name, languageVersion) => (name === fileName ? ts.createSourceFile(name, text, languageVersion, true, scriptKind) : undefined),
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
    const checker = program.getTypeChecker(); // creates the checker: this is what checks the file
    const sourceFile = program.getSourceFile(fileName);
    if (sourceFile === undefined) throw new Error(`the program could not find its own root file '${fileName}'`);
    const out = [];
    const dumper = createDumper(out, checker);

    // The five diagnostic lists the program exposes for this file, in the order getPreEmitDiagnostics concatenates
    // them. Computing them first is what makes the checker check the whole file before the walk below queries nodes.
    const sourceFileDiagnostics = {
        config: program.getConfigFileParsingDiagnostics(),
        syntactic: program.getSyntacticDiagnostics(sourceFile),
        options: program.getOptionsDiagnostics(),
        global: program.getGlobalDiagnostics(),
        semantic: program.getSemanticDiagnostics(sourceFile),
    };
    dumper.node(sourceFile, 0, sourceFileDiagnostics);
    return { text: out.join("\n") + "\n", gaveUp: dumper.gaveUp() };
}

function writeDump(outDir, rel, text) {
    const target = path.join(outDir, rel + ".txt");
    fs.mkdirSync(path.dirname(target), { recursive: true });
    fs.writeFileSync(target, text);
}

function main() {
    const args = process.argv.slice(2);
    let manifestPath, outDir, jsPath, strict = false;
    // The driver runs this script once per *batch* so that node's heap does not grow with the corpus: each Program plus
    // its type checker is enormous and node does not reclaim them quickly. The batch then also needs to say where to
    // read the port's hashes and where to put its own outputs, since several batches share one output directory.
    let portHashesArg, hashesArg, errorsArg, dumpsDir;
    for (let i = 0; i < args.length; i++) {
        switch (args[i]) {
            case "--manifest": manifestPath = args[++i]; break;
            case "--out": outDir = args[++i]; break;
            case "--js": jsPath = args[++i]; break;
            case "--strict": strict = true; break;
            case "--port-hashes": portHashesArg = args[++i]; break;
            case "--hashes": hashesArg = args[++i]; break;
            case "--errors": errorsArg = args[++i]; break;
            case "--dumps": dumpsDir = args[++i]; break;
            default: throw new Error("unknown argument '" + args[i] + "'");
        }
    }

    if (!manifestPath || !outDir || !jsPath) throw new Error("--manifest, --out and --js are required");
    ts = require(jsPath);
    CALL_LIKE.add(ts.SyntaxKind.CallExpression);
    CALL_LIKE.add(ts.SyntaxKind.NewExpression);
    CALL_LIKE.add(ts.SyntaxKind.TaggedTemplateExpression);
    CALL_LIKE.add(ts.SyntaxKind.JsxOpeningElement);
    CALL_LIKE.add(ts.SyntaxKind.JsxSelfClosingElement);

    const manifest = JSON.parse(fs.readFileSync(manifestPath, "utf8"));
    // allowJs: a `.js` root file is only a supported program root when it is on, and the corpus has a few. `strict` is
    // off by default like the reference's own default options object, and is what --strict turns on for both sides.
    const options = { target: ts.ScriptTarget.ESNext, allowJs: true };
    if (strict) options.strict = true;

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
    let same = 0, diverged = 0, gaveUp = 0;
    for (const entry of manifest.files) {
        const text = stripBom(fs.readFileSync(path.join(manifest.root, entry.path), "utf8"));
        let dumpText;
        try {
            const result = dump(text, options, entry.kind, entry.path);
            dumpText = result.text;
            gaveUp += result.gaveUp;
        } catch (e) {
            // The stack's first frames are the only thing that says *where* the reference gave up (a "cannot read
            // properties of undefined" from inside lib/typescript.js names neither the node nor the field), so they go
            // into the same one-line record the driver reads back.
            const frames = e && e.stack ? String(e.stack).split("\n").slice(1, 4).map((f) => f.trim()).join(" | ") : "";
            errors.push(`${entry.path}\tts-crash: ${e && e.message ? e.message : String(e)}${frames ? " @ " + frames : ""}`);
            continue;
        }

        const hash = fnv1a(dumpText);
        hashes.push(`${entry.path}\t${hash}\t${dumpText.length}`);
        const portHash = portHashes.get(entry.path);
        if (portHash === `${hash}\t${dumpText.length}`) {
            same++;
        } else {
            diverged++;
            if (portHash !== undefined) writeDump(dumpsDir || path.join(outDir, "ts"), entry.path, dumpText);
        }
    }

    // The driver creates --out before it starts a batch, but a standalone run should not have to.
    fs.mkdirSync(outDir, { recursive: true });
    fs.writeFileSync(hashesArg || path.join(outDir, "ts-hashes.txt"), hashes.map((h) => h + "\n").join(""));
    fs.writeFileSync(errorsArg || path.join(outDir, "ts-errors.txt"), errors.map((e) => e + "\n").join(""));
    // The peak RSS is reported from inside node: the driver cannot ask for it after the process exits, and this is the
    // number that says whether a batch size is small enough for the machine (see --node-batch in Program.cs).
    const maxRssMiB = Math.round(process.resourceUsage().maxRSS / 1024);
    console.log(`ts-side files=${manifest.files.length} same=${same} diverged=${diverged} ts-error=${errors.length} gave-up=${gaveUp} typescript=${ts.version} strict=${strict ? 1 : 0} maxRSS=${maxRssMiB}MiB`);
}

main();
