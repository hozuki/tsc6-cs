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

// Data tables the scanner needs. They are plain literals in the TS source, but there are 4,454 Unicode range
// endpoints among them plus the keyword/token/flag tables, so they are evaluated here rather than copied by
// hand. Order matters: `textToToken` spreads `textToKeywordObj`, so the latter must come first.
const tableSources = [
    ["src/compiler/types.ts", "LanguageFeatureMinimumTarget"],
    ["src/compiler/scanner.ts", "textToKeywordObj"],
    ["src/compiler/scanner.ts", "textToToken"],
    ["src/compiler/scanner.ts", "charCodeToRegExpFlag"],
    ["src/compiler/scanner.ts", "regExpFlagToFirstAvailableLanguageVersion"],
    ["src/compiler/scanner.ts", "unicodeES5IdentifierStart"],
    ["src/compiler/scanner.ts", "unicodeES5IdentifierPart"],
    ["src/compiler/scanner.ts", "unicodeESNextIdentifierStart"],
    ["src/compiler/scanner.ts", "unicodeESNextIdentifierPart"],
    ["src/compiler/scanner.ts", "nonBinaryUnicodeProperties"],
    ["src/compiler/scanner.ts", "binaryUnicodeProperties"],
    ["src/compiler/scanner.ts", "binaryUnicodePropertiesOfStrings"],
    ["src/compiler/scanner.ts", "valuesOfNonBinaryUnicodeProperties"],
];

const ts = require(path.join(tsRoot, "lib", "typescript.js"));

// Indexed-access types (`LiteralTypeNode["literal"]`, `HeritageClause["token"]`) are TS's way of naming a
// member's type from the outside, and the *checker* knows what that member holds. Reading the declaration
// text alone leaves `X["y"]` unresolved on the C# side, where it has no spelling at all, so a program is
// built and the checker is consulted for exactly those nodes. Building it costs about a second (the
// compiler pulls in ~170 files); if it fails, extraction continues and such types stay as written.
let checker = null;
let program = null;
try {
    program = ts.createProgram(
        sources.map(sourceRelative => path.join(tsRoot, sourceRelative)),
        {
            target: ts.ScriptTarget.ESNext,
            module: ts.ModuleKind.ESNext,
            skipLibCheck: true,
            noEmit: true,
            strict: false,
        });
    checker = program.getTypeChecker();
} catch (error) {
    console.warn(`extract: no type checker available, indexed-access types stay as written (${error.message})`);
}

const enums = [];
const interfaces = [];
const aliases = [];

for (const sourceRelative of sources) {
    const filePath = path.join(tsRoot, sourceRelative);
    // The file has to come from the program for the checker to recognise its nodes; the fallback keeps
    // extraction working when the program could not be built.
    const source = program?.getSourceFile(filePath)
        ?? ts.createSourceFile(filePath, fs.readFileSync(filePath, "utf8"), ts.ScriptTarget.Latest, /* setParentNodes */ true, ts.ScriptKind.TS);
    collect(source, sourceRelative);
}

const tables = [];
for (const [sourceRelative, name] of tableSources) {
    tables.push(collectTable(sourceRelative, name));
}

mergeDuplicateInterfaces(interfaces);

// The node-predicate table's source (see collectPredicates below).
const predicateSource = "src/compiler/factory/nodeTests.ts";

const predicates = collectPredicates();

// The node factory's fixed-shape members (docs/architecture.md §15.7).
const factory = collectFactory();

fs.mkdirSync(path.dirname(outPath), { recursive: true });fs.writeFileSync(
    outPath,
    JSON.stringify({ source: sources.join(", "), enums, interfaces, aliases, tables, predicates, factory }, null, 1),
    "utf8");

console.log(`extract: ${enums.length} enums, ${interfaces.length} interfaces, ${aliases.length} aliases, ${tables.length} tables, ${predicates.length} predicates, ${factory.members.length} factory members (${factory.members.filter(m => m.supported).length} supported) -> ${outPath}`);

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
                // Property and method signatures both become members. Methods are part of the contract, not an
                // implementation detail that can be left to delegates where needed: `NodeFactory` alone declares
                // 520 of them (`createIdentifier(name: string): Identifier`), and `TypeChecker` another 181.
                // Dropping them produced interfaces that no implementation could be checked against.
                members: statement.members
                    .filter(member => ts.isPropertySignature(member) || ts.isMethodSignature(member))
                    .map(member => ts.isMethodSignature(member)
                        ? {
                            name: memberName(member.name),
                            isMethod: true,
                            type: typeTextOf(member.type, source) ?? "void",
                            parameters: member.parameters.map(parameter => ({
                                name: parameter.name.getText(source),
                                type: typeTextOf(parameter.type, source) ?? "any",
                                isOptional: !!parameter.questionToken,
                                isRest: !!parameter.dotDotDotToken,
                            })),
                            typeParameters: (member.typeParameters ?? []).map(typeParameter => ({
                                name: typeParameter.name.text,
                                constraint: typeTextOf(typeParameter.constraint, source),
                            })),
                            isReadonly: false,
                            isOptional: !!member.questionToken,
                            doc: docOf(member),
                            trailing: trailingCommentOf(member),
                        }
                        : {
                            name: memberName(member.name),
                            isMethod: false,
                            type: typeTextOf(member.type, source) ?? "any",
                            parameters: [],
                            typeParameters: [],
                            isReadonly: hasModifier(member, ts.SyntaxKind.ReadonlyKeyword),
                            isOptional: !!member.questionToken,
                            doc: docOf(member),
                            trailing: trailingCommentOf(member),
                        }),
                doc: docOf(statement),
                concreteKind: kindMember ? typeTextOf(kindMember.type, source).slice("SyntaxKind.".length) : null,
                source: sourceRelative,
                startLine: lineAt(statement.getStart(source)),
                endLine: lineAt(statement.end),
            });
        } else if (ts.isTypeAliasDeclaration(statement)) {
            aliases.push({
                name: statement.name.text,
                typeParameters: (statement.typeParameters || []).map(p => p.name.text),
                value: typeTextOf(statement.type, source, statement.name.text) ?? "any",
                doc: docOf(statement),
                source: sourceRelative,
                startLine: lineAt(statement.getStart(source)),
                endLine: lineAt(statement.end),
            });
        }
    }

    // The declared type text of a type node, with indexed-access types resolved through the checker. TS writes
    // `X["y"]` as a type selector (`LiteralTypeNode["literal"]` is `NullLiteral | BooleanLiteral | …`) and what
    // the C# side needs is the member's real type. An answer that is still an indexed access — `T["name"]` on an
    // unresolved type parameter — keeps the written text, which the C# side degrades to `object?` with a note.
    function typeTextOf(node, source, selfName) {
        if (!node) return null;

        const written = node.getText(source);
        if (!checker || !ts.isIndexedAccessTypeNode(node)) return written;

        try {
            const resolved = checker.typeToString(
                checker.getTypeAtLocation(node),
                /* enclosingDeclaration */ undefined,
                ts.TypeFormatFlags.NoTruncation);

            // Keep the written form when the answer does not name anything new: still an indexed access
            // (`T["name"]` on an unresolved type parameter), or a recursive alias resolving to itself
            // (`type A<T> = { … }[…]` answers `A<T>`, which says nothing more than the declaration did).
            if (resolved.includes("[\"") || resolved.includes("['")) return written;
            if (selfName && new RegExp(`\\b${selfName}\\b`).test(resolved)) return written;
            return resolved;
        } catch {
            return written;
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

// -----------------------------------------------------------------------------------------------
// Declaration merging
// -----------------------------------------------------------------------------------------------

// TS lets an interface be declared more than once; the compiler merges the declarations into one type. The collector
// sees each declaration separately, so an interface declared twice would otherwise overwrite itself in the
// generator's by-name map — which is how `SourceFile` lost its `Declaration` base and stopped being a class. Three
// names in types.ts are affected: `SourceFile`, `Program` and `TypeNode`.
function mergeDuplicateInterfaces(list) {
    const byName = new Map();

    // Backwards, so splicing out a duplicate does not disturb the indices still to visit.
    for (let i = list.length - 1; i >= 0; i--) {
        const iface = list[i];
        const first = byName.get(iface.name);
        if (first === undefined) {
            byName.set(iface.name, iface);
            continue;
        }

        for (const base of iface.extends) {
            if (!first.extends.includes(base)) first.extends.push(base);
        }

        for (const member of iface.members) {
            if (!first.members.some(existing => existing.name === member.name)) first.members.push(member);
        }

        first.startLine = Math.min(first.startLine, iface.startLine);
        first.endLine = Math.max(first.endLine, iface.endLine);
        list.splice(i, 1);
    }
}

// -----------------------------------------------------------------------------------------------
// Node predicates
// -----------------------------------------------------------------------------------------------

// `factory/nodeTests.ts` is 227 exported functions that each test one `SyntaxKind`; all of them are a single
// `return node.kind === SyntaxKind.X;`. That is a table, not code, so it is generated like the others rather than
// transcribed by hand. A body of any other shape aborts the run.
function collectPredicates() {
    const filePath = path.join(tsRoot, predicateSource);
    const text = fs.readFileSync(filePath, "utf8");
    const source = ts.createSourceFile(filePath, text, ts.ScriptTarget.Latest, /* setParentNodes */ true, ts.ScriptKind.TS);

    const result = [];
    for (const statement of source.statements) {
        if (!ts.isFunctionDeclaration(statement) || !statement.body || !statement.name) continue;

        const body = statement.body.statements.map(s => s.getText(source)).join(" ").replace(/\s+/g, " ").trim();
        const returned = /^return (.+);$/.exec(body);
        if (!returned) {
            throw new Error(`unexpected predicate body for '${statement.name.text}': ${body}`);
        }

        // Almost every predicate is one kind test; a few (`isModuleExportName`) are two joined by `||`.
        const kinds = returned[1].split(/\s*\|\|\s*/).map(part => {
            const match = /^\w+\.kind === SyntaxKind\.(\w+)$/.exec(part.trim());
            if (!match) {
                throw new Error(`unexpected predicate body for '${statement.name.text}': ${body}`);
            }

            return match[1];
        });

        result.push({
            name: statement.name.text,
            kinds,
            startLine: source.getLineAndCharacterOfPosition(statement.getStart(source)).line + 1,
        });
    }

    return result;
}

// -----------------------------------------------------------------------------------------------
// Data tables
// -----------------------------------------------------------------------------------------------

// A table value is plain JSON, with one addition: a reference to an exported enum member
// (`SyntaxKind.X`, `CharacterCodes.a`, `LanguageFeatureMinimumTarget.Y`) is emitted as
// { "@ref": "SyntaxKind.X" }, so the C# side can look the member up in the generated enum instead of
// hard-coding the number. An unset value (`undefined! as Set<string>`) becomes { "@undefined": true }.
function collectTable(sourceRelative, name) {
    const filePath = path.join(tsRoot, sourceRelative);
    const text = fs.readFileSync(filePath, "utf8");
    const source = ts.createSourceFile(filePath, text, ts.ScriptTarget.Latest, /* setParentNodes */ true, ts.ScriptKind.TS);

    const known = new Map(tables.map(table => [table.name, table.value]));

    let declaration;
    for (const statement of source.statements) {
        if (!ts.isVariableStatement(statement)) continue;
        for (const candidate of statement.declarationList.declarations) {
            if (ts.isIdentifier(candidate.name) && candidate.name.text === name) declaration = candidate;
        }
    }

    if (!declaration || !declaration.initializer) {
        throw new Error(`table '${name}' not found in ${sourceRelative}`);
    }

    const value = evaluate(declaration.initializer);

    // TS assigns one property of a table after its literal
    // (`valuesOfNonBinaryUnicodeProperties.Script_Extensions = …Script;`); follow those assignments.
    for (const statement of source.statements) {
        if (!ts.isExpressionStatement(statement)) continue;
        const expression = statement.expression;
        if (!ts.isBinaryExpression(expression) || expression.operatorToken.kind !== ts.SyntaxKind.EqualsToken) continue;
        if (!ts.isPropertyAccessExpression(expression.left) || !ts.isPropertyAccessExpression(expression.right)) continue;
        if (expression.left.expression.getText(source) !== name || expression.right.expression.getText(source) !== name) continue;

        value[expression.left.name.text] = value[expression.right.name.text];
    }

    return {
        name,
        source: sourceRelative,
        startLine: lineAt(declaration.getStart(source)),
        endLine: lineAt(declaration.end),
        value,
    };

    function lineAt(pos) {
        return source.getLineAndCharacterOfPosition(pos).line + 1;
    }

    function evaluate(node) {
        switch (node.kind) {
            case ts.SyntaxKind.NumericLiteral:
                return Number(node.text);
            case ts.SyntaxKind.StringLiteral:
            case ts.SyntaxKind.NoSubstitutionTemplateLiteral:
                return node.text;
            case ts.SyntaxKind.TrueKeyword:
                return true;
            case ts.SyntaxKind.FalseKeyword:
                return false;
            case ts.SyntaxKind.Identifier:
                if (node.text === "undefined") return { "@undefined": true };
                if (known.has(node.text)) return known.get(node.text);
                throw new Error(`table '${name}': unsupported identifier '${node.text}'`);
            case ts.SyntaxKind.PropertyAccessExpression:
                return { "@ref": node.getText(source) };
            case ts.SyntaxKind.PrefixUnaryExpression: {
                const operand = evaluate(node.operand);
                if (typeof operand !== "number") throw new Error(`table '${name}': unary operand is not a number`);
                return node.operator === ts.SyntaxKind.MinusToken ? -operand : operand;
            }
            case ts.SyntaxKind.BinaryExpression: {
                // Only string concatenation occurs (the computed `constructor` key).
                if (node.operatorToken.kind !== ts.SyntaxKind.PlusToken) {
                    throw new Error(`table '${name}': unsupported operator '${node.operatorToken.getText(source)}'`);
                }

                const left = evaluate(node.left);
                const right = evaluate(node.right);
                if (typeof left !== "string" || typeof right !== "string") {
                    throw new Error(`table '${name}': '+' is only supported on strings`);
                }

                return left + right;
            }
            case ts.SyntaxKind.ParenthesizedExpression:
            case ts.SyntaxKind.AsExpression:
            // `undefined! as Set<string>` — the value TS fills in afterwards.
            case ts.SyntaxKind.NonNullExpression:
                return evaluate(node.expression);
            case ts.SyntaxKind.ArrayLiteralExpression:
                return evaluateArray(node);
            case ts.SyntaxKind.ObjectLiteralExpression:
                return evaluateObject(node);
            case ts.SyntaxKind.NewExpression:
                return evaluateNew(node);
            case ts.SyntaxKind.CallExpression:
                return evaluateCall(node);
            default:
                throw new Error(`table '${name}': unsupported expression '${ts.SyntaxKind[node.kind]}'`);
        }
    }

    function evaluateArray(node) {
        const result = [];
        for (const element of node.elements) {
            // `...otherTable` spreads another table in place.
            if (ts.isSpreadElement(element)) {
                const spread = evaluate(element.expression);
                if (!Array.isArray(spread)) throw new Error(`table '${name}': only arrays can be spread`);
                result.push(...spread);
                continue;
            }

            result.push(evaluate(element));
        }

        return result;
    }

    function evaluateObject(node) {
        const result = {};
        for (const property of node.properties) {
            // `...textToKeywordObj` — the spread of another table.
            if (ts.isSpreadAssignment(property)) {
                Object.assign(result, evaluate(property.expression));
                continue;
            }

            if (!ts.isPropertyAssignment(property)) {
                throw new Error(`table '${name}': unsupported property '${ts.SyntaxKind[property.kind]}'`);
            }

            result[propertyNameOf(property)] = evaluate(property.initializer);
        }

        return result;
    }

    function propertyNameOf(property) {
        const propertyName = property.name;

        // `["" + "constructor"]: …` — TS computes the name to keep `constructor` off the object's prototype.
        if (ts.isComputedPropertyName(propertyName)) {
            const value = evaluate(propertyName.expression);
            if (typeof value !== "string") throw new Error(`table '${name}': computed property name is not a string`);
            return value;
        }

        if (ts.isIdentifier(propertyName) || ts.isStringLiteral(propertyName)) return propertyName.text;

        throw new Error(`table '${name}': unsupported property name '${propertyName.getText(source)}'`);
    }

    function evaluateNew(node) {
        const callee = node.expression.getText(source);
        if (callee === "Map") return evaluate(node.arguments[0]);
        if (callee === "Set") return evaluate(node.arguments[0]);
        throw new Error(`table '${name}': unsupported constructor '${callee}'`);
    }

    function evaluateCall(node) {
        const callee = node.expression.getText(source);
        if (callee === "Object.entries") return Object.entries(evaluate(node.arguments[0]));
        throw new Error(`table '${name}': unsupported call '${callee}'`);
    }
}

// ---------------------------------------------------------------------------------------------
// The node factory
// ---------------------------------------------------------------------------------------------

// The fixed-shape `create*`/`update*` members of `createNodeFactory` (docs/architecture.md §15.7): a body that is a
// `createBase*` call plus field assignments, or a single comparison chain forwarding to `update(createX(...), node)`.
// Everything else in that file — the helpers, the members with real logic, the module-level functions — stays
// hand-written, which is why anything this cannot describe exactly is reported as unsupported rather than guessed at.
function collectFactory() {
    const sourceRelative = "src/compiler/factory/nodeFactory.ts";
    const filePath = path.join(tsRoot, sourceRelative);
    const source = program?.getSourceFile(filePath)
        ?? ts.createSourceFile(filePath, fs.readFileSync(filePath, "utf8"), ts.ScriptTarget.Latest, true, ts.ScriptKind.TS);

    let container = null;
    (function find(node) {
        if (ts.isFunctionDeclaration(node) && node.name && node.name.text === "createNodeFactory") { container = node.body; return; }
        ts.forEachChild(node, find);
    })(source);

    if (!container) throw new Error(`${sourceRelative}: createNodeFactory not found`);

    const members = [];
    for (const statement of container.statements) {
        if (!ts.isFunctionDeclaration(statement) || !statement.body) continue;
        const name = statement.name.text;
        if (!name.startsWith("create") && !name.startsWith("update")) continue;
        members.push(describe(statement));
    }

    return { source: sourceRelative, members };

    function text(node) {
        return node.getText(source).replace(/\s+/g, " ");
    }

    function describe(fn) {
        const name = fn.name.text;
        const kind = name.startsWith("update") ? "update" : "create";
        const common = {
            name,
            kind,
            returnType: fn.type ? fn.type.getText(source) : "any",
            typeParameters: (fn.typeParameters ?? []).map(typeParameter => ({
                name: typeParameter.name.text,
                constraint: typeParameter.constraint ? typeParameter.constraint.getText(source) : null,
            })),
            parameters: fn.parameters.map(parameter => ({
                name: parameter.name.getText(source),
                type: parameter.type ? parameter.type.getText(source) : "any",
                isOptional: !!parameter.questionToken,
                isRest: !!parameter.dotDotDotToken,
            })),
        };
        const unsupported = reason => ({ ...common, supported: false, reason });

        const body = fn.body.statements;
        return kind === "create" ? describeCreate(body, common, unsupported) : describeUpdate(body, common, unsupported);
    }

    function describeCreate(body, common, unsupported) {
        const first = body[0];
        const declaration = first && ts.isVariableStatement(first) ? first.declarationList.declarations[0] : null;
        const initializer = declaration && declaration.initializer;
        const base = initializer && ts.isCallExpression(initializer) && ts.isIdentifier(initializer.expression)
            && /^createBase/.test(initializer.expression.text) ? initializer : null;
        if (!base) return unsupported("the body does not start with `const node = createBase*(...)`");

        const last = body[body.length - 1];
        const tail = ts.isReturnStatement(last) ? text(last.expression) : "<none>";
        if (tail !== "node" && !/^update\(/.test(tail)) return unsupported(`unexpected tail: ${tail}`);

        const assignments = [];
        const calls = [];
        for (const statement of body.slice(1, body.length - 1)) {
            if (!ts.isExpressionStatement(statement)) return unsupported(`not an expression statement: ${text(statement)}`);

            const expression = statement.expression;
            if (ts.isCallExpression(expression)) {
                if (!ts.isIdentifier(expression.expression)) return unsupported(`unexpected call: ${text(statement)}`);
                calls.push({
                    name: expression.expression.text,
                    args: expression.arguments.map(argument => encodeExpression(argument, source)),
                });
                continue;
            }

            if (!ts.isBinaryExpression(expression) || !ts.isPropertyAccessExpression(expression.left)
                || text(expression.left.expression) !== "node") {
                return unsupported(`unexpected statement: ${text(statement)}`);
            }

            const op = expression.operatorToken.getText(source);
            if (op !== "=" && op !== "|=" && op !== "&=") return unsupported(`unexpected assignment operator '${op}'`);
            assignments.push({
                target: expression.left.name.getText(source),
                op,
                value: encodeExpression(expression.right, source),
            });
        }

        return {
            ...common,
            supported: true,
            reason: null,
            base: base.expression.text,
            typeArgument: base.typeArguments && base.typeArguments.length ? base.typeArguments[0].getText(source) : null,
            kindExpression: base.arguments.length ? base.arguments[0].getText(source) : null,
            tail,
            assignments,
            calls,
        };
    }

    function describeUpdate(body, common, unsupported) {
        if (body.length !== 1 || !ts.isReturnStatement(body[0]) || !ts.isConditionalExpression(body[0].expression)) {
            return unsupported("the body is not a single `return <comparisons> ? update(createX(...), node) : node`");
        }

        const conditional = body[0].expression;
        const whenTrue = conditional.whenTrue;
        if (!ts.isCallExpression(whenTrue) || !ts.isIdentifier(whenTrue.expression) || whenTrue.expression.text !== "update"
            || whenTrue.arguments.length !== 2 || !ts.isCallExpression(whenTrue.arguments[0])
            || text(whenTrue.arguments[1]) !== "node") {
            return unsupported(`unexpected consequent: ${text(whenTrue)}`);
        }

        if (!ts.isIdentifier(conditional.whenFalse) || conditional.whenFalse.text !== "node") {
            return unsupported(`unexpected alternative: ${text(conditional.whenFalse)}`);
        }

        const compares = [];
        let comparisonsOnly = true;
        (function walk(node) {
            if (ts.isBinaryExpression(node) && node.operatorToken.kind === ts.SyntaxKind.BarBarToken) {
                walk(node.left);
                walk(node.right);
                return;
            }

            if (ts.isBinaryExpression(node) && node.operatorToken.kind === ts.SyntaxKind.ExclamationEqualsEqualsToken
                && ts.isPropertyAccessExpression(node.left) && text(node.left.expression) === "node") {
                compares.push({ target: node.left.name.getText(source), value: encodeExpression(node.right, source) });
                return;
            }

            comparisonsOnly = false;
        })(conditional.condition);

        const create = whenTrue.arguments[0];
        if (!comparisonsOnly || !ts.isIdentifier(create.expression)) {
            return unsupported(`unexpected comparison or create call: ${text(conditional)}`);
        }

        return {
            ...common,
            // TS leaves the return type off an `update*` member: it returns the node it was given, so the type is the
            // one its first parameter declares.
            returnType: common.parameters[0].type,
            supported: true,
            reason: null,
            compares,
            updateCreate: { name: create.expression.text, args: create.arguments.map(argument => encodeExpression(argument, source)) },
        };
    }
}

// A TS expression, reduced to the shapes the fixed-shape members use (docs/architecture.md §15.7). Anything else comes
// back as `unsupported` with its source text, so the emitter can leave that member to the hand-written half instead of
// guessing — the same "fail rather than emit something half-right" rule the table extractors follow.
function encodeExpression(node, source) {
    if (!node) return null;

    const written = node.getText(source).replace(/\s+/g, " ");
    const unsupported = () => ({ form: "unsupported", text: written });

    if (ts.isIdentifier(node)) return { form: "identifier", name: node.text };
    if (node.kind === ts.SyntaxKind.TrueKeyword) return { form: "boolean", boolean: true };
    if (node.kind === ts.SyntaxKind.FalseKeyword) return { form: "boolean", boolean: false };
    if (node.kind === ts.SyntaxKind.NullKeyword) return { form: "null" };
    if (ts.isStringLiteral(node)) return { form: "string", value: node.text };
    if (ts.isNumericLiteral(node)) return { form: "number", value: node.text };
    if (ts.isParenthesizedExpression(node)) return { form: "paren", inner: encodeExpression(node.expression, source) };
    if (ts.isNonNullExpression(node)) return { form: "nonNull", inner: encodeExpression(node.expression, source) };

    if (ts.isPropertyAccessExpression(node)) {
        return { form: "property", target: encodeExpression(node.expression, source), name: node.name.getText(source) };
    }

    if (ts.isCallExpression(node)) {
        if (ts.isIdentifier(node.expression)) {
            return {
                form: "call",
                name: node.expression.text,
                args: node.arguments.map(argument => encodeExpression(argument, source)),
            };
        }

        const callee = node.expression;
        if (ts.isPropertyAccessExpression(callee) && ts.isCallExpression(callee.expression)
            && ts.isIdentifier(callee.expression.expression)) {
            return {
                form: "method",
                receiver: callee.expression.expression.text,
                name: callee.name.getText(source),
                args: node.arguments.map(argument => encodeExpression(argument, source)),
            };
        }

        return unsupported();
    }

    if (ts.isBinaryExpression(node)) {
        return {
            form: "binary",
            op: node.operatorToken.getText(source),
            left: encodeExpression(node.left, source),
            right: encodeExpression(node.right, source),
        };
    }

    if (ts.isPrefixUnaryExpression(node)) {
        // The operator is the first character of the expression (`~x`, `!x`, `-x`).
        return { form: "prefix", op: written[0], operand: encodeExpression(node.operand, source) };
    }

    if (ts.isConditionalExpression(node)) {
        return {
            form: "conditional",
            condition: encodeExpression(node.condition, source),
            whenTrue: encodeExpression(node.whenTrue, source),
            whenFalse: encodeExpression(node.whenFalse, source),
        };
    }

    if (ts.isArrayLiteralExpression(node)) {
        return { form: "array", items: node.elements.map(element => encodeExpression(element, source)) };
    }

    return unsupported();
}
