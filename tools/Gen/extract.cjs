// Extracts the type model that tools/Gen consumes, using the TypeScript compiler's own parser.
//
// Why this exists: the C# generator originally scanned types.ts line by line, which broke on several
// perfectly ordinary TS constructs (a member with no trailing `;`, nested generics in a parameter list,
// a one-line interface, a `}` that also opens the next declaration, a `const enum` value spanning lines).
// TypeScript's own parser already handles all of that, so the extractor runs it and hands the C# side a
// small, stable JSON shape instead. See docs/architecture.md §11.3 item 6.
//
// Usage: node tools/Gen/extract.cjs <typeScriptRepoRoot> <outputJsonPath> <sourceRelative> [moreSources...]

const fs = require("node:fs");
const path = require("node:path");

const tsRoot = process.argv[2];
const outPath = process.argv[3];
const sources = process.argv.slice(4);

if (!tsRoot || !outPath || sources.length === 0) {
    console.error("usage: node extract.cjs <typeScriptRepoRoot> <outputJsonPath> <sourceRelative> [moreSources...]");
    process.exit(2);
}

const ts = require(path.join(tsRoot, "lib", "typescript.js"));

const enums = [];
const interfaces = [];
const aliases = [];

for (const sourceRelative of sources) {
    const filePath = path.join(tsRoot, sourceRelative);
    const text = fs.readFileSync(filePath, "utf8");
    const source = ts.createSourceFile(filePath, text, ts.ScriptTarget.Latest, /* setParentNodes */ true, ts.ScriptKind.TS);
    collect(source, sourceRelative);
}

fs.mkdirSync(path.dirname(outPath), { recursive: true });
fs.writeFileSync(
    outPath,
    JSON.stringify({ source: sources.join(", "), enums, interfaces, aliases }, null, 1),
    "utf8");

console.log(`extract: ${enums.length} enums, ${interfaces.length} interfaces, ${aliases.length} aliases -> ${outPath}`);

function collect(source, sourceRelative) {
    const text = source.text;

    for (const statement of source.statements) {
        if (ts.isEnumDeclaration(statement)) {
            enums.push({
                name: statement.name.text,
                isConst: hasModifier(statement, ts.SyntaxKind.ConstKeyword),
                source: sourceRelative,
                startLine: lineAt(statement.getStart(source)),
                endLine: lineAt(statement.end),
                members: statement.members.map(member => ({
                    name: memberName(member.name),
                    valueText: member.initializer ? member.initializer.getText(source) : null,
                    doc: docOf(member),
                    trailing: trailingCommentOf(member),
                })),
            });
        } else if (ts.isInterfaceDeclaration(statement)) {
            const kindMember = statement.members.find(
                m => ts.isPropertySignature(m) && memberName(m.name) === "kind"
                    && m.type && m.type.getText(source).startsWith("SyntaxKind."));

            interfaces.push({
                name: statement.name.text,
                typeParameters: (statement.typeParameters || []).map(p => p.name.text),
                // `node.getText()` on the heritage type keeps generic arguments, which the C# side strips
                // when it needs a bare name.
                extends: (statement.heritageClauses || [])
                    .filter(clause => clause.token === ts.SyntaxKind.ExtendsKeyword)
                    .flatMap(clause => clause.types.map(t => t.getText(source))),
                // Only property signatures become data members. Method signatures (`foo(x): void`) are
                // skipped: TS declares them as interface methods, and the port models those as delegates
                // where they are needed rather than as fields.
                members: statement.members.filter(ts.isPropertySignature).map(member => ({
                    name: memberName(member.name),
                    type: member.type ? member.type.getText(source) : "any",
                    isReadonly: hasModifier(member, ts.SyntaxKind.ReadonlyKeyword),
                    isOptional: !!member.questionToken,
                    doc: docOf(member),
                    trailing: trailingCommentOf(member),
                })),
                doc: docOf(statement),
                concreteKind: kindMember ? kindMember.type.getText(source).slice("SyntaxKind.".length) : null,
                source: sourceRelative,
                startLine: lineAt(statement.getStart(source)),
                endLine: lineAt(statement.end),
            });
        } else if (ts.isTypeAliasDeclaration(statement)) {
            aliases.push({
                name: statement.name.text,
                typeParameters: (statement.typeParameters || []).map(p => p.name.text),
                value: statement.type.getText(source),
                doc: docOf(statement),
                source: sourceRelative,
                startLine: lineAt(statement.getStart(source)),
                endLine: lineAt(statement.end),
            });
        }
    }

    function hasModifier(node, kind) {
        return !!node.modifiers && node.modifiers.some(m => m.kind === kind);
    }

    function lineAt(pos) {
        return source.getLineAndCharacterOfPosition(pos).line + 1;
    }

    function memberName(name) {
        // Property names can be identifiers, string literals or computed; only the first two occur here.
        return name.getText(source).replace(/^["']|["']$/g, "");
    }

    function docOf(node) {
        const jsDoc = node.jsDoc;
        if (!jsDoc || jsDoc.length === 0) return null;

        const comment = jsDoc[jsDoc.length - 1].comment;
        if (comment === undefined) return null;

        const value = typeof comment === "string" ? comment : ts.displayPartsToString(comment);
        return value.length === 0 ? null : value;
    }

    function trailingCommentOf(node) {
        const ranges = ts.getTrailingCommentRanges(text, node.end);
        if (!ranges || ranges.length === 0) return null;

        return text.slice(ranges[0].pos, ranges[0].end).replace(/^\/\/\s*/, "").trim() || null;
    }
}
