// The TypeScript half of tools/BindDiff's flow-graph differential: it loads the reference compiler once, binds every
// file of the manifest with it (through a Program + getTypeChecker, which is what triggers binding), dumps the flow
// graph each file's binder produced in the canonical line format (the same one Program.cs emits), hashes that text
// with FNV-1a over UTF-16 code units, and writes a full dump only for the files whose hash differs from the port's
// (port-hashes.txt, written by the C# tool before this runs).
//
// usage: node ts-flow-dump.cjs --manifest <json> --out <dir> --js <lib/typescript.js> [--no-fallthrough]
//
// Outputs, all in --out: ts-hashes.txt ("<rel>\t<hash>\t<length>" per file, manifest order), ts-errors.txt (only if a
// file could not be bound), ts/<rel>.txt (the diverging files), and one summary line on stdout.
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
// every position in a BOM-carrying file shifts by one and the diff reports a divergence that is not in the port.
function stripBom(text) {
    return text.charCodeAt(0) === 0xFEFF ? text.slice(1) : text;
}

// A flow graph is cyclic and shared between nodes, so the dump gives every flow node an id from a deterministic DFS
// (the id map is per "flowKey" call) and prints the graph as lines: the referenced node first, the referrer after.
// node= is the payload the flow node carries: a node's kind(pos,end), the switch-clause payload, or the reduce-label
// payload whose target is dumped as part of the same sequence.
function dumpFlow(flow, out) {
    const ids = new Map();
    let next = 1;
    return describe(flow);

    function describe(f) {
        if (!f) return "-";
        if (ids.has(f)) return "#" + ids.get(f);
        const id = next++;
        ids.set(f, id);
        let nodeDesc = "-";
        if (f.node && typeof f.node.kind === "number") {
            nodeDesc = `${f.node.kind}(${f.node.pos},${f.node.end})`;
        } else if (f.node && f.node.switchStatement) {
            nodeDesc = `clause(${f.node.switchStatement.pos},${f.node.switchStatement.end},${f.node.clauseStart},${f.node.clauseEnd})`;
        } else if (f.node && f.node.target) {
            nodeDesc = `reduce(target=${describe(f.node.target)},antecedents=${f.node.antecedents ? f.node.antecedents.length : 0})`;
        }
        let anteDesc = "-";
        if (Array.isArray(f.antecedent)) anteDesc = "[" + f.antecedent.map(describe).join(",") + "]";
        else if (f.antecedent) anteDesc = describe(f.antecedent);
        out.push(`F#${id} flags=${f.flags} node=${nodeDesc} ante=${anteDesc}`);
        return "#" + id;
    }
}

function dumpNode(node, depth, out) {
    const flow = node.flowNode ? dumpFlow(node.flowNode, out) : "-";
    out.push(`N${depth} ${node.kind}(${node.pos},${node.end}) nflags=${node.flags === undefined ? 0 : node.flags} flow=${flow}`);
    if (node.kind === ts.SyntaxKind.SourceFile) {
        out.push(`X${depth} endFlowNode=${node.endFlowNode ? dumpFlow(node.endFlowNode, out) : "-"}`);
    }
    if (node.kind === ts.SyntaxKind.SwitchStatement) {
        out.push(`X${depth} possiblyExhaustive=${node.possiblyExhaustive === undefined ? "-" : node.possiblyExhaustive ? 1 : 0}`);
    }
    if (node.kind === ts.SyntaxKind.ConditionalExpression) {
        out.push(`X${depth} flowNodeWhenTrue=${node.flowNodeWhenTrue ? dumpFlow(node.flowNodeWhenTrue, out) : "-"} flowNodeWhenFalse=${node.flowNodeWhenFalse ? dumpFlow(node.flowNodeWhenFalse, out) : "-"}`);
    }
    if (node.fallthroughFlowNode) {
        out.push(`X${depth} fallthroughFlowNode=${dumpFlow(node.fallthroughFlowNode, out)}`);
    }
    ts.forEachChild(node, (child) => dumpNode(child, depth + 1, out));
}

// The file name must be the corpus-relative path on both sides: the parser derives "is this a declaration
// file?" from the name, so a placeholder name would keep the port and the reference from writing the same
// source-file flags (.d.ts files) and report a divergence that is not in the port.
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
    program.getTypeChecker(); // initializes binding: this is what builds the flow graph
    const out = [];
    dumpNode(program.getSourceFile(fileName), 0, out);
    return out.join("\n") + "\n";
}

function writeDump(outDir, rel, text) {
    const target = path.join(outDir, rel + ".txt");
    fs.mkdirSync(path.dirname(target), { recursive: true });
    fs.writeFileSync(target, text);
}

function main() {
    const args = process.argv.slice(2);
    let manifestPath, outDir, jsPath, noFallthrough = false;
    // The driver runs this script once per *batch* so that node's heap does not grow with the corpus: each Program plus
    // its type checker is enormous and node does not reclaim them quickly. The batch then also needs to say where to
    // read the port's hashes and where to put its own outputs, since several batches share one output directory.
    let portHashesArg, hashesArg, errorsArg, dumpsDir;
    for (let i = 0; i < args.length; i++) {
        switch (args[i]) {
            case "--manifest": manifestPath = args[++i]; break;
            case "--out": outDir = args[++i]; break;
            case "--js": jsPath = args[++i]; break;
            case "--no-fallthrough": noFallthrough = true; break;
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
    // allowJs: a `.js` root file is only a supported program root when it is on, and the corpus has a few. It is the
    // only option here that the port's binder does not read (it reads target, alwaysStrict and
    // noFallthroughCasesInSwitch), so turning it on cannot move the two sides apart.
    const options = { target: ts.ScriptTarget.ESNext, allowJs: true };
    if (noFallthrough) options.noFallthroughCasesInSwitch = true;

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
    let same = 0, diverged = 0;
    for (const entry of manifest.files) {
        const text = stripBom(fs.readFileSync(path.join(manifest.root, entry.path), "utf8"));
        let dumpText;
        try {
            dumpText = dump(text, options, entry.kind, entry.path);
        } catch (e) {
            errors.push(`${entry.path}\tts-crash: ${e && e.message ? e.message : String(e)}`);
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

    fs.writeFileSync(hashesArg || path.join(outDir, "ts-hashes.txt"), hashes.map((h) => h + "\n").join(""));
    fs.writeFileSync(errorsArg || path.join(outDir, "ts-errors.txt"), errors.map((e) => e + "\n").join(""));
    // The peak RSS is reported from inside node: the driver cannot ask for it after the process exits, and this is the
    // number that says whether a batch size is small enough for the machine (see --node-batch in Program.cs).
    const maxRssMiB = Math.round(process.resourceUsage().maxRSS / 1024);
    console.log(`ts-side files=${manifest.files.length} same=${same} diverged=${diverged} ts-error=${errors.length} typescript=${ts.version} maxRSS=${maxRssMiB}MiB`);
}

main();
