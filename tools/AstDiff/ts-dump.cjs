// The TypeScript half of tools/AstDiff's parse-differential. It loads the reference compiler once, parses every file
// of the manifest with it, dumps each AST in the canonical line format (the same one Program.cs emits), hashes the
// dump text with FNV-1a over UTF-16 code units, and writes a full dump only for the files whose hash differs from
// the port's (port-hashes.txt, written by the C# tool before this runs).
//
// usage: node ts-dump.cjs --manifest <json> --out <dir> --js <lib/typescript.js> [--resume]
//
// Outputs, all in --out: ts-hashes.txt ("<rel>\t<hash>\t<len>" per file, manifest order), ts-errors.txt (only if a
// file could not be parsed), ts/<rel>.txt (the diverging files), and one summary line on stdout.
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

// Same canonical line format as Program.AppendNode: kind(pos,end)[ flags=n][ `text`], with the text only for the five
// kinds whose value TS keeps on the node.
function label(node) {
    let extra = "";
    if (ts.isIdentifier(node) || ts.isPrivateIdentifier(node)) extra = " `" + node.escapedText + "`";
    else if (ts.isStringLiteral(node) || ts.isNumericLiteral(node) || ts.isNoSubstitutionTemplateLiteral(node)) extra = " `" + node.text + "`";
    const flags = node.flags ? " flags=" + node.flags : "";
    return `${node.kind}(${node.pos},${node.end})${flags}${extra}`;
}

// LF only, no trailing newline: the text hashed here is byte-for-byte what Program.Dump produces for the same tree.
// The walk is iterative (an explicit stack, children pushed in reverse) for the same reason TS's own
// forEachChildRecursively is: a left-leaning tree of a few thousand binary expressions is deeper than V8's stack
// (tests/cases/compiler/binderBinaryExpressionStress.ts is exactly that), while the port walks it on a big-stack thread.
function dump(file) {
    const lines = [];
    const stack = [[file, 0]];
    while (stack.length > 0) {
        const [node, depth] = stack.pop();
        lines.push("  ".repeat(depth) + label(node));
        const children = [];
        ts.forEachChild(node, child => { children.push(child); });
        for (let i = children.length - 1; i >= 0; i--) stack.push([children[i], depth + 1]);
    }

    for (const diagnostic of (file.parseDiagnostics || []).concat(file.jsonDiagnostics || [])) {
        lines.push(`DIAG ${diagnostic.code} ${diagnostic.start} ${diagnostic.length} ${diagnostic.messageText}`);
    }
    return lines.join("\n");
}

function scriptKindOf(name) {
    if (/\.tsx$/i.test(name)) return ts.ScriptKind.TSX;
    if (/\.jsx$/i.test(name)) return ts.ScriptKind.JSX;
    if (/\.json$/i.test(name)) return ts.ScriptKind.JSON;
    if (/\.[cm]?js$/i.test(name)) return ts.ScriptKind.JS;
    return ts.ScriptKind.TS;
}

function readIndex(file) {
    const index = new Map();
    if (!fs.existsSync(file)) return index;
    for (const rawLine of fs.readFileSync(file, "utf8").split("\n")) {
        const line = rawLine.endsWith("\r") ? rawLine.slice(0, -1) : rawLine;
        const tab = line.indexOf("\t");
        if (tab > 0) index.set(line.slice(0, tab), line.slice(tab + 1));
    }
    return index;
}

function main(argv) {
    let manifestPath, outDir, jsPath, resume = false;
    for (let i = 0; i < argv.length; i++) {
        switch (argv[i]) {
            case "--manifest": manifestPath = argv[++i]; break;
            case "--out": outDir = argv[++i]; break;
            case "--js": jsPath = argv[++i]; break;
            case "--resume": resume = true; break;
            default: throw new Error(`unknown argument '${argv[i]}'`);
        }
    }
    if (!manifestPath || !outDir || !jsPath) throw new Error("usage: node ts-dump.cjs --manifest <json> --out <dir> --js <lib/typescript.js> [--resume]");

    ts = require(path.resolve(jsPath));
    const manifest = JSON.parse(fs.readFileSync(manifestPath, "utf8"));
    const portHashes = readIndex(path.join(outDir, "port-hashes.txt"));
    const hashesPath = path.join(outDir, "ts-hashes.txt");
    const errorsPath = path.join(outDir, "ts-errors.txt");
    const dumpsDir = path.join(outDir, "ts");
    const done = resume ? readIndex(hashesPath) : new Map();
    const hashOut = fs.openSync(hashesPath, resume ? "a" : "w");
    const errorOut = fs.openSync(errorsPath, resume ? "a" : "w");

    let same = 0, diverged = 0, errors = 0, skipped = 0;
    for (const entry of manifest.files) {
        const rel = entry.path;
        if (done.has(rel)) {
            skipped++;
            continue;
        }
        let line;
        try {
            const text = fs.readFileSync(path.join(manifest.root, rel), "utf8");
            const kind = ts.ScriptKind[entry.kind] ?? scriptKindOf(rel);
            const file = ts.createSourceFile(rel, text, ts.ScriptTarget.ESNext, /*setParentNodes*/ false, kind);
            const dumpText = dump(file);
            const hash = fnv1a(dumpText);
            line = `${rel}\t${hash}\t${dumpText.length}`;
            if (portHashes.get(rel) === `${hash}\t${dumpText.length}`) {
                same++;
            } else {
                diverged++;
                const target = path.join(dumpsDir, rel + ".txt");
                fs.mkdirSync(path.dirname(target), { recursive: true });
                fs.writeFileSync(target, dumpText);
            }
        } catch (ex) {
            errors++;
            fs.writeSync(errorOut, `${rel}\tts-error: ${ex.name}: ${String(ex.message).split("\n")[0]}\n`);
            continue;
        }
        fs.writeSync(hashOut, line + "\n");
    }

    fs.closeSync(hashOut);
    fs.closeSync(errorOut);
    process.stdout.write(`same=${same} diverged=${diverged} errors=${errors} resumed=${skipped}\n`);
}

try {
    main(process.argv.slice(2));
} catch (ex) {
    process.stderr.write(`ts-dump.cjs: ${ex.name}: ${ex.message}\n`);
    process.exit(1);
}
