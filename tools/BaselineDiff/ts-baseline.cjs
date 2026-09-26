// The TypeScript half of tools/BaselineDiff's baseline differential: it loads the reference compiler once, replays the
// TypeScript conformance corpus through the *official test harness's* option model (the per-file `// @` directives of
// src/harness/harnessIO.ts's TestCaseParser, the harness host of src/harness/fakesHosts.ts, the defaults of
// Compiler.compileFiles and CompilerBaselineRunner), renders the same baseline texts the harness checks in under
// tests/baselines/reference (`.js`, `.errors.txt`, `.types`, `.symbols`), hashes them with FNV-1a over UTF-16 code
// units and reports, per corpus file and per derived configuration, whether that text equals the checked-in baseline.
//
// usage: node ts-baseline.cjs --manifest <json> --out <dir> --js <lib/typescript.js> --baselines <tests/baselines/reference>
//            [--hashes <file>] [--cases <file>] [--dumps <dir>] [--limit <n>]
//
// The manifest is {"root": <corpus root>, "workspaceRoot": <the TypeScript checkout>, "files": [{"path": <root-relative>, "kind": "TS"}]}.
// Only the corpus files are read from the manifest; the harness host's virtual file system is the one Compiler.compileFiles
// builds (`/.src` for the test units, `/.ts` mounted at the reference's `lib/` for the default library files it names,
// `/.lib` mounted at `tests/lib`), with `/.src` as the current directory and `/.ts` as `defaultLibLocation` — the two
// prefixes Utils.removeTestPathPrefixes strips from everything a baseline prints.
//
// Outputs, all in --out: ts-cases.jsonl (one record per (file, configuration, artifact): the harness model's settings,
// resolved options, derived baseline stem and the artifact's hash/verdict), ts-summary.txt (one line per node batch) and
// ts/<stem>.<ext> for every artifact that did not match the checked-in baseline.
"use strict";

const fs = require("node:fs");
const path = require("node:path");

const SRC_FOLDER = "/.src";
const BUILT_FOLDER = "/.ts";
const TEST_LIB_FOLDER = "/.lib";
const HARNESS_NEW_LINE = "\r\n";

const FN_OFFSET = 14695981039346656037n;
const FN_PRIME = 1099511628211n;

let ts;

// ----------------------------------------------------------------------------------------------------------------
// Harness utilities (src/harness/util.ts, src/harness/harnessUtils.ts).
// ----------------------------------------------------------------------------------------------------------------

const testPathPrefixRegExp = /(?:(file:\/{3})|\/)\.(?:ts|lib|src)\//g;
function removeTestPathPrefixes(text, retainTrailingDirectorySeparator) {
    return text !== undefined
        ? text.replace(testPathPrefixRegExp, (_, scheme) => scheme || (retainTrailingDirectorySeparator ? "/" : ""))
        : text;
}

function splitContentByNewlines(content) {
    return content.split(/\r?\n/);
}

function getByteOrderMarkLength(text) {
    if (text.length >= 1) {
        const ch0 = text.charCodeAt(0);
        if (ch0 === 0xfeff) return 1;
        if (ch0 === 0xfe) return text.length >= 2 && text.charCodeAt(1) === 0xff ? 2 : 0;
        if (ch0 === 0xff) return text.length >= 2 && text.charCodeAt(1) === 0xfe ? 2 : 0;
        if (ch0 === 0xef) return text.length >= 3 && text.charCodeAt(1) === 0xbb && text.charCodeAt(2) === 0xbf ? 3 : 0;
    }
    return 0;
}

function removeByteOrderMark(text) {
    const length = getByteOrderMarkLength(text);
    return length ? text.slice(length) : text;
}

function addUTF8ByteOrderMark(text) {
    return getByteOrderMarkLength(text) === 0 ? "\u00EF\u00BB\u00BF" + text : text;
}

// Baseline.writeComparison compares encodeString(expected) with encodeString(actual); encodeString is the UTF-8
// round-trip, so a lone surrogate in either text is replaced by U+FFFD on both sides before they are compared.
function encodeString(s) {
    return Buffer.from(s).toString("utf8");
}

function sanitizeTestFilePath(name) {
    const replaced = ts.normalizeSlashes(name.replace(/[\^<>:"|?*%]/g, "_")).replace(/\.\.\//g, "__dotdot/");
    const p = ts.toPath(replaced, "", canonicalizeForHarness);
    return ts.startsWith(p, "/") ? p.substring(1) : p;
}

// src/harness/util.ts:canonicalizeForHarness — the harness canonicalizes to *lower case* on every host so a baseline
// name never depends on the file system's case sensitivity.
function canonicalizeForHarness(fileName) {
    return fileName.toLowerCase();
}

function checkDuplicatedFileName(resultName, dupeCase) {
    resultName = sanitizeTestFilePath(resultName);
    if (dupeCase.has(resultName)) {
        const count = 1 + dupeCase.get(resultName);
        dupeCase.set(resultName, count);
        resultName = `${resultName}.dupe${count}`;
    }
    else {
        dupeCase.set(resultName, 0);
    }
    return resultName;
}

function isDefaultLibraryFile(filePath) {
    const fileName = ts.getBaseFileName(ts.normalizeSlashes(filePath));
    return ts.startsWith(fileName, "lib.") && ts.endsWith(fileName, ts.Extension.Dts);
}

function getConfigNameFromFileName(filename) {
    const flc = ts.getBaseFileName(filename).toLowerCase();
    return ts.find(["tsconfig.json", "jsconfig.json"], x => x === flc);
}

// src/harness/vpathUtil.ts:26-136 — the extension predicates CompilationResult and the declaration re-compile classify
// the host's outputs with. `extname` is vpath's own (a suffix match against the given extensions).
function vpathExtname(path, extensions, ignoreCase) {
    for (const extension of extensions) {
        if (ts.fileExtensionIs(path, extension) || (ignoreCase && ts.fileExtensionIs(path.toLowerCase(), extension))) {
            return extension;
        }
    }
    return "";
}

const isTypeScriptFile = path => ts.hasTSFileExtension(path);
const isJavaScriptFile = path => ts.hasJSFileExtension(path);
const isDeclarationFile = path => ts.isDeclarationFileName(path);
const isSourceMapFile = path => vpathExtname(path, [".map"], false).length > 0;
const isJsonFile = path => vpathExtname(path, [".json"], false).length > 0;
function isTsConfigFilePath(path) {
    return path.includes("tsconfig") && path.includes("json");
}

// ----------------------------------------------------------------------------------------------------------------
// TestCaseParser (src/harness/harnessIO.ts:1205-1390).
// ----------------------------------------------------------------------------------------------------------------

const optionRegex = /^\/{2}\s*@(\w+)\s*:\s*([^\r\n]*)/gm;
const linkRegex = /^\/{2}\s*@link\s*:\s*([^\r\n]*)\s*->\s*([^\r\n]*)/gm;

function extractCompilerSettings(content) {
    const opts = {};
    optionRegex.lastIndex = 0;
    let match;
    while ((match = optionRegex.exec(content)) !== null) {
        opts[match[1]] = match[2].trim();
    }
    return opts;
}

function makeUnitsFromTest(code, fileName, settings) {
    if (settings === undefined) settings = extractCompilerSettings(code);
    const testUnitData = [];
    const lines = splitContentByNewlines(code);

    let currentFileContent;
    let currentFileOptions = {};
    let currentFileName;
    let refs = [];
    let symlinks;

    for (const line of lines) {
        let optionMatch;
        const possiblySymlinks = parseSymlinkFromTest(line, symlinks, SRC_FOLDER);
        if (possiblySymlinks) {
            symlinks = possiblySymlinks;
        }
        else if (optionRegex.lastIndex = 0, (optionMatch = optionRegex.exec(line)) !== null) {
            optionRegex.lastIndex = 0;
            const metaDataName = optionMatch[1].toLowerCase();
            currentFileOptions[optionMatch[1]] = optionMatch[2].trim();
            if (metaDataName !== "filename") {
                continue;
            }

            if (currentFileName) {
                testUnitData.push({
                    content: currentFileContent,
                    name: currentFileName,
                    fileOptions: currentFileOptions,
                    originalFilePath: fileName,
                    references: refs,
                });
                currentFileContent = undefined;
                currentFileOptions = {};
                currentFileName = optionMatch[2].trim();
                refs = [];
            }
            else {
                currentFileName = optionMatch[2].trim();
                if (currentFileContent && ts.skipTrivia(currentFileContent, 0, /*stopAfterLineBreak*/ false, /*stopAtComments*/ false) !== currentFileContent.length) {
                    throw new Error("Non-comment test content appears before the first '// @Filename' directive");
                }
                currentFileContent = "";
            }
        }
        else {
            if (currentFileContent === undefined) {
                currentFileContent = "";
            }
            else if (currentFileContent !== "") {
                currentFileContent = currentFileContent + "\n";
            }
            currentFileContent = currentFileContent + line;
        }
    }

    currentFileName = testUnitData.length > 0 || currentFileName ? currentFileName : ts.getBaseFileName(fileName);

    testUnitData.push({
        content: currentFileContent || "",
        name: currentFileName,
        fileOptions: currentFileOptions,
        originalFilePath: fileName,
        references: refs,
    });

    const parseConfigHost = {
        useCaseSensitiveFileNames: false,
        readDirectory: (directory, extensions, excludes, includes, depth) =>
            ts.matchFiles(directory, extensions, excludes, includes, /*useCaseSensitiveFileNames*/ false, "", depth, dir => {
                const files = [];
                const directories = new Set();
                for (const unit of testUnitData) {
                    const unitFileName = ts.getNormalizedAbsolutePath(unit.name, SRC_FOLDER);
                    if (unitFileName.toLowerCase().startsWith(dir.toLowerCase())) {
                        let p = unitFileName.substring(dir.length);
                        if (p.startsWith("/")) p = p.substring(1);
                        if (p.includes("/")) directories.add(p.substring(0, p.indexOf("/")));
                        else files.push(p);
                    }
                }
                return { files, directories: ts.arrayFrom(directories) };
            }, ts.identity),
        fileExists: fileName => testUnitData.some(data => data.name.toLowerCase() === fileName.toLowerCase()),
        readFile: name => ts.forEach(testUnitData, data => data.name.toLowerCase() === name.toLowerCase() ? data.content : undefined),
    };

    let tsConfig;
    let tsConfigFileUnitData;
    for (let i = 0; i < testUnitData.length; i++) {
        const data = testUnitData[i];
        if (getConfigNameFromFileName(data.name)) {
            const configJson = ts.parseJsonText(data.name, data.content);
            const configFileName = ts.getNormalizedAbsolutePath(data.name, SRC_FOLDER);
            const configDir = ts.getDirectoryPath(configFileName);
            tsConfig = ts.parseJsonSourceFileConfigFileContent(configJson, parseConfigHost, configDir, undefined, configFileName);
            tsConfigFileUnitData = data;
            ts.orderedRemoveItemAt(testUnitData, i);
            break;
        }
    }
    return { settings, testUnitData, tsConfig, tsConfigFileUnitData, symlinks };
}

function parseSymlinkFromTest(line, symlinks, absoluteRootDir) {
    linkRegex.lastIndex = 0;
    const linkMetaData = linkRegex.exec(line);
    linkRegex.lastIndex = 0;
    if (!linkMetaData) return undefined;
    if (!symlinks) symlinks = {};
    symlinks[ts.getNormalizedAbsolutePath(linkMetaData[2].trim(), absoluteRootDir)] = { symlink: ts.getNormalizedAbsolutePath(linkMetaData[1].trim(), absoluteRootDir) };
    return symlinks;
}

// ----------------------------------------------------------------------------------------------------------------
// The option model (src/harness/harnessIO.ts:255-420): the harness's own extra option declarations, then the
// reference's, and the value conversion Compiler.compileFiles performs before it builds a program.
// ----------------------------------------------------------------------------------------------------------------

const harnessOptionDeclarations = [
    { name: "allowNonTsExtensions", type: "boolean", defaultValueDescription: false },
    { name: "useCaseSensitiveFileNames", type: "boolean", defaultValueDescription: false },
    { name: "baselineFile", type: "string" },
    { name: "fileName", type: "string" },
    { name: "noErrorTruncation", type: "boolean", defaultValueDescription: false },
    { name: "suppressOutputPathCheck", type: "boolean", defaultValueDescription: false },
    { name: "noImplicitReferences", type: "boolean", defaultValueDescription: false },
    { name: "currentDirectory", type: "string" },
    { name: "symlink", type: "string" },
    { name: "link", type: "string" },
    { name: "noTypesAndSymbols", type: "boolean", defaultValueDescription: false },
    { name: "fullEmitPaths", type: "boolean", defaultValueDescription: false },
    { name: "noCheck", type: "boolean", defaultValueDescription: false },
    { name: "reportDiagnostics", type: "boolean", defaultValueDescription: false },
    { name: "captureSuggestions", type: "boolean", defaultValueDescription: false },
];

let optionsIndex;
function getCommandLineOption(name) {
    if (!optionsIndex) {
        optionsIndex = new Map();
        for (const option of harnessOptionDeclarations.concat(ts.optionDeclarations)) {
            optionsIndex.set(option.name.toLowerCase(), option);
        }
    }
    return optionsIndex.get(name.toLowerCase());
}

function optionValue(option, value, errors) {
    switch (option.type) {
        case "boolean":
            return value.toLowerCase() === "true";
        case "string":
            return value;
        case "number": {
            const numberValue = parseInt(value, 10);
            if (isNaN(numberValue)) throw new Error(`Value must be a number, got: ${JSON.stringify(value)}`);
            return numberValue;
        }
        case "list":
        case "listOrElement":
            return ts.parseListTypeOption(option, value, errors);
        default:
            return ts.parseCustomTypeOption(option, value, errors);
    }
}

function setCompilerOptionsFromHarnessSetting(settings, options) {
    for (const name in settings) {
        if (Object.prototype.hasOwnProperty.call(settings, name)) {
            const value = settings[name];
            if (value === undefined) throw new Error(`Cannot have undefined value for compiler option '${name}'.`);
            if (name === "typeScriptVersion") continue;
            const option = getCommandLineOption(name);
            if (option) {
                const errors = [];
                options[option.name] = optionValue(option, value, errors);
                if (errors.length > 0) throw new Error(`Unknown value '${value}' for compiler option '${name}'.`);
            }
            else {
                throw new Error(`Unknown compiler option '${name}'.`);
            }
        }
    }
}

// ----------------------------------------------------------------------------------------------------------------
// The harness host (src/harness/fakesHosts.ts CompilerHost over the VFS createFromFileSystem builds).
// ----------------------------------------------------------------------------------------------------------------

function normalizeSlash(p) {
    return ts.normalizeSlashes(p);
}

/**
 * The virtual file system Compiler.compileFiles builds: `/.src` holds the test's documents (created with cwd `/.src`,
 * so a relative unit name lands under it, while an absolute one — `/sub/foo.ts` — does not), `/.ts` mounts the
 * reference's own `lib/` (the harness mounts `built/local`, which the build copies from the same sources) and `/.lib`
 * mounts `tests/lib`. Case sensitivity follows `options.useCaseSensitiveFileNames`, which the harness defaults to true.
 */
function createVfs(options, documents, workspaceRoot, libDir, testLibDir) {
    const ignoreCase = !(options.useCaseSensitiveFileNames !== undefined ? options.useCaseSensitiveFileNames : true);
    const cwd = SRC_FOLDER;
    const canon = p => ignoreCase ? p.toLowerCase() : p;
    const files = new Map(); // canonical absolute path -> text
    const resolveVirtual = p => {
        p = normalizeSlash(p);
        if (!ts.isRootedDiskPath(p)) p = ts.combinePaths(cwd, p);
        return ts.getNormalizedAbsolutePath(p, cwd);
    };
    for (const document of documents) {
        files.set(canon(resolveVirtual(document.file)), document.text);
    }

    // The mounts: only the two filesystems the harness maps in are reachable, so a read outside them is a miss.
    const mounts = [
        [BUILT_FOLDER, libDir],
        [TEST_LIB_FOLDER, testLibDir],
    ];
    const readMounted = absolute => {
        const physical = physicalPathOf(absolute);
        if (physical === undefined) return undefined;
        try {
            return fs.readFileSync(physical, "utf8");
        }
        catch {
            return undefined;
        }
    };

    // The physical path a `/.ts` or `/.lib` path maps to, or undefined for a purely virtual path.
    function physicalPathOf(absolute) {
        for (const [prefix, dir] of mounts) {
            if (absolute === prefix || ts.startsWith(absolute, prefix + "/")) {
                const rest = absolute.substring(prefix.length).replace(/^\//, "");
                return path.join(dir, ...rest.split("/").filter(Boolean));
            }
        }
        return undefined;
    }

    const readFile = p => {
        const absolute = resolveVirtual(p);
        const key = canon(absolute);
        if (files.has(key)) return files.get(key);
        return readMounted(absolute);
    };

    const directoryExists = p => {
        const absolute = resolveVirtual(p);
        if (absolute === "/") return true;
        const physical = physicalPathOf(absolute);
        if (physical !== undefined) {
            try {
                return fs.statSync(physical).isDirectory();
            }
            catch {
                return false;
            }
        }
        // A virtual directory is one a document was written under (`fs.mkdirpSync(dirname(document.file))`); `/.src`
        // exists because `compileFiles` changes the virtual cwd into it.
        if (files.has(canon(absolute))) return false;
        for (const key of files.keys()) {
            if (ts.startsWith(key, canon(absolute) === "/" ? "/" : canon(absolute) + "/")) return true;
        }
        return false;
    };

    const getDirectories = p => {
        const absolute = resolveVirtual(p);
        const result = new Set();
        const physical = physicalPathOf(absolute);
        if (physical !== undefined) {
            try {
                for (const entry of fs.readdirSync(physical, { withFileTypes: true })) {
                    if (entry.isDirectory()) result.add(entry.name);
                }
            }
            catch { /* the mount is absent */ }
        }
        for (const prefix of mounts.map(m => m[0])) {
            if (ts.startsWith(prefix, absolute + "/")) result.add(prefix.substring(absolute.length + 1).split("/")[0]);
        }
        for (const key of files.keys()) {
            if (ts.startsWith(key, absolute + "/")) {
                const rest = key.substring(absolute.length + 1);
                const sep = rest.indexOf("/");
                if (sep > 0) result.add(rest.substring(0, sep));
            }
        }
        return ts.arrayFrom(result);
    };

    const writeFile = (p, text) => {
        files.set(canon(resolveVirtual(p)), text);
    };

    const readDirectory = (dirPath, extensions, excludes, includes, depth) => {
        const absolute = resolveVirtual(dirPath);
        return ts.matchFiles(
            absolute,
            extensions,
            excludes,
            includes,
            !ignoreCase,
            "",
            depth,
            dir => {
                const result = { files: [], directories: [] };
                // The physical children of a directory that maps into a mount (`/.ts`, `/.lib`).
                const physical = physicalPathOf(dir);
                if (physical !== undefined) {
                    try {
                        for (const entry of fs.readdirSync(physical, { withFileTypes: true })) {
                            if (entry.isDirectory()) result.directories.push(entry.name);
                            else result.files.push(entry.name);
                        }
                    }
                    catch { /* the mount is absent */ }
                }
                const prefix = canon(dir) === "/" ? "/" : canon(dir) + "/";
                for (const key of files.keys()) {
                    if (ts.startsWith(key, prefix)) {
                        const rest = key.substring(prefix.length);
                        const sep = rest.indexOf("/");
                        if (sep > 0) result.directories.push(rest.substring(0, sep));
                        else result.files.push(rest);
                    }
                }
                const directories = [];
                for (const d of result.directories) if (!directories.includes(d)) directories.push(d);
                return { files: result.files, directories };
            },
            ts.identity,
        );
    };

    return {
        cwd,
        readFile,
        writeFile,
        fileExists: p => readFile(p) !== undefined,
        directoryExists,
        getDirectories,
        readDirectory,
        realpath: p => resolveVirtual(p),
        stringComparer: ignoreCase
            ? (a, b) => ts.compareStringsCaseInsensitive(a, b)
            : (a, b) => ts.compareStringsCaseSensitive(a, b),
        useCaseSensitiveFileNames: !ignoreCase,
    };
}

class CompilerHost {
    constructor(vfs, options, workspaceRoot, libDir) {
        this.vfs = vfs;
        this.outputs = [];
        this.traces = [];
        this.defaultLibLocation = BUILT_FOLDER;
        this._outputsMap = new Map();
        this._sourceFiles = new Map();
        this._setParentNodes = true; // Harness.lightMode is false in a normal run, so shouldAssertInvariants is true
        this._newLine = ts.getNewLineCharacter(options);
        this.libDir = libDir;
        this.workspaceRoot = workspaceRoot;
        this._jsDocParsingMode = ts.JSDocParsingMode.ParseForTypeErrors;
    }

    getCurrentDirectory() { return this.vfs.cwd; }
    useCaseSensitiveFileNames() { return this.vfs.useCaseSensitiveFileNames; }
    getNewLine() { return this._newLine; }
    getCanonicalFileName(fileName) { return this.vfs.useCaseSensitiveFileNames ? fileName : fileName.toLowerCase(); }
    fileExists(fileName) { return this.vfs.fileExists(fileName); }
    directoryExists(directoryName) { return this.vfs.directoryExists(directoryName); }
    readFile(p) { return this.vfs.readFile(p); }
    readDirectory(p, extensions, excludes, includes, depth) { return this.vfs.readDirectory(p, extensions, excludes, includes, depth); }
    getDirectories(p) { return this.vfs.getDirectories(p); }
    realpath(p) { return this.vfs.realpath(p); }
    trace(s) { this.traces.push(s); }

    writeFile(fileName, content, writeByteOrderMark) {
        if (writeByteOrderMark) content = addUTF8ByteOrderMark(content);
        this.vfs.writeFile(fileName, content);
        const document = { file: fileName, text: content, meta: new Map([["fileName", fileName]]) };
        if (!this._outputsMap.has(document.file)) {
            this._outputsMap.set(document.file, this.outputs.length);
            this.outputs.push(document);
        }
        this.outputs[this._outputsMap.get(document.file)] = document;
    }

    getDefaultLibLocation() {
        return ts.resolvePath(this.getCurrentDirectory(), this.defaultLibLocation);
    }

    getDefaultLibFileName(options) {
        return ts.resolvePath(this.getDefaultLibLocation(), ts.getDefaultLibFileName(options));
    }

    getSourceFile(fileName, languageVersionOrOptions) {
        const canonicalFileName = this.getCanonicalFileName(ts.resolvePath(this.getCurrentDirectory(), fileName));
        const existing = this._sourceFiles.get(canonicalFileName);
        if (existing) return existing;
        const content = this.readFile(canonicalFileName);
        if (content === undefined) return undefined;
        const options = typeof languageVersionOrOptions === "object"
            ? { ...languageVersionOrOptions, jsDocParsingMode: this._jsDocParsingMode }
            : { languageVersion: languageVersionOrOptions, jsDocParsingMode: this._jsDocParsingMode };
        const parsed = ts.createSourceFile(fileName, content, options, this._setParentNodes);
        this._sourceFiles.set(canonicalFileName, parsed);
        return parsed;
    }
}

// ----------------------------------------------------------------------------------------------------------------
// Compiler.compileFiles (src/harness/harnessIO.ts:333-380) + src/harness/compilerImpl.ts:compileFiles.
// ----------------------------------------------------------------------------------------------------------------

function compileFiles(inputFiles, otherFiles, harnessSettings, compilerOptions, currentDirectory, ctx) {
    const originalCurrentDirectory = currentDirectory;
    const options = compilerOptions ? ts.cloneCompilerOptions(compilerOptions) : { noResolve: false };
    options.newLine = options.newLine || ts.NewLineKind.CarriageReturnLineFeed;
    options.noErrorTruncation = true;
    options.skipDefaultLibCheck = typeof options.skipDefaultLibCheck === "undefined" ? true : options.skipDefaultLibCheck;

    if (typeof currentDirectory === "undefined") currentDirectory = SRC_FOLDER;

    if (harnessSettings) {
        setCompilerOptionsFromHarnessSetting(harnessSettings, options);
    }

    const programFileNames = inputFiles
        .map(file => options.configFile ? ts.getNormalizedAbsolutePath(file.unitName, currentDirectory) : file.unitName)
        .filter(fileName => !ts.fileExtensionIs(fileName, ts.Extension.Json));

    const documents = inputFiles.concat(otherFiles).map(f => ({ file: f.unitName, text: f.content }));
    const vfs = createVfs(options, documents, ctx.workspaceRoot, ctx.libDir, ctx.testLibDir);
    if (ctx.symlinks && Object.keys(ctx.symlinks).length > 0) {
        // The harness applies the `// @link:` map with `fs.apply(symlinks)` and reads through the resolver; the
        // conformance corpus declares none, so a corpus file that does is reported rather than half-modelled.
        throw new UnmodelledError(`symlinks declared (${Object.keys(ctx.symlinks).join(", ")})`);
    }

    ts.assign(options, ts.convertToOptionsWithAbsolutePaths(options, p => ts.getNormalizedAbsolutePath(p, currentDirectory)));
    const host = new CompilerHost(vfs, options, ctx.workspaceRoot, ctx.libDir);
    const result = compileFilesWithHost(host, programFileNames, options, ctx);
    result.originalCurrentDirectory = originalCurrentDirectory;
    result.repeat = newOptions => compileFiles(inputFiles, otherFiles, { ...harnessSettings, ...newOptions }, compilerOptions, originalCurrentDirectory, ctx);
    return result;
}

function compileFilesWithHost(host, rootFiles, compilerOptions, ctx) {
    // src/harness/compilerImpl.ts:compileFiles — the project branch is not reachable for a corpus test without a
    // tsconfig unit, and the pre/post-emit error comparison is part of the answer (it can *add* diagnostics).
    // establish defaults (aligns with old harness)
    if (compilerOptions.newLine === undefined) compilerOptions.newLine = ts.NewLineKind.CarriageReturnLineFeed;
    if (compilerOptions.skipDefaultLibCheck === undefined) compilerOptions.skipDefaultLibCheck = true;
    if (compilerOptions.noErrorTruncation === undefined) compilerOptions.noErrorTruncation = true;

    const skipErrorComparison = (rootFiles ? rootFiles.length : 0) >= 100 || (!!compilerOptions.skipLibCheck && !!compilerOptions.declaration);
    const preProgram = !skipErrorComparison
        ? ts.createProgram({ rootNames: rootFiles || [], options: { ...compilerOptions, configFile: compilerOptions.configFile, traceResolution: false }, host })
        : undefined;
    let preErrors = preProgram && ts.getPreEmitDiagnostics(preProgram);

    const program = ts.createProgram({ rootNames: rootFiles || [], options: compilerOptions, host });
    const emitResult = program.emit();
    let postErrors = ts.getPreEmitDiagnostics(program);
    const longerErrors = preErrors && preErrors.length > postErrors.length ? preErrors : postErrors;
    const shorterErrors = longerErrors === preErrors ? postErrors : preErrors;
    const errors = preErrors && (preErrors.length !== postErrors.length)
        ? [
            ...shorterErrors,
            ts.addRelatedInfo(
                ts.createCompilerDiagnostic({
                    category: ts.DiagnosticCategory.Error,
                    code: -1,
                    key: "-1",
                    message: `Pre-emit (${preErrors.length}) and post-emit (${postErrors.length}) diagnostic counts do not match! This can indicate that a semantic _error_ was added by the emit resolver - such an error may not be reflected on the command line or in the editor, but may be captured in a baseline here!`,
                }),
                ts.createCompilerDiagnostic({
                    category: ts.DiagnosticCategory.Error,
                    code: -1,
                    key: "-1",
                    message: `The excess diagnostics are:`,
                }),
                ...ts.filter(longerErrors, p => !ts.some(shorterErrors, p2 => ts.compareDiagnostics(p, p2) === ts.Comparison.EqualTo)),
            ),
        ]
        : postErrors;

    return new CompilationResult(host, compilerOptions, program, emitResult, errors);
}

class CompilationResult {
    constructor(host, options, program, result, diagnostics) {
        this.host = host;
        this.program = program;
        this.result = result;
        this.diagnostics = diagnostics;
        this.options = program ? program.getCompilerOptions() : options;
        this._inputs = [];
        // src/harness/compilerImpl.ts:CompilationResult — the harness's own classification, in the insertion order the
        // host accepted the writes: a `.json` output counts as a js output (a `resolveJsonModule` copy), a `.d.ts` one
        // as a declaration, a `.map` one as a source map, and anything else as JavaScript.
        const js = this.js = new Map();
        const dts = this.dts = new Map();
        const maps = this.maps = new Map();
        for (const document of this.host.outputs) {
            if (isJavaScriptFile(document.file) || ts.fileExtensionIs(document.file, ts.Extension.Json)) js.set(document.file, document);
            else if (isDeclarationFile(document.file)) dts.set(document.file, document);
            else if (isSourceMapFile(document.file)) maps.set(document.file, document);
        }
    }

    get vfs() { return this.host.vfs; }
    get inputs() { return this._inputs; }
    get outputs() { return this.host.outputs; }
    get traces() { return this.host.traces; }
    get emitSkipped() { return (this.result && this.result.emitSkipped) || false; }
    get singleFile() { return !!this.options.outFile; }
    get commonSourceDirectory() {
        const common = (this.program && this.program.getCommonSourceDirectory()) || "";
        return common && ts.combinePaths(this.vfs.cwd, common);
    }

    getOutputPath(p, ext) {
        if (this.options.outFile) {
            p = ts.resolvePath(this.vfs.cwd, this.options.outFile);
        }
        else {
            p = ts.resolvePath(this.vfs.cwd, p);
            const outDir = (ext === ".d.ts" || ext === ".d.mts" || ext === ".d.cts" || ext.endsWith(".ts") || ext.includes(".d."))
                ? this.options.declarationDir || this.options.outDir
                : this.options.outDir;
            if (outDir) {
                const common = this.commonSourceDirectory;
                if (common) {
                    p = ts.getRelativePathFromDirectory(common, p, this.vfs.useCaseSensitiveFileNames);
                    p = ts.combinePaths(ts.resolvePath(this.vfs.cwd, this.options.outDir), p);
                }
            }
        }
        return ts.changeAnyExtension(p, ext, "", true);
    }

    getNumberOfJsFiles(includeJson) {
        if (includeJson) return this.js.size;
        let count = this.js.size;
        this.js.forEach(document => {
            if (ts.fileExtensionIs(document.file, ts.Extension.Json)) count--;
        });
        return count;
    }
}

// ----------------------------------------------------------------------------------------------------------------
// The error baseline (src/harness/harnessIO.ts:494-660). Transcribed: `pretty` is off unless a test asked for it, so
// only the plain `formatDiagnostics` path is reachable for the corpus (one `@pretty: true` file exists).
// ----------------------------------------------------------------------------------------------------------------

function minimalDiagnosticsToString(diagnostics, pretty) {
    const host = {
        getCanonicalFileName: ts.createGetCanonicalFileName(true),
        getCurrentDirectory: () => "",
        getNewLine: () => HARNESS_NEW_LINE,
    };
    return (pretty ? ts.formatDiagnosticsWithColorAndContext : ts.formatDiagnostics)(diagnostics, host);
}

function* iterateErrorBaseline(inputFiles, diagnostics, options) {
    diagnostics = ts.toSorted(diagnostics, ts.compareDiagnostics);
    let outputLines = "";
    let totalErrorsReportedInNonLibraryNonTsconfigFiles = 0;
    let errorsReported = 0;
    let firstLine = true;
    function newLine() {
        if (firstLine) {
            firstLine = false;
            return "";
        }
        return HARNESS_NEW_LINE;
    }

    const formatDiagnsoticHost = {
        getCurrentDirectory: () => options && options.currentDirectory ? options.currentDirectory : "",
        getNewLine: () => HARNESS_NEW_LINE,
        getCanonicalFileName: ts.createGetCanonicalFileName(options && options.caseSensitive !== undefined ? options.caseSensitive : true),
    };

    function outputErrorText(error) {
        const message = ts.flattenDiagnosticMessageText(error.messageText, HARNESS_NEW_LINE);
        const errLines = removeTestPathPrefixes(message)
            .split("\n")
            .map(s => s.length > 0 && s.charAt(s.length - 1) === "\r" ? s.substr(0, s.length - 1) : s)
            .filter(s => s.length > 0)
            .map(s => "!!! " + ts.diagnosticCategoryName(error) + " TS" + error.code + ": " + s);
        if (error.relatedInformation) {
            for (const info of error.relatedInformation) {
                let location = info.file ? " " + ts.formatLocation(info.file, info.start, formatDiagnsoticHost, ts.identity) : "";
                location = removeTestPathPrefixes(location);
                if (location && info.file && isDefaultLibraryFile(info.file.fileName)) {
                    location = location.replace(/(lib.*\.d\.ts):\d+:\d+/i, "$1:--:--");
                }
                errLines.push(`!!! related TS${info.code}${location}: ${ts.flattenDiagnosticMessageText(info.messageText, HARNESS_NEW_LINE)}`);
            }
        }
        errLines.forEach(e => outputLines += newLine() + e);
        errorsReported++;
        if (!error.file || (!isDefaultLibraryFile(error.file.fileName) && !getConfigNameFromFileName(error.file.fileName))) {
            totalErrorsReportedInNonLibraryNonTsconfigFiles++;
        }
    }

    let topDiagnostics = minimalDiagnosticsToString(diagnostics, options && options.pretty);
    topDiagnostics = removeTestPathPrefixes(topDiagnostics);
    topDiagnostics = topDiagnostics.replace(/^(lib.*\.d\.ts)\(\d+,\d+\)/gim, "$1(--,--)");
    yield ["__diagnosticSummary", topDiagnostics + HARNESS_NEW_LINE + HARNESS_NEW_LINE, diagnostics.length];

    const globalErrors = diagnostics.filter(err => !err.file);
    globalErrors.forEach(outputErrorText);
    yield ["__globalErrors", outputLines, errorsReported];
    outputLines = "";
    errorsReported = 0;

    const dupeCase = new Map();
    for (const inputFile of inputFiles.filter(f => f.content !== undefined)) {
        const fileErrors = diagnostics.filter(e => {
            const errFn = e.file;
            return !!errFn && ts.comparePaths(
                removeTestPathPrefixes(errFn.fileName),
                removeTestPathPrefixes(inputFile.unitName),
                (options && options.currentDirectory) || "",
                !(options && options.caseSensitive),
            ) === ts.Comparison.EqualTo;
        });

        outputLines += newLine() + "==== " + removeTestPathPrefixes(inputFile.unitName) + " (" + fileErrors.length + " errors) ====";

        let markedErrorCount = 0;
        const lineStarts = ts.computeLineStarts(inputFile.content);
        let lines = inputFile.content.split("\n");
        if (lines.length === 1) lines = lines[0].split("\r");

        lines.forEach((line, lineIndex) => {
            if (line.length > 0 && line.charAt(line.length - 1) === "\r") line = line.substr(0, line.length - 1);
            const thisLineStart = lineStarts[lineIndex];
            const nextLineStart = lineIndex === lines.length - 1 ? inputFile.content.length : lineStarts[lineIndex + 1];
            outputLines += newLine() + "    " + line;
            fileErrors.forEach(errDiagnostic => {
                const err = errDiagnostic;
                const end = ts.textSpanEnd(err);
                if (end >= thisLineStart && (err.start < nextLineStart || lineIndex === lines.length - 1)) {
                    const relativeOffset = err.start - thisLineStart;
                    const length = (end - err.start) - Math.max(0, thisLineStart - err.start);
                    const squiggleStart = Math.max(0, relativeOffset);
                    outputLines += newLine() + "    " + line.substr(0, squiggleStart).replace(/\S/g, " ") + new Array(Math.min(length, line.length - squiggleStart) + 1).join("~");
                    if (lineIndex === lines.length - 1 || nextLineStart > end) {
                        outputErrorText(errDiagnostic);
                        markedErrorCount++;
                    }
                }
            });
        });

        if (markedErrorCount !== fileErrors.length) {
            throw new Error(`count of errors in ${inputFile.unitName}: marked ${markedErrorCount}, expected ${fileErrors.length}`);
        }
        yield [checkDuplicatedFileName(inputFile.unitName, dupeCase), outputLines, errorsReported];
        outputLines = "";
        errorsReported = 0;
    }
}

function getErrorBaseline(inputFiles, diagnostics, pretty) {
    let outputLines = "";
    for (const [, content] of iterateErrorBaseline(inputFiles, diagnostics, { pretty })) {
        outputLines += content;
    }
    if (pretty) {
        outputLines += removeTestPathPrefixes(ts.getErrorSummaryText(ts.getErrorCountForSummary(diagnostics), ts.getFilesInErrorForSummary(diagnostics), HARNESS_NEW_LINE, { getCurrentDirectory: () => "" }));
    }
    return outputLines;
}

// ----------------------------------------------------------------------------------------------------------------
// The type/symbol baselines (src/harness/typeWriter.ts + harnessIO.ts:663-860).
// ----------------------------------------------------------------------------------------------------------------

function* forEachASTNode(node) {
    const work = [node];
    while (work.length) {
        const elem = work.pop();
        yield elem;
        const resChildren = [];
        ts.forEachChild(elem, c => { resChildren.unshift(c); });
        work.push(...resChildren);
    }
}

function nodeIsFullySynthetic(node) {
    return ts.nodeIsSynthesized(node) && !ts.getParseTreeNode(node);
}

let syntheticNodeUnderliningPrinter;
function createSyntheticNodeUnderliningPrinter() {
    if (syntheticNodeUnderliningPrinter) {
        syntheticNodeUnderliningPrinter.reset();
        return syntheticNodeUnderliningPrinter;
    }
    let underlining = false;
    const printer = ts.createPrinter({ removeComments: true }, {
        onEmitNode: (hint, node, cb) => {
            if (nodeIsFullySynthetic(node) !== underlining) {
                underlining = !underlining;
                const result = cb(hint, node);
                underlining = !underlining;
                return result;
            }
            return cb(hint, node);
        },
    });
    const baseWriter = ts.createTextWriter("");
    const underliner = ts.createTextWriter("");
    const underlineFor = s => s.length === 0 ? s : (underlining ? "^" : " ").repeat(s.length);
    const writer = {
        write: s => { baseWriter.write(s); underliner.write(underlineFor(s)); },
        writeTrailingSemicolon: t => { baseWriter.writeTrailingSemicolon(t); underliner.writeTrailingSemicolon(underlineFor(t)); },
        writeComment: t => { baseWriter.writeComment(t); underliner.writeComment(underlineFor(t)); },
        getText: () => baseWriter.getText(),
        rawWrite: s => { baseWriter.rawWrite(s); underliner.rawWrite(underlineFor(s)); },
        writeLiteral: s => { baseWriter.writeLiteral(s); underliner.writeLiteral(underlineFor(s)); },
        getTextPos: () => baseWriter.getTextPos(),
        getLine: () => baseWriter.getLine(),
        getColumn: () => baseWriter.getColumn(),
        getIndent: () => baseWriter.getIndent(),
        isAtStartOfLine: () => baseWriter.isAtStartOfLine(),
        hasTrailingComment: () => baseWriter.hasTrailingComment(),
        hasTrailingWhitespace: () => baseWriter.hasTrailingWhitespace(),
        writeKeyword: t => { baseWriter.writeKeyword(t); underliner.writeKeyword(underlineFor(t)); },
        writeOperator: t => { baseWriter.writeOperator(t); underliner.writeOperator(underlineFor(t)); },
        writePunctuation: t => { baseWriter.writePunctuation(t); underliner.writePunctuation(underlineFor(t)); },
        writeSpace: t => { baseWriter.writeSpace(t); underliner.writeSpace(underlineFor(t)); },
        writeStringLiteral: t => { baseWriter.writeStringLiteral(t); underliner.writeStringLiteral(underlineFor(t)); },
        writeParameter: t => { baseWriter.writeParameter(t); underliner.writeParameter(underlineFor(t)); },
        writeProperty: t => { baseWriter.writeProperty(t); underliner.writeProperty(underlineFor(t)); },
        writeSymbol: (t, symbol) => { baseWriter.writeSymbol(t, symbol); underliner.writeSymbol(underlineFor(t), symbol); },
        writeLine: force => { baseWriter.writeLine(force); underliner.writeLine(force); },
        increaseIndent: () => { baseWriter.increaseIndent(); underliner.increaseIndent(); },
        decreaseIndent: () => { baseWriter.decreaseIndent(); underliner.decreaseIndent(); },
        clear: () => { baseWriter.clear(); underliner.clear(); },
    };
    syntheticNodeUnderliningPrinter = {
        printer,
        writer,
        underliner,
        reset() { underlining = false; baseWriter.clear(); underliner.clear(); },
    };
    return syntheticNodeUnderliningPrinter;
}

class TypeWriterWalker {
    constructor(program, hadErrorBaseline) {
        this.program = program;
        this.hadErrorBaseline = hadErrorBaseline;
        this.checker = program.getTypeChecker();
    }

    *getSymbols(fileName) {
        const sourceFile = this.program.getSourceFile(fileName);
        this.currentSourceFile = sourceFile;
        yield* this.visitNode(sourceFile, /*isSymbolWalk*/ true);
    }

    *getTypes(fileName) {
        const sourceFile = this.program.getSourceFile(fileName);
        this.currentSourceFile = sourceFile;
        yield* this.visitNode(sourceFile, /*isSymbolWalk*/ false);
    }

    *visitNode(node, isSymbolWalk) {
        for (const n of forEachASTNode(node)) {
            if (ts.isExpressionNode(n) || n.kind === ts.SyntaxKind.Identifier || ts.isDeclarationName(n)) {
                const result = this.writeTypeOrSymbol(n, isSymbolWalk);
                if (result) yield result;
            }
        }
    }

    isImportStatementName(node) {
        if (ts.isImportSpecifier(node.parent) && (node.parent.name === node || node.parent.propertyName === node)) return true;
        if (ts.isImportClause(node.parent) && node.parent.name === node) return true;
        if (ts.isImportEqualsDeclaration(node.parent) && node.parent.name === node) return true;
        return false;
    }

    isExportStatementName(node) {
        if (ts.isExportAssignment(node.parent) && node.parent.expression === node) return true;
        if (ts.isExportSpecifier(node.parent) && (node.parent.name === node || node.parent.propertyName === node)) return true;
        return false;
    }

    isIntrinsicJsxTag(node) {
        const p = node.parent;
        if (!(ts.isJsxOpeningElement(p) || ts.isJsxClosingElement(p) || ts.isJsxSelfClosingElement(p))) return false;
        if (p.tagName !== node) return false;
        return ts.isIntrinsicJsxName(node.getText());
    }

    writeTypeOrSymbol(node, isSymbolWalk) {
        const actualPos = ts.skipTrivia(this.currentSourceFile.text, node.pos);
        const lineAndCharacter = this.currentSourceFile.getLineAndCharacterOfPosition(actualPos);
        const sourceText = ts.getSourceTextOfNodeFromSourceFile(this.currentSourceFile, node);

        if (!isSymbolWalk) {
            if (ts.isPartOfTypeNode(node) || (ts.isIdentifier(node) && !(ts.getMeaningFromDeclaration(node.parent) & ts.SemanticMeaning.Value) && !(ts.isTypeAliasDeclaration(node.parent) && node.parent.name === node))) {
                return undefined;
            }
            if (ts.isOmittedExpression(node)) return undefined;

            let type = ts.isExpressionWithTypeArgumentsInClassExtendsClause(node.parent) ? this.checker.getTypeAtLocation(node.parent) : undefined;
            if (!type || (type.flags & ts.TypeFlags.Any)) type = this.checker.getTypeAtLocation(node);
            let typeString;
            let underline;
            if (
                !this.hadErrorBaseline &&
                (type.flags & ts.TypeFlags.Any) &&
                !ts.isBindingElement(node.parent) &&
                !ts.isPropertyAccessOrQualifiedName(node.parent) &&
                !ts.isLabelName(node) &&
                !(ts.isModuleDeclaration(node.parent) && ts.isGlobalScopeAugmentation(node.parent)) &&
                !ts.isMetaProperty(node.parent) &&
                !this.isImportStatementName(node) &&
                !this.isExportStatementName(node) &&
                !this.isIntrinsicJsxTag(node)
            ) {
                typeString = type.intrinsicName;
            }
            else {
                const typeFormatFlags = ts.TypeFormatFlags.NoTruncation | ts.TypeFormatFlags.AllowUniqueESSymbolType | ts.TypeFormatFlags.GenerateNamesForShadowedTypeParams;
                let typeNode = this.checker.typeToTypeNode(type, node.parent, (typeFormatFlags & ts.TypeFormatFlags.NodeBuilderFlagsMask) | ts.NodeBuilderFlags.IgnoreErrors, ts.InternalNodeBuilderFlags.AllowUnresolvedNames);
                if (ts.isIdentifier(node) && ts.isTypeAliasDeclaration(node.parent) && node.parent.name === node && ts.isIdentifier(typeNode) && ts.idText(typeNode) === ts.idText(node)) {
                    typeNode = this.checker.typeToTypeNode(type, node.parent, ((typeFormatFlags | ts.TypeFormatFlags.InTypeAlias) & ts.TypeFormatFlags.NodeBuilderFlagsMask) | ts.NodeBuilderFlags.IgnoreErrors);
                }
                const { printer, writer, underliner, reset } = createSyntheticNodeUnderliningPrinter();
                printer.writeNode(ts.EmitHint.Unspecified, typeNode, this.currentSourceFile, writer);
                typeString = writer.getText();
                underline = underliner.getText();
                reset();
            }
            return { line: lineAndCharacter.line, syntaxKind: node.kind, sourceText, type: typeString, underline };
        }

        const symbol = this.checker.getSymbolAtLocation(node);
        if (!symbol) return undefined;
        // src/harness/typeWriter.ts:303-340 — the symbol string is the checker's rendering, then every declaration
        // the symbol has (at most five, then a count of the rest), each as `Decl(file, line, character)` with a
        // declaration in a `lib.*.d.ts` reporting `--` for both coordinates.
        let symbolString = "Symbol(" + this.checker.symbolToString(symbol, node.parent);
        if (symbol.declarations) {
            let count = 0;
            for (const declaration of symbol.declarations) {
                if (count >= 5) {
                    symbolString += ` ... and ${symbol.declarations.length - count} more`;
                    break;
                }
                count++;
                symbolString += ", ";
                if (declaration.__symbolTestOutputCache) {
                    symbolString += declaration.__symbolTestOutputCache;
                    continue;
                }
                const declSourceFile = declaration.getSourceFile();
                const declLineAndCharacter = declSourceFile.getLineAndCharacterOfPosition(declaration.pos);
                const fileName = ts.getBaseFileName(declSourceFile.fileName);
                const isLibFile = /lib.*\.d\.ts/i.test(fileName);
                const declText = `Decl(${fileName}, ${isLibFile ? "--" : declLineAndCharacter.line}, ${isLibFile ? "--" : declLineAndCharacter.character})`;
                symbolString += declText;
                declaration.__symbolTestOutputCache = declText;
            }
        }
        symbolString += ")";
        return { line: lineAndCharacter.line, syntaxKind: node.kind, sourceText, symbol: symbolString };
    }
}

// src/harness/harnessIO.ts:754-830 — the `.types` baseline's optional performance block. The counters are the
// program's own checker (not process-global), read before and after the types walk, and only the entries whose *post*
// value reaches the harness's threshold are printed: cumulatively, so a pre value already at the rounded value prints
// without the `->`.
function getPerformanceBaselineValues(program) {
    const checker = program.getTypeChecker();
    const caches = checker.getRelationCacheSizes();
    return {
        strictSubtype: caches.strictSubtype,
        subtype: caches.subtype,
        identity: caches.identity,
        assignability: caches.assignable,
        typeCount: checker.getTypeCount(),
        instantiation: checker.getInstantiationCount(),
        symbol: checker.getSymbolCount(),
    };
}

function roundToHumanLogarithm(n) {
    if (n < 10) return 0;
    const powerOfTen = Math.floor(Math.log10(n));
    const basePowerOfTen = Math.pow(10, powerOfTen);
    const multipliers = [1, 2.5, 5, 10];
    const closestMultiplier = multipliers.reduce((prev, curr) => Math.abs(curr * basePowerOfTen - n) < Math.abs(prev * basePowerOfTen - n) ? curr : prev);
    return closestMultiplier * basePowerOfTen;
}

function perfValueToString(value) {
    return roundToHumanLogarithm(value).toLocaleString("en-US");
}

function generateTypeAndSymbolBaseline(header, program, allFiles, hasErrorBaseline) {
    const fullWalker = new TypeWriterWalker(program, !!hasErrorBaseline);
    const checkBaseLines = isSymbolBaseLine => {
        const fullExtension = isSymbolBaseLine ? ".symbols" : ".types";
        const result = generateBaseLine(isSymbolBaseLine);
        return { extension: fullExtension, text: result };
    };
    const generateBaseLine = isSymbolBaseline => {
        let result = "";
        const perfLines = [];
        const prePerformanceValues = getPerformanceBaselineValues(program);
        for (const [, content] of iterateBaseLine(isSymbolBaseline)) {
            result += content;
        }
        const postPerformanceValues = getPerformanceBaselineValues(program);

        if (!isSymbolBaseline) {
            const perfStats = [
                ["Strict subtype cache", 1000, prePerformanceValues.strictSubtype, postPerformanceValues.strictSubtype],
                ["Subtype cache", 1000, prePerformanceValues.subtype, postPerformanceValues.subtype],
                ["Identity cache", 1000, prePerformanceValues.identity, postPerformanceValues.identity],
                ["Assignability cache", 1000, prePerformanceValues.assignability, postPerformanceValues.assignability],
                ["Type Count", 1000, prePerformanceValues.typeCount, postPerformanceValues.typeCount],
                ["Instantiation count", 1500, prePerformanceValues.instantiation, postPerformanceValues.instantiation],
                ["Symbol count", 45000, prePerformanceValues.symbol, postPerformanceValues.symbol],
            ];
            if (perfStats.some(([, threshold, , postValue]) => postValue >= threshold)) {
                perfLines.push("=== Performance Stats ===");
                for (const [name, threshold, preValue, postValue] of perfStats) {
                    if (postValue >= threshold) {
                        const preString = perfValueToString(preValue);
                        const postString = perfValueToString(postValue);
                        perfLines.push(preString === postString ? `${name}: ${preString}` : `${name}: ${preString} -> ${postString}`);
                    }
                }
                perfLines.push("");
                perfLines.push("");
            }
        }

        return result ? (`//// [${header}] ////\r\n\r\n${perfLines.join("\n")}${result}`) : null;
    };
    function* iterateBaseLine(isSymbolBaseline) {
        const dupeCase = new Map();
        for (const file of allFiles) {
            const { unitName } = file;
            let typeLines = "=== " + unitName + " ===\r\n";
            const codeLines = ts.flatMap(file.content.split(/\r?\n/), e => e.split(/[\r\u2028\u2029]/));
            const gen = isSymbolBaseline ? fullWalker.getSymbols(unitName) : fullWalker.getTypes(unitName);
            let lastIndexWritten;
            for (const result of gen) {
                if (isSymbolBaseline && !result.symbol) return;
                if (lastIndexWritten === undefined) {
                    typeLines += codeLines.slice(0, result.line + 1).join("\r\n") + "\r\n";
                }
                else if (result.line !== lastIndexWritten) {
                    if (!((lastIndexWritten + 1 < codeLines.length) && (codeLines[lastIndexWritten + 1].match(/^\s*[{|}]\s*$/) || codeLines[lastIndexWritten + 1].trim() === ""))) {
                        typeLines += "\r\n";
                    }
                    typeLines += codeLines.slice(lastIndexWritten + 1, result.line + 1).join("\r\n") + "\r\n";
                }
                lastIndexWritten = result.line;
                const typeOrSymbolString = isSymbolBaseline ? result.symbol : result.type;
                const lineText = result.sourceText.replace(/\r?\n/g, "");
                const formattedLine = lineText + " : " + typeOrSymbolString;
                typeLines += ">" + formattedLine + "\r\n";
                if (result.underline) {
                    typeLines += ">" + " ".repeat(lineText.length) + " : " + result.underline + "\r\n";
                }
            }
            if (lastIndexWritten === undefined) lastIndexWritten = -1;
            if (lastIndexWritten + 1 < codeLines.length) {
                if (!((lastIndexWritten + 1 < codeLines.length) && (codeLines[lastIndexWritten + 1].match(/^\s*[{|}]\s*$/) || codeLines[lastIndexWritten + 1].trim() === ""))) {
                    typeLines += "\r\n";
                }
                typeLines += codeLines.slice(lastIndexWritten + 1).join("\r\n");
            }
            typeLines += "\r\n";
            yield [checkDuplicatedFileName(unitName, dupeCase), removeTestPathPrefixes(typeLines)];
        }
    }
    return { types: checkBaseLines(false), symbols: checkBaseLines(true) };
}

// ----------------------------------------------------------------------------------------------------------------
// doJsEmitBaseline (src/harness/harnessIO.ts:910-1000).
// ----------------------------------------------------------------------------------------------------------------

function prepareDeclarationCompilationContext(inputFiles, otherFiles, result, harnessSettings, options, currentDirectory, ctx) {
    if (options.declaration && result.diagnostics.length === 0) {
        if (options.emitDeclarationOnly) {
            if (result.js.size > 0 || (result.dts.size === 0 && !options.noEmit)) {
                throw new Error("Only declaration files should be generated when emitDeclarationOnly:true");
            }
        }
        else if (result.dts.size !== result.getNumberOfJsFiles(false)) {
            throw new Error("There were no errors and declFiles generated did not match number of js files generated");
        }
    }

    const declInputFiles = [];
    const declOtherFiles = [];
    if (options.declaration && result.diagnostics.length === 0 && result.dts.size > 0) {
        for (const file of inputFiles) addDtsFile(file, declInputFiles);
        for (const file of otherFiles) addDtsFile(file, declOtherFiles);
        return { declInputFiles, declOtherFiles, harnessSettings, options, currentDirectory: currentDirectory || harnessSettings.currentDirectory };
    }
    return undefined;

    function addDtsFile(file, dtsFiles) {
        if (isDeclarationFile(file.unitName) || isJsonFile(file.unitName)) {
            dtsFiles.push(file);
        }
        else if (isTypeScriptFile(file.unitName) || (isJavaScriptFile(file.unitName) && result.options.allowJs)) {
            const declFile = findResultCodeFile(file.unitName);
            if (declFile && !findUnit(declFile.file, declInputFiles) && !findUnit(declFile.file, declOtherFiles)) {
                dtsFiles.push({ unitName: declFile.file, content: removeByteOrderMark(declFile.text) });
            }
        }
    }

    function findResultCodeFile(fileName) {
        const sourceFile = result.program.getSourceFile(fileName);
        let sourceFileName;
        const outFile = options.outFile;
        if (!outFile) {
            if (options.outDir) {
                let sourceFilePath = ts.getNormalizedAbsolutePath(sourceFile.fileName, result.vfs.cwd);
                sourceFilePath = sourceFilePath.replace(result.program.getCommonSourceDirectory(), "");
                sourceFileName = ts.combinePaths(options.outDir, sourceFilePath);
            }
            else {
                sourceFileName = sourceFile.fileName;
            }
        }
        else {
            sourceFileName = outFile;
        }
        const dTsFileName = ts.removeFileExtension(sourceFileName) + ts.getDeclarationEmitExtensionForPath(sourceFileName);
        return result.dts.get(dTsFileName);
    }

    function findUnit(fileName, units) {
        return ts.forEach(units, unit => unit.unitName === fileName ? unit : undefined);
    }
}

function compileDeclarationFiles(context, ctx) {
    if (!context) return undefined;
    const { declInputFiles, declOtherFiles, harnessSettings, options, currentDirectory } = context;
    const output = compileFiles(declInputFiles, declOtherFiles, harnessSettings, options, currentDirectory, ctx);
    return { declInputFiles, declOtherFiles, declResult: output };
}

function fileOutput(file, harnessSettings) {
    const fileName = harnessSettings.fullEmitPaths ? removeTestPathPrefixes(file.file) : ts.getBaseFileName(file.file);
    return "//// [" + fileName + "]\r\n" + file.text;
}

function doJsEmitBaseline(baselinePath, header, options, result, tsConfigFiles, toBeCompiled, otherFiles, harnessSettings, ctx) {
    if (!options.noEmit && !options.emitDeclarationOnly && result.js.size === 0 && result.diagnostics.length === 0) {
        throw new Error("Expected at least one js file to be emitted or at least one error to be created.");
    }

    let tsCode = "";
    const tsSources = otherFiles.concat(toBeCompiled);
    tsCode += "//// [" + header + "] ////\r\n\r\n";
    for (let i = 0; i < tsSources.length; i++) {
        tsCode += "//// [" + ts.getBaseFileName(tsSources[i].unitName) + "]\r\n";
        tsCode += tsSources[i].content + (i < tsSources.length - 1 ? "\r\n" : "");
    }

    let jsCode = "";
    result.js.forEach(file => {
        if (jsCode.length && jsCode.charCodeAt(jsCode.length - 1) !== ts.CharacterCodes.lineFeed) {
            jsCode += "\r\n";
        }
        if (!result.diagnostics.length && !ts.endsWith(file.file, ts.Extension.Json)) {
            const fileParseResult = ts.createSourceFile(file.file, file.text, ts.getEmitScriptTarget(options), /*setParentNodes*/ false, ts.endsWith(file.file, "x") ? ts.ScriptKind.JSX : ts.ScriptKind.JS);
            if (ts.length(fileParseResult.parseDiagnostics)) {
                jsCode += getErrorBaseline([{ unitName: file.file, content: file.text }], fileParseResult.parseDiagnostics);
                return;
            }
        }
        jsCode += fileOutput(file, harnessSettings);
    });

    if (result.dts.size > 0) {
        jsCode += "\r\n\r\n";
        result.dts.forEach(declFile => {
            jsCode += fileOutput(declFile, harnessSettings);
        });
    }

    const declFileContext = prepareDeclarationCompilationContext(toBeCompiled, otherFiles, result, harnessSettings, options, /*currentDirectory*/ undefined, ctx);
    const declFileCompilationResult = compileDeclarationFiles(declFileContext, ctx);

    if (declFileCompilationResult && declFileCompilationResult.declResult.diagnostics.length) {
        jsCode += "\r\n\r\n//// [DtsFileErrors]\r\n";
        jsCode += "\r\n\r\n";
        jsCode += getErrorBaseline(tsConfigFiles.concat(declFileCompilationResult.declInputFiles, declFileCompilationResult.declOtherFiles), declFileCompilationResult.declResult.diagnostics);
    }
    else if (!options.noCheck && !options.noEmit) {
        const withoutChecking = result.repeat({ noCheck: "true" });
        compareResultFileSets(withoutChecking.dts, result.dts);
        compareResultFileSets(withoutChecking.js, result.js);
    }

    return jsCode.length > 0 ? tsCode + "\r\n\r\n" + jsCode : null;

    function compareResultFileSets(a, b) {
        a.forEach((doc, key) => {
            const original = b.get(key);
            if (!original) {
                jsCode += `\r\n\r\n!!!! File ${removeTestPathPrefixes(doc.file)} missing from original emit, but present in noCheck emit\r\n`;
                jsCode += fileOutput(doc, harnessSettings);
            }
            else if (original.text !== doc.text) {
                jsCode += `\r\n\r\n!!!! File ${removeTestPathPrefixes(doc.file)} differs from original emit in noCheck emit\r\n`;
                jsCode += "//// [" + (harnessSettings.fullEmitPaths ? removeTestPathPrefixes(doc.file) : ts.getBaseFileName(doc.file)) + "]\r\n";
                jsCode += diffText(original.text, doc.text);
            }
        });
    }
}

// `diff`'s createTwoFilesPatch, as the harness uses it: a unified patch with those two headers. The reference keeps
// the package's exact output, so this is a transcription of diff 4.x's header/body shape.
function diffText(expected, actual) {
    const Diff = require("diff");
    return Diff.createTwoFilesPatch("Expected", "Actual", expected, actual, "The full check baseline", "with noCheck set");
}

// ----------------------------------------------------------------------------------------------------------------
// Configurations (src/testRunner/compilerRunner.ts:129-350 + harnessIO.ts:960-1140).
// ----------------------------------------------------------------------------------------------------------------

function computeVaryBy() {
    return [
        // implicit variations from defined options
        ...ts.optionDeclarations
            .filter(option => !option.isCommandLineOnly
                && (option.type === "boolean" || typeof option.type === "object")
                && (option.affectsProgramStructure
                    || option.affectsEmit
                    || option.affectsModuleResolution
                    || option.affectsBindDiagnostics
                    || option.affectsSemanticDiagnostics
                    || option.affectsSourceFile
                    || option.affectsDeclarationPath
                    || option.affectsBuildInfo))
            .map(option => option.name),
        // explicit variations that do not match above conditions
        "noEmit",
        "isolatedModules",
    ];
}

function getVaryByStarSettingValues(varyBy) {
    const option = ts.forEach(ts.optionDeclarations, decl => ts.equateStringsCaseInsensitive(decl.name, varyBy) ? decl : undefined);
    if (option) {
        if (typeof option.type === "object") return option.type;
        if (option.type === "boolean") return new Map(Object.entries({ true: 1, false: 0 }));
    }
    return undefined;
}

function splitVaryBySettingValue(text, varyBy) {
    if (!text) return undefined;
    let star = false;
    const includes = [];
    const excludes = [];
    for (let s of text.split(/,/)) {
        s = s.trim().toLowerCase();
        if (s.length === 0) continue;
        if (s === "*") star = true;
        else if (ts.startsWith(s, "-") || ts.startsWith(s, "!")) excludes.push(s.slice(1));
        else includes.push(s);
    }
    if (includes.length <= 1 && !star && excludes.length === 0) return undefined;

    const variations = [];
    const values = getVaryByStarSettingValues(varyBy);
    const add = (key, value) => {
        if (ts.findIndex(variations, v => v.key === key || (value !== undefined && v.value === value)) === -1) {
            variations.push({ key, value });
        }
    };
    for (const include of includes) add(include, values ? values.get(include) : undefined);
    if (star && values) for (const [key, value] of ts.arrayFrom(values.entries())) add(key, value);
    for (const exclude of excludes) {
        const value = values ? values.get(exclude) : undefined;
        let index;
        while ((index = ts.findIndex(variations, v => v.key === exclude || (value !== undefined && v.value === value))) >= 0) {
            ts.orderedRemoveItemAt(variations, index);
        }
    }
    if (variations.length === 0) throw new Error(`Variations in test option '@${varyBy}' resulted in an empty set.`);
    return ts.map(variations, v => v.key);
}

function getFileBasedTestConfigurations(settings, varyBy) {
    let varyByEntries;
    let variationCount = 1;
    for (const varyByKey of varyBy) {
        if (Object.prototype.hasOwnProperty.call(settings, varyByKey)) {
            const entries = splitVaryBySettingValue(settings[varyByKey], varyByKey);
            if (entries) {
                if (!varyByEntries) varyByEntries = [];
                variationCount *= entries.length;
                if (variationCount > 25) throw new Error(`Provided test options exceeded the maximum number of variations`);
                varyByEntries.push([varyByKey, entries]);
            }
        }
    }
    if (!varyByEntries) return undefined;
    const configurations = [];
    const compute = (variationState, offset) => {
        if (offset >= varyByEntries.length) {
            configurations.push({ ...variationState });
            return;
        }
        const [varyByKey, entries] = varyByEntries[offset];
        for (const entry of entries) {
            variationState[varyByKey] = entry;
            compute(variationState, offset + 1);
        }
    };
    compute({}, 0);
    return configurations;
}

// CompilerTest's constructor: the configured name is the base name with the sorted configuration entries appended.
function configuredName(justName, configurationOverrides) {
    let name = justName;
    if (configurationOverrides) {
        let configured = "";
        for (const key of Object.keys(configurationOverrides).sort()) {
            if (configured) configured += ",";
            configured += `${key.toLowerCase()}=${configurationOverrides[key].toLowerCase()}`;
        }
        if (configured) {
            const extname = path.posix.extname(justName);
            const basename = justName.substring(0, justName.length - extname.length);
            name = `${basename}(${configured})${extname}`;
        }
    }
    return name;
}

// The unit assembly of CompilerTest's constructor; every unit becomes the harness's own `{unitName, content,
// fileOptions}` record (`createHarnessTestFile`), which is what the compiler and the baselines both read.
function assembleUnits(testCaseContent) {
    const units = testCaseContent.testUnitData;
    const asTestFile = unit => ({ unitName: unit.name, content: unit.content, fileOptions: unit.fileOptions });
    const toBeCompiled = [];
    const otherFiles = [];
    const hasNonDtsFiles = units.some(unit => !ts.fileExtensionIs(unit.name, ts.Extension.Dts));
    if (testCaseContent.tsConfig) {
        for (const unit of units) {
            if (testCaseContent.tsConfig.fileNames.includes(ts.getNormalizedAbsolutePath(unit.name, SRC_FOLDER))) toBeCompiled.push(asTestFile(unit));
            else otherFiles.push(asTestFile(unit));
        }
        return { toBeCompiled, otherFiles, hasNonDtsFiles, tsConfigFiles: [asTestFile(testCaseContent.tsConfigFileUnitData)] };
    }
    const baseUrl = testCaseContent.settings.baseUrl;
    if (baseUrl !== undefined && !ts.isRootedDiskPath(baseUrl)) {
        testCaseContent.settings.baseUrl = ts.getNormalizedAbsolutePath(baseUrl, SRC_FOLDER);
    }
    const lastUnit = units[units.length - 1];
    if (testCaseContent.settings.noImplicitReferences || /require\(/.test(lastUnit.content) || /reference\spath/.test(lastUnit.content)) {
        toBeCompiled.push(asTestFile(lastUnit));
        for (const unit of units) {
            if (unit.name !== lastUnit.name) otherFiles.push(asTestFile(unit));
        }
    }
    else {
        for (const unit of units) toBeCompiled.push(asTestFile(unit));
    }
    return { toBeCompiled, otherFiles, hasNonDtsFiles, tsConfigFiles: [] };
}

// ----------------------------------------------------------------------------------------------------------------
// One corpus file → its configurations → the baseline texts the harness would check in.
// ----------------------------------------------------------------------------------------------------------------

const NO_CONTENT = "<no content>";

const ARTIFACT_KINDS = ["js", "errors", "types", "symbols"];

function artifactName(stem, kind) {
    switch (kind) {
        case "js": return stem.replace(/\.tsx?/, ".js");
        case "errors": return stem.replace(/\.tsx?$/, ".errors.txt");
        case "types": return (ts.endsWith(stem, ".ts") || ts.endsWith(stem, ".tsx") ? stem.replace(/\.tsx?/, "") : stem) + ".types";
        case "symbols": return (ts.endsWith(stem, ".ts") || ts.endsWith(stem, ".tsx") ? stem.replace(/\.tsx?/, "") : stem) + ".symbols";
        default: throw new Error(`unknown artifact kind '${kind}'`);
    }
}

/**
 * One corpus file → its harness-derived cases. `CompilerTest.getConfigurations` extracts the settings from the whole
 * file's text and computes the varying configurations; each case is those settings with one configuration's entries
 * merged over them, exactly as the runner's constructor does.
 */
function deriveCases(rel, content) {
    const settings = extractCompilerSettings(content);
    const testCaseContent = makeUnitsFromTest(content, rel, settings);
    const configurations = getFileBasedTestConfigurations(settings, computeVaryBy());
    const list = configurations && configurations.length ? configurations : [undefined];
    return list.map(configuration => ({
        configuration,
        settings: { ...settings, ...(configuration || {}) },
        testCaseContent,
    }));
}

/** The stem every one of a case's baselines is named after: the file's base name with `(key=value,…)` appended. */
function caseStem(rel, configuration) {
    return configuredName(path.posix.basename(rel), configuration);
}

/**
 * Runs one case and returns the harness's baseline texts — one entry per artifact kind, where `undefined` is "the
 * harness does not check this baseline for this case at all" and `null` is "the generator returned null", which the
 * baseline machinery compares as the literal `<no content>`.
 */
function runCase(ctx, rel, caseInfo) {
    const { settings, testCaseContent, configuration } = caseInfo;
    const units = assembleUnits(testCaseContent);
    const tsConfigOptions = testCaseContent.tsConfig ? ts.cloneCompilerOptions(testCaseContent.tsConfig.options) : undefined;
    if (tsConfigOptions && tsConfigOptions.configFilePath !== undefined) {
        tsConfigOptions.configFile.fileName = tsConfigOptions.configFilePath;
    }
    const result = compileFiles(
        units.toBeCompiled,
        units.otherFiles,
        settings,
        tsConfigOptions,
        settings.currentDirectory,
        ctx,
    );
    const options = result.options;
    if (process.env.BASELINEDIFF_TRACE_CASE && process.env.BASELINEDIFF_TRACE_CASE === caseStem(rel, configuration)) {
        console.error(`[trace] ${caseStem(rel, configuration)} options=${JSON.stringify(options)}`);
        console.error(`[trace] roots=${JSON.stringify(units.toBeCompiled.map(u => u.unitName))} other=${JSON.stringify(units.otherFiles.map(u => u.unitName))}`);
        console.error(`[trace] sourceFiles=${JSON.stringify(result.program.getSourceFiles().map(f => f.fileName))}`);
        console.error(`[trace] diagnostics=${result.diagnostics.length} ${JSON.stringify(result.diagnostics.slice(0, 5).map(d => d.code))}`);
    }
    const out = { js: undefined, errors: undefined, types: undefined, symbols: undefined };

    // verifyDiagnostics — doErrorBaseline is handed null (compared as `<no content>`) when there is nothing to report,
    // so a test with no diagnostics has no `.errors.txt` baseline at all.
    out.errors = !result.diagnostics || result.diagnostics.length === 0
        ? null
        : getErrorBaseline(units.tsConfigFiles.concat(units.toBeCompiled, units.otherFiles), result.diagnostics, !!options.pretty);

    // verifyJavaScriptOutput
    if (units.hasNonDtsFiles) {
        out.js = doJsEmitBaseline(caseStem(rel, configuration), ctx.relativeCasePath(rel), options, result, units.tsConfigFiles, units.toBeCompiled, units.otherFiles, settings, ctx);
    }

    // verifyTypesAndSymbols (the runner skips both baselines when the test says so)
    const noTypesAndSymbols = settings.noTypesAndSymbols && settings.noTypesAndSymbols.toLowerCase() === "true";
    if (!noTypesAndSymbols) {
        const allFiles = units.toBeCompiled.concat(units.otherFiles).filter(file => !!result.program.getSourceFile(file.unitName));
        const generated = generateTypeAndSymbolBaseline(ctx.relativeCasePath(rel), result.program, allFiles, !!ts.length(result.diagnostics));
        out.types = generated.types.text;
        out.symbols = generated.symbols.text;
    }

    return out;
}

// ----------------------------------------------------------------------------------------------------------------
// The driver: manifest in, per-(case, artifact) hashes and verdicts out.
// ----------------------------------------------------------------------------------------------------------------

class UnmodelledError extends Error {
    constructor(message) {
        super(message);
        this.name = "UnmodelledError";
    }
}

function fnv1a(text) {
    let hash = FN_OFFSET;
    for (let i = 0; i < text.length; i++) {
        hash ^= BigInt(text.charCodeAt(i));
        hash = (hash * FN_PRIME) & 0xFFFFFFFFFFFFFFFFn;
    }
    return hash.toString(16).padStart(16, "0");
}

function readTextStrippingBom(file) {
    // ts.sys.readFile strips a UTF-8 BOM before the text ever reaches the parser or the baseline comparison, so both
    // the corpus file and the checked-in baseline are read the same way here.
    const text = removeByteOrderMark(fs.readFileSync(file, "utf8"));
    return text;
}

function parseArgs(argv) {
    const args = { limit: undefined, dumps: undefined, hashes: undefined, cases: undefined, errors: undefined, summary: undefined };
    for (let i = 0; i < argv.length; i++) {
        switch (argv[i]) {
            case "--manifest": args.manifest = argv[++i]; break;
            case "--out": args.out = argv[++i]; break;
            case "--js": args.js = argv[++i]; break;
            case "--baselines": args.baselines = argv[++i]; break;
            case "--lib": args.lib = argv[++i]; break;
            case "--test-lib": args.testLib = argv[++i]; break;
            case "--hashes": args.hashes = argv[++i]; break;
            case "--cases": args.cases = argv[++i]; break;
            case "--errors": args.errors = argv[++i]; break;
            case "--summary": args.summary = argv[++i]; break;
            case "--dumps": args.dumps = argv[++i]; break;
            case "--limit": args.limit = parseInt(argv[++i], 10); break;
            default: throw new Error(`unknown argument '${argv[i]}'`);
        }
    }
    for (const required of ["manifest", "out", "js", "baselines"]) {
        if (!args[required]) throw new Error(`--${required} is required`);
    }
    args.hashes = args.hashes || path.join(args.out, "ts-hashes.txt");
    args.cases = args.cases || path.join(args.out, "ts-cases.jsonl");
    args.errors = args.errors || path.join(args.out, "ts-errors.txt");
    args.summary = args.summary || path.join(args.out, "ts-summary.txt");
    return args;
}

function main() {
    const args = parseArgs(process.argv.slice(2));
    ts = require(args.js);
    fs.mkdirSync(args.out, { recursive: true });
    const manifest = JSON.parse(fs.readFileSync(args.manifest, "utf8"));
    const workspaceRoot = manifest.workspaceRoot || path.dirname(path.dirname(args.baselines));
    const ctx = {
        workspaceRoot,
        libDir: args.lib || path.join(workspaceRoot, "lib"),
        testLibDir: args.testLib || path.join(workspaceRoot, "tests", "lib"),
        baselines: args.baselines,
        // The `.js`/`.types`/`.symbols` baselines open with the source file's path *relative to the workspace root*
        // (`tests/cases/conformance/…`), which is what CompilerBaselineRunner enumerates and hands to doJsEmitBaseline.
        relativeCasePath: rel => path.relative(workspaceRoot, path.join(manifest.root, rel.split("/").join(path.sep))).split(path.sep).join("/"),
    };
    let files = manifest.files;
    if (args.limit) files = files.slice(0, args.limit);

    const hashLines = [];
    const caseLines = [];
    const errorLines = [];
    let cases = 0, same = 0, diverged = 0, unmodelled = 0, extra = 0, missing = 0, errors = 0;
    const started = Date.now();

    for (const file of files) {
        const rel = file.path;
        const absolute = path.join(manifest.root, rel.split("/").join(path.sep));
        let content;
        try {
            content = readTextStrippingBom(absolute);
        }
        catch (ex) {
            errorLines.push(`${rel}\tread-error: ${ex.name}: ${firstLine(ex.message)}`);
            continue;
        }

        let derived;
        try {
            derived = deriveCases(rel, content);
        }
        catch (ex) {
            const stem = configuredName(path.posix.basename(rel), undefined);
            errorLines.push(`${stem}\tmodel-error: ${ex.name}: ${firstLine(ex.message)}`);
            for (const kind of ARTIFACT_KINDS) hashLines.push(`${stem}\t${kind}\terror\t\t`);
            continue;
        }

        for (const caseInfo of derived) {
            cases++;
            const stem = caseStem(rel, caseInfo.configuration);
            let produced;
            try {
                produced = runCase(ctx, rel, caseInfo);
            }
            catch (ex) {
                const state = ex instanceof UnmodelledError ? "unmodelled" : "error";
                if (process.env.BASELINEDIFF_TRACE) console.error(`[${stem}] ${ex.stack}`);
                errorLines.push(`${stem}\t${state}: ${ex.name}: ${firstLine(ex.message)}`);
                for (const kind of ARTIFACT_KINDS) hashLines.push(`${stem}\t${kind}\t${state}\t\t`);
                if (state === "unmodelled") unmodelled++;
                else errors++;
                continue;
            }

            for (const kind of ARTIFACT_KINDS) {
                const name = artifactName(stem, kind);
                const expectedFile = path.join(args.baselines, name);
                const hasBaseline = fs.existsSync(expectedFile);
                const expected = hasBaseline ? readTextStrippingBom(expectedFile) : NO_CONTENT;
                const text = produced[kind];

                if (text === undefined) {
                    // The harness does not check this baseline for this case (an all-`.d.ts` test has no `.js`
                    // baseline; `@noTypesAndSymbols` turns both type baselines off).
                    hashLines.push(`${stem}\t${kind}\tskipped\t\t`);
                    caseLines.push({ stem, file: rel, kind, state: "skipped", baseline: hasBaseline, verdict: hasBaseline ? "extra-baseline" : "same" });
                    if (hasBaseline) { extra++; errorLines.push(`${stem}\t${kind}\textra-baseline: not checked by the model but ${name} exists`); }
                    continue;
                }

                const actual = text === null ? NO_CONTENT : text;
                const hash = fnv1a(encodeString(actual));
                const length = actual.length;
                const verdict = encodeString(actual) === encodeString(expected) ? "same" : "diverge";
                hashLines.push(`${stem}\t${kind}\t${text === null ? "null" : "text"}\t${hash}\t${length}`);
                caseLines.push({ stem, file: rel, kind, state: text === null ? "null" : "text", baseline: hasBaseline, verdict, hash, length });
                if (verdict === "same") same++;
                else {
                    diverged++;
                    if (args.dumps) {
                        const dump = path.join(args.dumps, name);
                        fs.mkdirSync(path.dirname(dump), { recursive: true });
                        fs.writeFileSync(dump, actual, "utf8");
                    }
                }
            }
        }
    }

    fs.writeFileSync(args.hashes, hashLines.join("\n") + (hashLines.length ? "\n" : ""), "utf8");
    fs.writeFileSync(args.cases, caseLines.map(c => JSON.stringify(c)).join("\n") + (caseLines.length ? "\n" : ""), "utf8");
    fs.writeFileSync(args.errors, errorLines.join("\n") + (errorLines.length ? "\n" : ""), "utf8");
    const elapsed = ((Date.now() - started) / 1000).toFixed(2);
    const summary = `files=${files.length} cases=${cases} same=${same} diverged=${diverged} unmodelled=${unmodelled} errors=${errors} extra=${extra} seconds=${elapsed} rss=${Math.round(process.memoryUsage().rss / (1024 * 1024))}MiB`;
    fs.writeFileSync(args.summary, summary + "\n", "utf8");
    console.log(summary);
}

function firstLine(message) {
    if (!message) return "";
    const index = message.search(/[\r\n]/);
    const line = index < 0 ? message : message.slice(0, index);
    return line.length > 300 ? line.slice(0, 300) : line;
}

main();