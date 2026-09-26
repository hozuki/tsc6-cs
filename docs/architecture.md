# TypeScript 编译器 → C# 移植架构

> 参考源: `D:\Source\GitHub\TypeScript`，分支 `release-6.0`（版本 6.0.3）
> 目标: `tsc-cs`，.NET 11，C# 惯用风格重写（非逐行硬翻）

本文档是移植工作的路线图与设计契约。**任何模块开工前先读本文档对应章节；实现中若发现文档与源码冲突，以源码为准并回写本文档。**

---

## 1. 源规模（实测）

| 区域 | 文件数 | 行数 |
|---|---|---|
| `src/compiler/*.ts`（顶层） | 40 | 142,080 |
| `src/compiler/factory/` | 10 | 12,103 |
| `src/compiler/transformers/` | 21 | 25,083 |
| `src/compiler/transformers/declarations/` | 1 | 758 |
| `src/compiler/transformers/module/` | 4 | 4,655 |
| **合计** | **76** | **≈184,700** |

最大的单文件：`checker.ts` 50,507 行，`utilities.ts` 11,376 行，`parser.ts` 9,775 行，`types.ts` 9,535 行。

测试资源（验收用）：

- `tests/cases/conformance/` — 5,695 个 `.ts` 用例
- `tests/baselines/reference/` — 60,931 个 baseline 文件（`.js` / `.types` / `.symbols` / `.errors.txt` / `.symbols.txt` 等）
- `src/harness/` — 测试驱动框架（定义了 baseline 的生成格式）

---

## 2. 编译管线

经典 6 阶段，均为纯函数式变换，阶段间通过显式数据结构传递：

```
源文本 string
  │
  ▼
① Scanner      lexer/Tokenizer：字符流 → SyntaxKind token 流（含 trivia 前导/尾随处理）
  │            entry: createScanner()
  ▼
② Parser       token 流 → AST（SourceFile），产出语法诊断
  │            entry: createSourceFile() / parseSourceFile()
  ▼
③ Binder       AST → Symbol 表 + 容器作用域 + FlowNode 控制流图；填 parent
  │            entry: bindSourceFile()，绑定结果挂在 Node.symbol / LocalsContainer.locals
  ▼
④ Checker      语义分析：类型推导、类型检查、产生语义诊断；懒计算 + 缓存
  │            entry: createTypeChecker(host) → TypeChecker
  │            Program 协调 ①~④：createProgram() → Program
  ▼
⑤ Transformers AST → AST（语法降级 / 类型擦除 / 声明生成 / 模块格式改写）
  │            entry: transform(nodes, transformers) + transformers/*
  ▼
⑥ Emitter      AST → 文本（.js / .d.ts / .map），由 Printer 完成
  │            entry: emitFiles()、createPrinter()
  ▼
输出文件 + 诊断
```

**关键认知**：⑤ 之后 AST 才变成"纯 JS AST"。④ 与 ⑤ 之间通过 `EmitResolver`（emitter.ts 内）桥接——它是 checker 的窄接口视图。

### 各阶段粒度

| 阶段 | 输入 | 输出 | 是否可变 AST | 复杂度 |
|---|---|---|---|---|
| Scanner | `string`, `ScriptTarget` | `SyntaxKind` token（不回建字符串） | — | 中 |
| Parser | scanner 回调 | `SourceFile`（含 `NodeArray`） | 构造 | 高 |
| Binder | `SourceFile` | `Symbol` / `SymbolTable` / `FlowNode` | 原地写入 AST | 高 |
| Checker | `Program` + `TypeCheckerHost` | `Type` / `Signature` / `Diagnostic` | 写 `NodeLinks`/`SymbolLinks`/`TypeLinks` | 极高 |
| Transformers | `Node` | 新 `Node`（`NodeFactory` 构造） | 不可变式重建 | 高 |
| Emitter | `Node` | `string` chunk + sourcemap | 只读 | 高 |

---

## 3. 模块地图与依赖分层

移植**严格自底向上按层推进**：下层未完成且未通过测试前，不开始上层。

### Layer 0 — 基础设施（无编译语义）

| 源文件 | 行数 | 移植为 | 说明 |
|---|---|---|---|
| `core.ts` | 2,368 | `Core/` | 容器（Map/Set/MultiMap/Queue）、数组与字符串工具、比较器、`Pattern` |
| `debug.ts` | 1,139 | `Core/` | 断言、`Debug.assert*`、fail |
| `performanceCore.ts` | 86 | `Core/` | 计时钩子发现与时间戳；CLR 无 Web Performance API，`timestamp` 由 `Stopwatch` 承接 |
| `performance.ts` | 189 | `Core/` | mark/measure 埋点。**依赖 `sys.ts` 的 `System` 实例**，随 `sys.ts` 顺延到 **M2b** |
| `path.ts` | 1,024 | `Core/` | 路径规范化；C# 对照 `Path` 但**必须复刻 TS 语义**（大小写、`/` 分隔符） |
| `sys.ts` | 1,826 | `Core/` | `System` 接口：文件读写、目录、环境。**实现依赖上层**：`utilities.ts`（Layer 5）的 `matchFiles`/`resolveJSModule`/`writeFileEnsuringDirectories`、`watch.ts` 的 `closeFileWatcher`，以及 `types.ts` 的 `WatchOptions`/`PollingWatchKind`。顺延到 **M2b** |
| `semver.ts` | 397 | `Core/` | 版本比较 |
| `tracing.ts` | 375 | `Core/Tracing.cs` | ETW/trace 埋点。已落地（M5 前置：`binder.ts` 的三处调用点需要它）；主机由 `StartTracing` 传入，写入缓冲后交给 `ISystem.WriteFile` —— 见 §10.2 |

### Layer 1 — 数据模型（全局共享的类型与标志）

| 源文件 | 行数 | 说明 |
|---|---|---|
| `types.ts` | 9,535 | **全部** `SyntaxKind`、`Node` 及其派生接口、`Symbol`、`Type`、`Signature`、各种 `Flags` 位标志、`NodeArray`、`Diagnostic` 等 |

`types.ts` 是整个移植的**地基与最大翻译风险点**：它用 TS 的类型系统（条件类型、映射类型、字面量联合）表达"运行时约束"，C# 需要换成显式运行时结构（见 §4）。

### Layer 2 — 词法

`scanner.ts`（4,101）— `Scanner`（`createScanner`）、字符分类表、`textToKeywordObj` 关键字表、Unicode 标识符表。

### Layer 3 — 语法

| 源文件 | 行数 | 说明 |
|---|---|---|
| `parser.ts` | 10,823 | `Parser`（`createSourceFile`）：递归下降 + 错误恢复 + JSDoc 解析 |
| `factory/nodeFactory.ts` | 7,542 | `NodeFactory`：唯一的 AST 构造入口（缓存、`NodeArray` 复用、parent 处理） |
| `factory/utilities.ts` | 1,757 | factory 辅助（`update*` 例程的共享逻辑） |
| `factory/nodeTests.ts` | 1,201 | 节点判定谓词（取代 TS 的 `isXxx` 条件类型） |
| `factory/emitHelpers.ts` | 1,491 | 生成 `__awaiter`/`__spreadArray` 等 helper |
| `expressionToTypeNode.ts` | 1,330 | 表达式 → 类型节点（用于声明生成与快速修复） |
| `factory/parenthesizerRules.ts` | 706 | 括号规则 |
| `factory/emitNode.ts` | 381 | 发射期附加信息槽 |
| `factory/nodeConverters.ts` | 193 | 节点转换器 |
| `factory/nodeChildren.ts` | 61 | 子节点访问表（增量解析的 `WeakMap` 缓存） |
| `factory/baseNodeFactory.ts` | 60 | 构造函数间接层（见 §15.2：C# 侧省略） |
| `factory/utilitiesPublic.ts` | 52 | factory 的公开辅助 |

合计约 25,600 行 —— 是 Layer 2（4,101）的六倍，M4 因此按文件切成 M4a–M4g（见 §15.1）。

### Layer 4 — 绑定

`binder.ts`（3,630）— `Binder`：符号表、作用域链、控制流图构建。

### Layer 5 — 语义（含 Program 与模块解析）

| 源文件 | 行数 | 说明 |
|---|---|---|
| `checker.ts` | 50,507 | `TypeChecker`：类型系统、推导、检查、诊断。**移植量最大、风险最高** |
| `utilities.ts` | 11,376 | 编译期公共工具（节点访问、类型显示、`getEffective*` 等）——被 parser/checker/emitter 广泛依赖 |
| `program.ts` | 4,736 | `Program`：文件图、`CompilerHost` 调度、诊断聚合 |
| `programDiagnostics.ts` | 396 | 诊断辅助 |
| `moduleNameResolver.ts` | 3,159 | 模块解析（node16/nodenext/bundler 等策略） |
| `moduleSpecifiers.ts` | 1,379 | 生成模块说明符（用于自动导入） |
| `resolutionCache.ts` | 1,588 | 解析结果缓存 |
| `symbolWalker.ts` | 198 | 符号遍历（公共 API） |

### Layer 6 — 变换

`transformer.ts`（624，`transform` 骨架）；`transformers/`（25,083）：
`ts.ts`（类型擦除核心）、`es2015`–`esnext`（语法降级链）、`classFields`、`esDecorators`、`legacyDecorators`、`generators`、`destructuring`、`jsx`、`module/*`（模块格式）、`declarations.ts` + `declarations/diagnostics.ts`（`.d.ts` 生成）、`typeSerializer.ts`、`namedEvaluation.ts`、`classThis.ts`、`taggedTemplate.ts`。

### Layer 7 — 发射

`emitter.ts`（5,721，`Printer` / `EmitTextWriter` / `EmitResolver` / `emitFiles`）、`sourcemap.ts`（723）。

### Layer 8 — 驱动 / CLI / 增量

`commandLineParser.ts`（4,037，`tsc` 选项与 `tsconfig` 解析）、`executeCommandLine.ts`（1,197）、`watch.ts`/`watchPublic.ts`/`watchUtilities.ts`/`watchGuard`（文件监视）、`builder.ts`（2,340）+ `builderState.ts` + `builderPublic.ts`（`--build` 增量）、`tsbuild.ts` + `tsbuildPublic.ts`（2,267）。

### Layer 9 — 公共 API 汇聚

`utilitiesPublic.ts`（2,377）、`visitorPublic.ts`（1,638）、`corePublic.ts`、`builderStatePublic.ts`、`typescript/typescript.ts`（barrel）。

---

## 4. 核心数据模型与 C# 映射

### 4.1 `SyntaxKind`

- 源：`export const enum SyntaxKind`，**隐式递增**，约 360 个成员。
- C#：`public enum SyntaxKind : int`，**每个成员显式写出与 TS 相同的数值**。
- 理由：数值被 `NodeFlags`/`ModifierFlags` 之外的代码、调试输出和 baseline 间接依赖；显式赋值也防止将来插入成员时错位。**不得**改序、删项。
- 子集联合（`KeywordSyntaxKind`、`PunctuationSyntaxKind`、`TriviaSyntaxKind`、`TypeNodeSyntaxKind`、`ModifierSyntaxKind`、`LiteralSyntaxKind` …）→ `static readonly FrozenSet<SyntaxKind>` + `IsKeyword()` / `IsPunctuation()` 之类谓词，放在 `SyntaxKindExtensions`。既有集合是热路径，用 `switch` 或位集合优化。

### 4.2 节点（`Node`）

TS 的模型是**扁平接口 + 按需附加字段**：

```ts
interface Node extends ReadonlyTextRange {
    readonly kind: SyntaxKind;
    readonly flags: NodeFlags;
    readonly parent: Node;
    readonly transformFlags: TransformFlags;
    id?: NodeId;            // NodeLinks 查表键
    original?: Node;        // 变换后的原始节点
    emitNode?: EmitNode;    // 变换期附加
}
```

而具体节点（`Identifier extends PrimaryExpression, Declaration, JSDocContainer, FlowContainer` 等）通过**接口继承组合**表达"有哪些字段"，`symbol`/`locals`/`flowNode` 等状态分散在 `Declaration`/`LocalsContainer`/`FlowContainer` 中（见 types.ts:942-976 的注释）。

C# 方案（决定整个代码库形态）：

- `Node` 是**抽象基类**，持有公共槽位：`Kind`、`Flags`、`Parent`、`TransformFlags`、`Id`、`Original`、`EmitNode`、`Pos`、`End`。
- 每种节点一个**派生类**（`Identifier`、`CallExpression`…），字段为普通属性。
- `Declaration.symbol` 之类的"横向能力"用**接口**（`IDeclaration`、`ILocalsContainer`、`IFlowContainer`、`IJsDocContainer`）实现，节点类按需实现多个接口；判定用 `is Declaration d`。
- 不采用"单一 Node 类 + 属性字典"方案：会丢失类型安全且性能差。
- **注意**：节点在 C# 中是可变的（binder 原地写 `Symbol`、`Locals`、`FlowNode`），`readonly` 只表示"解析后不再写"，用普通 setter + 少量 `internal set` 表达。

### 4.3 `Symbol` / `Type` / `Signature`

同样是"接口 + 附加缓存槽"模式：`Symbol` 有 `links: SymbolLinks`、`Type` 有 `id`/`resolvedTypeArguments` 等。C# 直接映射为类 + 内部缓存字段；`SymbolLinks` 用**惰性分配**（`Symbol.Links` getter 里 new），复刻 TS 里 `getSymbolLinks` 的语义。

### 4.4 容器与 `NodeArray`

- TS 的 `Map`/`Set` 只保证**相等性**（`SameValueZero`），不保证 C# 引用相等以外的语义差异；C# 用 `Dictionary`/`HashSet` + 显式 `IEqualityComparer`。
- `core.ts` 的 `createMap`/`createMultiMap`/`createSet`/`createQueue` 对应包装类型，**不可**随意换成语义不同的 BCL 类型。
- `NodeArray<T> extends ReadonlyArray<T>, ReadonlyTextRange` → C# 是 `List<T>` 子类 `NodeArray<T>`，带 `Pos`/`End`/`HasTrailingComma` 等额外状态（这些字段 emitter 依赖）。
- `SymbolTable = Map<__String, Symbol>`；TS 为性能用**驻留字符串**（`__String`）。C# 用 `string` + 必要时 `InternedString` 包装。

### 4.5 位标志

`NodeFlags` / `ModifierFlags` / `SymbolFlags` / `TypeFlags` / `TransformFlags` / `SignatureFlags` … 全部 → `[Flags] enum : uint`（`TypeFlags` 可能超 32 位，需确认后选 `uint` 或 `ulong`）。`AwaitUsing = Const | Using` 这类组合成员在 C# 的 `[Flags]` 中直接支持。

### 4.6 字符串字面量联合

如 `ScriptKind`、`ModuleResolutionKind` 等有的已是 `const enum`（直接映射），有的（如 `EmitHint`、`LanguageVariant`）是数值枚举；真正的字符串联合（如 `ModuleKind` 的名称）用 `enum` + 名称映射表，或 `public const string` 常量类，按原语义选择。

---

## 5. TS ↔ C# 语言模型差异（移植规则）

**这是"用 C# 思维重写"的操作手册，逐条适用于所有模块。**

| # | TS 特性 | C# 对应 | 注意 |
|---|---|---|---|
| 1 | `undefined` 与 `null` 是两个值 | 统一用 `null`（引用/可空值类型 `T?`） | TS 里 `x === undefined` 与 `x === null` 语义不同、`??` 与 `\|\|` 不同。C# 里 `undefined`→`null`，`null` 字面量→`JSNull` 概念按需保留（如 JS 值字面量）|
| 2 | 结构化类型 | 名义类型（接口/基类） | 跨"形状相同但名义不同"的复用要靠显式接口 |
| 3 | 联合类型 + 控制流窄化 | `switch (kind)` + `is` 模式匹配 | 节点窄化统一走 `Kind` 判别；`Node` 层级设计要确保窄化不丢信息 |
| 4 | 条件类型 / 映射类型（`IsFunctionLike`、`HasLocals`、`ForEachChildNodes`） | **不可直接表达** → 运行时结构 | 类型级映射改为：接口标记（空接口 + 判定）、`static readonly` 表、或生成的 `switch` |
| 5 | 对象字面量即类型 | `record` / `class` / `readonly struct` | 纯数据（如 `TextSpan`、`DiagnosticRelatedInformation`）优先 `readonly record struct` |
| 6 | 闭包返回对象（模块级工厂函数） | `class` + 实例字段/方法，或 `static class` | TS 到处是 `export function createXxx(): Xxx { ... return { ... } }` → C# 是 `class Xxx` + 构造函数。**不**用 lambda 模拟闭包状态 |
| 7 | 可选参数 / 重载签名 | 默认参数 / 方法重载 | 保留重载语义 |
| 8 | `const enum` | `enum`（显式数值） | 见 §4.1 |
| 9 | 迭代协议 `Iterable`/`Iterator` | `IEnumerable<T>` | 注意 `IteratorResult.done` 的惰性语义 |
| 10 | `any` | 避免 `dynamic`；用 `object?` + 泛型/显式类型 | 类型断言处改用显式 cast 或 `Cast<T>()` 辅助 |
| 11 | 泛型约束 / `extends` 条件 | `where` 约束；复杂约束降级为运行时检查 | 遇到无法表达的约束时**显式注释**说明降级点 |
| 12 | 模块/命名空间合并 | `namespace` + `partial class` | 可按原文件边界切分 partial |
| 13 | 类型断言 `as T` / `!` | `(T)x` / 显式非空 | 保留断言语义，不引入静默转换 |
| 14 | 数值语义（32 位、位运算 `>>>`、`^`） | `uint`/`ulong` + `>>>`（C# 11 起支持无符号右移） | bit 运算密集处在 checker/emitter，必须与 JS 语义一致 |
| 15 | 字符串 UTF-16 码元 | C# `string` 也是 UTF-16 | **天然一致**，`charCodeAt` → `s[i]`；注意代理对处理 |
| 16 | 正则 | `System.Text.RegularExpressions` | JS 正则语义有差异（如 `\d`、lastIndex），**逐处核对** |
| 17 | 对象属性顺序 / `for...in` | `Dictionary` **不保证**插入序 | 任何依赖属性顺序的地方（如 emitter 的语义顺序）必须显式保序结构 |
| 18 | `Symbol.iterator` 自定义迭代 | `IEnumerable<T>` 实现 | — |

---

## 6. 解决方案结构

```
D:\Source\VS\TEMP\tsc-cs\
├─ tsc-cs.sln
├─ Directory.Build.props          # TFM=net11.0, LangVersion=preview, Nullable=enable, TreatWarningsAsErrors
├─ Directory.Packages.props       # 中央包版本管理
├─ global.json                    # 固定 SDK 11.0.100-rc
├─ docs\
│   └─ architecture.md            # 本文档
├─ src\
│   ├─ TypeScript.Compiler\       # 命名空间 TypeScript.Compiler
│   │   ├─ Core\                  # ← Layer 0（core/debug/path/sys/...）
│   │   ├─ Model\                 # ← Layer 1（SyntaxKind、Node 层级、Symbol、Type、Flags）
│   │   ├─ Lexer\                 # ← Layer 2
│   │   ├─ Parser\                # ← Layer 3
│   │   ├─ Binder\                # ← Layer 4
│   │   ├─ Checker\               # ← Layer 5
│   │   ├─ Program\               # ← Layer 5
│   │   ├─ Transforms\            # ← Layer 6
│   │   ├─ Emit\                  # ← Layer 7
│   │   └─ CommandLine\           # ← Layer 8
│   └─ tsc\                       # CLI 可执行（对应 src/tsc）
├─ tests\
│   ├─ TypeScript.Compiler.Tests\ # xUnit 单元测试（开发内环）
│   └─ TypeScript.Baselines\      # baseline 差分验收（外环）
└─ tools\
    ├─ gen\                       # 从 TS 源码生成/校验枚举与诊断表的脚本
    └─ AstDiff\                   # 解析差分外环（§15.11）：端口与真 lib/typescript.js 对同一语料逐字节比 AST
```

- 每个 C# 文件头部注释标注对应 TS 源文件与起止行，便于双向对照：
  `// ported from: src/compiler/scanner.ts:1-3823`
- 一个 TS 源文件通常对应一个 C# 文件（部分再按 partial 拆分），**但函数级实现允许按 C# 惯用法重排**。

---

## 7. 验证策略

### 内环：xUnit（开发时快速反馈）

- 每个模块实现后立刻写针对性单测。
- 断言工具：`Assert`/`FluentAssertions`（若引入需走中央包管理）。
- 覆盖：正常路径 + 边界（空输入、错误恢复）+ 与 TS 语义等价的边界（如 `undefined` 比较）。

### 外环：baseline 差分（验收 ground truth）

TS 官方 baseline 由 `src/harness` 生成。我们要复刻**输出格式**，然后对 `tests/cases/conformance/**/*.ts` 做：

1. 用 C# 编译器跑该用例（编译选项取自用例内的 `// @xxx` 指令注释）；
2. 生成对应 baseline 文本（`.errors.txt` / `.js` / `.types` / `.symbols`）；
3. 与 `tests/baselines/reference/` 同名文件逐字节比对。

分两阶段：

- **P1（语法层验收）**：`.errors.txt`（语法诊断）与 `.js`，不含类型检查。可在 Layer 3 完成后启动。
- **P2（语义层验收）**：加 `.types` / `.symbols`，依赖 checker。

P1 自身再分两半，因为两半的前置完全不同（M4e 收口时定的）：

- **P1a：解析差分（不需要任何新层，已启动）**。对 `tests/cases/**` 的每个文件，用移植版与真 `lib/typescript.js` 各打印一份规范化 AST（kind 数值、pos、end、NodeFlags、标识符/字面量文本 + 每条解析诊断的 code/start/length），逐字节比。它比 `.errors.txt` 在语法层上更严（后者只比诊断文本、且混有语义错误；P1a 比整棵树），且不需要发射器 —— M4e 的收官就是用这个办法在 6 个真实编译器源文件（含 `checker.ts`，29.8 万行 dump）与 `typescript.d.ts` 上做到的逐字节一致。
- **P1b：`.errors.txt` + `.js`**，需要声明解析（指令注释）、选项模型与**发射器/转换器**（Layer 4/5），前者在 M8 之后、后者更晚。

验收 harness 需要：
- 解析用例的 `// @target: es2015`、`// @strict: true` 等指令（复刻 harness 的 baseline 选项解析）；
- `baseline` 比较器（TS 的 `.types` 格式含 `===` 分隔与 `getTypeNameForErrorDisplay` 风格的类型字符串）。
- 报告：通过/失败用例数 + 差异文件路径；**不**在仓库内留临时输出（写入 `artifacts/`，已被 `.gitignore`）。

---

## 8. 里程碑

| 里程碑 | 内容 | 完成判据 |
|---|---|---|
| **M0** | 架构梳理（本文档）+ 解决方案骨架 | `dotnet build` / `dotnet test` 通过，空测试绿灯 |
| **M1** | Layer 0：`Core`（容器、字符码、断言、比较器） | 单测覆盖 core 等价语义 |
| **M1b** | Layer 0 其余：`Paths`（`path.ts`）、`Semver`（`semver.ts`）、`PerformanceCore` | 路径与 semver 语义单测（含 TS 文档中的路径表格） |
| **M2** | Layer 1：`types.ts` 的枚举与标志（`SyntaxKind` 全量、`Extension`、`ScriptTarget`…）+ `Node` 类型层级 | 枚举值与 TS 逐项校验（`tools/gen` 脚本比对） |
| **M2b** | 节点模型：`types.ts` 的接口/类型别名 → 237 个类 + 302 个接口（见 §12） | 生成物编译通过；继承链与能力接口单测 |
| **M2c** | Layer 0 其余：`sys.ts` 的宿主接口与纯逻辑（`getNodeSystem` 除外，见 §13）、`performance.ts` | 宿主抽象与计时埋点单测 |
| **M3a** | Layer 1 补充：诊断基础设施 —— 全量诊断表由 `tools/Gen` 从 `diagnosticMessages.json` 生成（2,130 条），加 `DiagnosticMessage` | 生成物编译通过；名字改写规则与全表不变量单测 |
| **M3** | Layer 2：`Scanner` 全量（见 §14） | 单测：token 流、trivia、错误恢复、关键字表 |
| **M4** | Layer 3：`Parser` + `NodeFactory` | 能对 conformance 用例产出 AST；语法诊断 baseline 对齐 |
| **M4a** | Layer 3 前置：`factory/nodeTests.ts` 的 227 个谓词由 `tools/Gen` 生成（`Parser/NodeTests.cs`） | 与 TS 源的函数数、返回类型、参数类型对齐；kind 而非类 |
| **M4b** | Layer 3 基础设施：`emitNode.ts` → `Parser/EmitNodes.cs`、`nodeChildren.ts` → `Parser/NodeChildren.cs`，加 parse-tree 谓词 | `EmitNode` 槽位的创建/复用/释放、合成注释、token 源映射表；`SyntaxList` 直存子节点 |
| **M5** | Layer 4：`Binder` | `.symbols` baseline 对齐（作用域/符号） |
| **M6** | Layer 5：`Program` + `Checker`（分期：基础类型 → 泛型 → 高级特性） | `.types` / `.errors.txt` 语义 baseline 对齐 |
| **M7** | Layer 6/7：Transformers + Emitter | `.js` baseline 对齐 |
| **M8** | Layer 8：CLI / tsconfig / watch / build | `tsc` 能编译真实项目 |

---

## 9. 移植纪律

1. **自底向上，逐层验收**：上层开工前，下层必须有测试且通过。
2. **每个模块完成后立刻 commit**（`[AI-<model>] <模块>: <一句话>`）。
3. **不做"硬翻"**：函数体按 C# 惯用法重写（见 §5），但**行为必须等价**，语义等价性由外环 baseline 兜底。
4. **遇到无法直接表达的 TS 构造**：在代码处用 `// PORT-NOTE:` 注释说明降级方案，并回写 §4/§5。
5. **枚举与诊断表**：优先从 TS 源用脚本生成，避免手工抄写出错（`tools/gen`）。
6. **热路径性能**：checker/emitter 的集合与字符串操作需注意分配；先保证正确，M6 后统一做性能剖析。
7. **正则用源生成器**：编译期常量模式一律写成 `[GeneratedRegex]` 的 partial 方法（`private static partial Regex Xxx();`），不用 `new Regex(..., RegexOptions.Compiled)`。生成器会为每个模式生成专用的 `RegexRunner`，既没有首次使用的编译开销，也比 `Compiled` 的反射发射更快。模式在运行时才算出来的地方（`Wildcard.GetRegexFromPattern`）只能用 `new Regex`，并在代码处注明原因。
8. **生成的类型一律 `partial`，手写部分接在另一个文件里**：`tools/Gen` 产出的每个 `class`/`interface`/`static class` 都带 `partial`（enum 不行，也不需要），这样生成器处理不了或不该校死的成员可以放在**同名的另一个文件**里，由编译器合成同一个类型。收益有两处：一是生成器不必为个别成员长特例——`SyntaxList._children` 原本让 `_` 前缀规则开了个口子，现在由 `Model/SyntaxList.cs` 补齐；二是后面几层（M4c 起）遇到"生成的类需要一个辅助方法/额外接口"时，不必改生成器重跑全量。约定：手写文件以**类型名**命名（`Model/SyntaxList.cs`），与生成物（`Model/NodeClasses.cs`）不同名，生成器只覆盖后者。这条不变量由 `GeneratedPartialTests` 守住。

---

## 10. 累积差异与决策清单

移植过程中发现的、与原源码有意不同的地方都在这里登记。代码中对应处以 `PORT-NOTE:` 注释标注。

### 10.1 命名

| TS | C# | 原因 |
|---|---|---|
| `core.ts` 的各函数 | `static partial class Core` | C# 没有模块级函数；`Core/` 目录仅是组织手段，命名空间仍是扁平的 `TypeScript.Compiler` |
| `CharacterCodes.$` | `CharacterCodes.Dollar` | `$` 不是合法 C# 标识符 |
| `CharacterCodes.equals` | `CharacterCodes.EqualsSign` | PascalCase 化后会与 `object.Equals` 撞名 |
| `EqualityComparer<T>` / `Comparer<T>`（TS 委托） | 同名 C# 委托 | 会遮蔽 BCL 的 `EqualityComparer<T>` / `Comparer<T>`；需要 BCL 版本时在该处写全限定名（见 `Core.EquateValues`） |
| `Queue<T>`（TS 接口） | `TsQueue<T>` | 避免与 `System.Collections.Generic.Queue<T>` 混淆 |
| `MultiMap<K,V>`（挂在 Map 实例上） | 组合式 `MultiMap<K,V>` 类 | C# 不能给实例加成员；继承 `Dictionary` 又会与 `Add(TKey,TValue)`（仅返回类型不同）冲突 |
| `System`（`sys.ts` 的宿主接口） | `ISystem` | `System` 是 C# 的根命名空间 |
| `tracing`（模块级 `let`） | `Tracing.Current`（可空实例） | `tracing` 在 TS 里是"可能没有"的值，调用点一律 `tracing?.…`，故保留可空形态而非空对象 |
| `tracingEnabled`（namespace） | `TracingEnabled`（类，单例 `Instance`） | §5 规则 6/12；它的状态要跨 start/stop 存活（legend 累积、`traceCount` 递增） |
| `tracingEnabled.Args`（索引签名） | `TracingArgs` + `TracingValue` | C# 无对象字面量；值的一元联合用显式标记结构表达（§5 规则 4/10） |
| `TracingNode.tracingPath`（展开属性） | `Node.TracingPath` | C# 实例无法加展开属性，槽位声明在 `Node` 上，接口本身消失 |
| `WatchOptions`（接口） | `IWatchOptions`（生成）+ `SysWatch.WatchOptions`（具体实现） | 生成器给接口加 `I` 前缀（D1）；TS 到处写对象字面量，需要一个可构造的具体类型 |
| `FsWatchCallback` 的 `eventName: "rename" \| "change"` | `SysWatch.FsWatchEventName` | 字面量联合做比较容易写错，而且它是宿主边界（宿主自己映射原生事件） |
| `sys.ts` 里 `{ getModifiedTime }` / `{ getModifiedTime, setTimeout }` 这类结构化 host 参数 | `IModifiedTimeHost` / `IPollingWatchFileHost` | TS 靠结构兼容，C# 要求具名 |

### 10.2 语义映射（有意为之的等价改写）

- **JS truthiness**：新增 `Js.IsTruthy`。TS 代码里 `if (v)`、`v ? … : …` 大量依赖 truthy，C# 只有 `bool`；凡是原样依赖 truthy 的位置都调用它，而不是偷懒写成 `!= null`。
- **稳定排序**：TS 用 `Array.prototype.sort`（ES2019 起稳定），C# 的 `List.Sort`/`Array.Sort` 是内省排序、**不稳定**。`Core.StableSort` 用原始索引做 tiebreak 来恢复稳定性，所有排序入口（`toSorted`/`sortAndDeduplicate`/`deduplicate`）都经过它。
- **字符串序**：JS 的 `<`/`>` 按 UTF-16 码元比较；C# 的 `string` 没有比较运算符，统一走 `string.CompareOrdinal`。
- **字符串索引越界**：JS 的 `str.charCodeAt(i)` 越界返回 `NaN`，与任何字符码比较都为 `false`；C# 的 `s[i]` 越界**抛异常**。`Paths.CharCodeAt` 把越界读映射为 `-1`（落在 0..0xFFFF 之外，比较结果与 `NaN` 一致）；`path.ts` 的根长度解析（`//`、`c:`、`file:///`）依赖这一点，直接翻译会在 `"/"` 这类输入上崩溃。
- **大小写折叠**：JS 的 `toUpperCase`/`toLowerCase` 是 **full case mapping**（`"ß"` → `"SS"`），.NET 的 `ToUpperInvariant`/`ToLowerInvariant` 是保持长度的 simple mapping，两者在 ß、İ 这类字符上不一致：TS 的 `equateStringsCaseInsensitive("ss", "ß")` 为 `true`，只用 .NET 的映射会得到 `false`。移植版因此走 **ICU** —— `Core/IcuCaseFolding` 探测并加载 `u_strToUpper`/`u_strToLower`（UTF-16 接口、root locale，即 V8 用的那张表），原来所有 `ToUpperInvariant`/`ToLowerInvariant` 调用点都改成它。
  - ICU 不可得时回退到 .NET 的 invariant 映射，并在 stderr 上**打印一次警告** —— 降级必须可见，否则一条行为差异会变成谜团。
  - 探测形态：Windows 的 `icuuc.dll` 用未加后缀的符号名，Unix 构建按 ICU 主版本重命名符号（`u_strToUpper_74`）并把版本写进文件名，两种都试（版本 60..80 逐个探测）。
  - 入口点存成 `delegate* unmanaged[Cdecl]<…>` **函数指针**，而不是被 marshaller 包装的委托：ICU 完全是动态加载的，调用里也没有托管成分需要编组（字符串手动 `fixed`、locale 就是一个 NUL 字节），委托只会多一层间接。指针被包在 `ToUpper`/`ToLower` 后面，调用方看不见它 —— `unsafe` 的边界因此只落在这一个文件（`TypeScript.Compiler.csproj` 开 `AllowUnsafeBlocks`，见 D7）。
  - `Wildcard.GetRegexFromPattern` 的 `RegexOptions.IgnoreCase` 不走这条路径：那是正则引擎自己的折叠，与 V8 的差异单独记在该方法处。
- **`StartsWith`**：TS 写 `str.lastIndexOf(prefix, 0) === 0`。.NET 的 `LastIndexOf(value, 0, Ordinal)` 在 `value` 长于 `startIndex + 1` 时返回 `-1`（实测 `"./foo".LastIndexOf("./", 0, Ordinal) == -1`），语义不同，因此改写为 `str.StartsWith(prefix, Ordinal)` 并补上 TS `slice` 的长度收敛行为。
- **"返回同一实例"的优化**：`filter`/`sameMap`/`compact`/`concatenate`/`appendIfUnique` 在 TS 里会把**入参本人**返回（"什么都没变"时），checker 依赖这个优化。移植版把这类数组工具的参数与返回类型统一成 `IList<T>` —— JS `Array` 在 C# 里的对应物 —— 于是那一份实例可以真的交回去；生成物里 `readonly T[]` 的映射也随之从 `IReadOnlyList<T>` 改成 `List<T>`（见 §12.3）。只有实现内部（`new List<T>(…)`、计数用的局部变量）才出现具体类型；`IList<T>` 没有 `RemoveRange` 这类成员，那里展开成等价的循环。
- **断言分级**：TS 通过把 `Debug` 上的断言函数替换成 `noop` 并缓存来实现分级；C# 不能在运行时替换自身方法，改为每个受分级控制的断言在入口处判断 `ShouldAssert`。调用点契约不变。
- **JS 位运算 + 双精度混合算术**：`generateDjb2Hash` 的三行代码里，`acc << 5` 每步把 `acc` 强制成 int32，而随后的 `+` 是**双精度加法、不再截断**，于是 `acc` 又离开 int32 区间（2000 字符输入下 2000 步里有 1894 步如此）。用 `int` 累加会在这些步骤上截断，用 `long` 会在超过 2^53 后与 JS 分歧，因此移植版用 `double` 累加，并新增 `Js.ToInt32`（JS `ToInt32` 精确语义：`NaN`/无穷 → `0`，否则按 2^32 取模再落回有符号区间）与 `Js.NumberToString`（JS `Number#toString()` 对整数值输出十进制整数，而 C# 的默认格式早得多就切到科学计数法）。
- **truthy 的 `string` 重载**：`Js.IsTruthy` 有 `object?` 与 `T? where T : class` 两个重载，而对 `string` 参数**泛型那个会胜出**，答的是"非 null"——于是 TS 的 `if (spec)` 被译成"非 null"，空串成了 truthy，`flatMap` 的 wildcard 规格就是这么错的（下一条）。现在有 `IsTruthy(string?)` 专用重载（带 `[NotNullWhen(true)]`），泛型重载上写明了它的适用边界。
- **`flatMap` 丢弃 falsy**：TS 的 `flatMap` 判的是 `if (v)`，`""`/`0` 都会被丢掉；移植版原本写成 `v != null`，空规格于是变成"永远匹配"的一个分支。已改为 `Js.IsTruthy`。
- **正则**：JS 的 `/i` 不参考环境 culture，`getRegexFromPattern` 因此固定加 `RegexOptions.CultureInvariant`；字符类里的 `\w` 是 ASCII-only 而 .NET 的是 Unicode-aware，所以 `[^\w\s/]` 写成 `[^A-Za-z0-9_\s/]`（否则 `é` 之类不会被转义）。
- **`missingFileModifiedTime` 的比较**：TS 比的是**引用**（`existingTime === missingFileModifiedTime`），C# 里是 `DateTimeOffset` 的**值**比较。两者只在"文件确实存在于 epoch 0"时分歧；`getModifiedTime` 的兜底返回的正是那个值，所以保持等价。
- **`Number(text)`**：JS 对不可解析的输入得到 `NaN`，而 `NaN` 是 falsy，后续 `customLevels[level] || levels[level]` 会退回默认值；移植版的 `Number` 直接退化为 `0`，与 `Levels` 里"0 表示未设置"一致。
- **"watcher 数量耗尽"**：TS 判 `e.code === "ENOSPC"`；C# 里是 `SysCore.IsSystemWatcherLimitExceeded`（`HResult == 28`，或消息里出现 `ENOSPC` 作为兜底）。
- **tracing 的 JSON**：TS 把普通 JS 对象交给引擎的 `JSON.stringify`。移植版没有 `JSON.stringify`，追踪自己的值与对象因此有显式形态（`TracingArgs`/`TracingValue`）和一个私有写入器（`TracingJson`），并保留该 API 的三条行为：成员按插入序、值为 `undefined` 的成员不写出、数组里的 `undefined` 写成 `null`。数字用 `Js.NumberToString`（`JSON.stringify` 把非有限数写成 `null` 的那条规则照抄）。
- **tracing 的写入与主机**：TS 在 `startTracing` 里 `require("fs")` 拿文件描述符（整个编译器只有它绕过 `sys`，因为 `sys.writeFile` 不能流式写）。移植版的主机由 `Tracing.StartTracing` 传入（§13.5 没有全局 `sys`），文本先攒在 `StringBuilder` 里、`StopTracing` 时一次 `ISystem.WriteFile`。两处差异都只在"未走完的运行"里可见：崩溃的进程不会留下 trace（TS 留下被截断的前缀），以及 trace 在生成期间驻留内存（TS 边算边写盘）。
- **tracing 的宿主依赖**：`getLocation` 用到 `Utilities.GetSourceFileOfNode`/`UnescapeLeadingUnderscores`（都在 Utilities），行号列号走 `ScannerUtilities.ComputeLineAndCharacterOfPosition` —— `ISourceFileLike` 目前没有任何类型实现，所以行起点是从 `SourceFile` 上取的（与 `Utilities.Binder.Diagnostics.cs` 同一处收窄，见该文件的 PORT-NOTE）。

### 10.3 有意复刻的上游行为（看起来像 bug，但必须保持一致）

- `relativeComplement` 的内层循环一旦耗尽 `arrayA` 就结束本轮 `offsetB`，所以"大于 `arrayA` 全部剩余元素"的 `arrayB` 元素不会进入结果（例如 `relativeComplement([1,3,5], [1,2,3,4,5,6])` 得到 `[2,4]` 而非 `[2,4,6]`）。上游调用方保证 `arrayA` 覆盖 `arrayB` 的最大值，因此该分支不会被触发；移植版保留该语义。
- `insertSorted` 在"插入位置的前一个元素已相等"时返回 `false` 且不修改数组（只在与后继相等时才替换）。
- `tracing.ts` 的 `dumpTypes` 给递归身份发号时写的是 `if (!recursionToken)`，而 `0` 也是 falsy：拿到 0 号的身份在后续每次查表时都会被重新编号（`recursionId` 因此会漂移）。它只影响 trace 的 `recursionId` 字段，移植版照抄（`Core/Tracing.cs` 的 `DumpTypes`）。

### 10.4 尚未移植（有 TODO 标注，随对应层补齐）

| 项 | 归属 |
|---|---|
| `debug.ts`: `failBadSyntaxKind`、`assertEachNode`、`assertNode`、`assertNotNode`、`assertOptionalNode`、`assertOptionalToken`、`assertMissingNode`、`formatSymbol`、`formatEnum`、`formatSyntaxKind` | M2（需要 `Node`/`Symbol`/`SyntaxKind`） |
| `core.ts`: `compareTextSpans` | M2（需要 `TextSpan`） |
| `core.ts`: `getAllKeys`、`assign`、`clone`、`extend`、`copyProperties`、`maybeBind` | M5（形态取决于 `CompilerOptions` 等如何建模，不用反射硬凑） |
| `core.ts`: `arrayToNumericMap` | M5（JS 的稀疏数组赋值在 C# 无对应形态） |
| `core.ts`: `singleOrMany`、`combine` | M5（`T \| T[]` 联合类型在 C# 无直接对应） |
| `core.ts`: `isNodeLikeSystem` | 已实现为常量 `false`（CLR 上永远不是 Node） |
| `core.ts`: `toArray` | 已实现，但需由调用方给出元素类型参数（`T \| T[]` 无法在运行时区分） |
| `sys.ts`: `getNodeSystem` 及其 Node 绑定（`readDirectory`/`require` 依赖 Layer 5，CPU profiler 在 CLR 无对应） | M8（见 §13.2）—— 策略层已全部移植，缺的是 `ISystem` 的 .NET 实现 |

---

## 11. 设计决策记录

### 11.1 已确认（变更需显式推翻）

**D1 — `Node` 类型层级：每类节点一个 C# 类。**

- 每个 `SyntaxKind` 对应一个具体 C# 类（`Identifier`、`CallExpression`、`FunctionDeclaration`…，约 350 个），`Node` 是抽象基类，持有 `Kind`/`Flags`/`Parent`/`Pos`/`End`/`TransformFlags`/`Id`/`Original`/`EmitNode` 等公共槽位。
- TS 用接口继承表达的"横向能力"（`Declaration.symbol`、`LocalsContainer.locals`、`FlowContainer.flowNode`、`JSDocContainer.jsDoc`）映射为**接口**（`IDeclaration`、`ILocalsContainer`、`IFlowContainer`、`IJsDocContainer`），节点类按需实现。
- 判定/窄化用 `is` 模式匹配；`binder`/`checker`/`transformer` 里的 `switch (node.kind)` 与 `isXxx(node)` 谓词都建立在此之上。
- **不动摇**：不采用"单一 Node 类 + 属性字典"，也不采用"少量分组基类"。理由：C# 是名义类型语言，放弃类层级就等于放弃类型系统，而 parser/factory/checker 的绝大部分正确性依赖它。
- 实现约定：节点类**可变**（binder 原地写 `Symbol`/`Locals`/`FlowNode`），`readonly` 仅表示"解析后不再写"，用普通 setter 表达。

**D2 — 数据表：脚本生成 + 生成物提交入库。**

- `tools/Gen`（独立 .NET console 项目，不依赖 Node/Python）读取 TS 源，生成 C# 并**写入仓库**。
- 覆盖：`SyntaxKind`、`NodeFlags`/`ModifierFlags`/`SymbolFlags`/`TypeFlags` 等位标志、`Extension` 之类的常量表、`diagnosticMessages.json` → 诊断表。
- 生成文件带 `// <auto-generated>` + `// DO NOT EDIT` 头注释，注明来源文件与生成命令。
- `tools/Gen --check` 模式只校验、不写入 —— 接入单元测试，TS 源更新导致漂移时立刻失败。
- 生成器**不使用**正则硬啃 TS 语法；枚举体只做"成员名 + 可选显式值 + 注释"的行级解析，超出该形态就报错退出（宁可失败也不要生成半对的东西）。

**D3 — 范围：`src/compiler` + `src/tsc`。**

- 含 scanner / parser / factory / binder / checker / transformers / emitter / sourcemap / commandLineParser / executeCommandLine / watch / builder / tsbuild。
- **不含** `src/services`、`src/server`、`jsTyping`、`typingsInstaller`、`watchGuard`。
- `deprecatedCompat`（向后兼容垫片）视需要裁剪，策略随实现再议。

**D4 — `__String`：值类型 + 全局池。**

- `SymbolTable` 的键、`InternalSymbolName` 比较等高频路径使用驻留（interned）字符串。
- 形态：`readonly struct` 包装池化的 `string`，等值与哈希走池内引用比较。配套 TS 的 `escapeLeadingUnderscores` / `unescapeLeadingUnderscores`。
- 落地时机 M4（`SymbolTable`）。这是唯一一个**改晚了要返工**的决策，所以提前定死。

**D5 — baseline 外环：独立 xUnit 项目。**

- 单独的 `tests/TypeScript.Baselines` 项目，跑 `tests/cases/**` 并比对 `tests/baselines/reference/`。
- 与 `TypeScript.Compiler.Tests`（内环：快、无外部依赖）分开，避免外环的 I/O 与用例数量拖慢内环反馈。
- 内环失败 = 我改坏了；外环失败 = 与 TS 语义有差距。两类信号不混在一起。

**D6 — 诊断不本地化，但保留本地化框架（只实现英文）。**

- 诊断建模为「消息模板 + 参数 + 位置」，取文案时经过一个可替换的本地化解析层。
- 只提供英文文案；`loc/lcl/` 下的 13 种语言不移植。
- 这样以后要加本地化不必回头改诊断的建模方式。

**D9 — `CompilerHost`：完整复刻 TS 的 host 接口。**

- `System`（sys.ts）、`CompilerHost`、`ModuleResolutionHost` 等按 TS 定义照搬，不做简化版。
- 好处：可以用虚拟文件系统做测试，且将来能逐项核对 host 语义；简化版会在 M5/M6 逐个暴露缺口。

**D10 — `sys.ts`：策略逐行移植，原语由 .NET 承接。**

- `sys.ts` 分成两半：**策略**（什么时候算"变了"——轮询队列、优先级、递归目录、原生 watcher 失败后的回退）与**原语**（读文件、stat、OS watcher、计时器）。策略进 `Core/SysWatch*.cs`，对着源码逐行核；原语只留契约（`ISystem` 等），实现交给 .NET。
- 理由：`getNodeSystem` 的每一行都是 Node API 绑定，翻译它既不能增强可对照性，也没有 baseline 可验；而策略那半是真正的编译器行为，必须一字不差。
- 划界标准是"有没有验收手段"：能对着 `sys.ts` 逐行核对的进 `Core`，只能靠集成测试验证的推迟到 M8（`SystemHost`）。对照表见 §13.2。

### 11.2 待定（到相应里程碑再决策）

| # | 决策 | 何时需要 | 影响 |
|---|---|---|---|
| D7 | 热路径优化：`Span`/`unsafe`/对象池的引入边界 | M6（checker 可跑之后） | 先正确后快；过早优化会污染移植的可对照性。**首次（也是目前唯一）引入 `unsafe` 的是 `IcuCaseFolding` 的 P/Invoke 函数指针，见 §10.2 —— 那不是优化，而是"动态加载的库只能这么调"。** |

### 11.3 生成器（tools/Gen）

```
dotnet run --project tools/Gen --            # 重新生成，写入仓库
dotnet run --project tools/Gen -- --check    # 只校验不写入（CI / 本地预检）
```

- **输入基准**：`tools/ts-source.txt` 里记录的 checkout（当前为 release-6.0 @ `050880ce59`，版本 6.0.3）。移动基准时先改这里的注释，再重跑生成器。
- **覆盖**：`SyntaxKind`（396 个成员，其中 37 个是哨兵别名）、`NodeFlags`、`ModifierFlags`、`JsxFlags`、`SymbolFlags`、`CheckFlags`、`NodeCheckFlags`、`TypeFlags`、`SignatureFlags`、`TransformFlags`、`EmitFlags`，以及诊断表（`diagnosticMessages.json` → `Model/Diagnostics.cs`，2,130 条）。
- **类型模型之外的数据表**：`TypeModelSources` 之外的枚举按名字抽取（`extract.cjs` 的 `enumSources`），与 `tableSources` 同形 —— `Associativity` 与 `OperatorPrecedence` 声明在 `utilities.ts`，读整份文件会把它的 500 余个声明拖进节点模型，所以只点名这两个。它们也在 `Generator.Specs` 里有显式条目：`Associativity` 的值是 0/1，默认的"值是独立位"启发式会把它判成 `[Flags]`。
- **保证**：单元测试 `GeneratedTablesTests` 直接调用 `Generator.Generate` 与仓库内文件逐字节比对，所以 `--check` 的约束在 `dotnet test` 里同样成立；TS 源更新后忘记重跑生成器会立刻失败。
- **边界**：生成器只理解 TS 写这些表用的形态（成员名、可选 `=` 表达式、`/** */`、`//`、跨行的 `|` 链）。遇到别的写法会**报错退出**，不猜测。

**生成过程中确认的三处 TS enum → C# 的差异（生成器已处理）**：

1. TS 的 `const enum` 在编译期内联，别名不占运行时位置；C# 的 enum 保留别名成员，于是 `Enum.GetValues` 会返回重复值，`ToString()` 对别名也不可靠。**调试输出必须用独立的名称表**（`formatSyntaxKind`，随 M2 的 model 层实现），不能依赖 `Enum.GetName`。
2. **C# enum 没有一元 `~`；更微妙的是，enum 成员初始化式要求常量表达式的类型是「底层类型」而不是枚举类型。** 两点叠加决定了两种不同写法：
   - **生成的 enum 成员**（TS 的 `ModifierFlags.Modifier = All & ~Decorator`）写成 `(uint)All & ~(uint)Decorator` —— 所有操作数转换到底层类型，整体保持 `uint`。外层**不能再包** `(ModifierFlags)`：成员初始化式要的是 `uint`，包了会报 `CS0019`/`CS0266`。只做 `|`/`&`/`^` 组合的表达式（`A | B`）无需转换，C# 为 enum 定义了这些运算符。
   - **移植代码里的运行时表达式**（值需要留成 enum 类型）用 `Flags.Clear(value, mask)`（`Core/Flags.cs`），展开为 `(T)((U)value & ~(U)mask)`。
   - 负数（TS 的 `SymbolFlags.All = -1`）渲染为 `unchecked((uint)-1)`：`-1u` 在 C# 里是 `long`，赋不进 `uint` 底层的成员。
   - 移位：`1u << 31` —— 左操作数需要后缀（否则 `1 << 31` 溢出 `int`），右操作数**不能**带后缀（C# 只有 `T << int`，没有 `uint << uint`）。
   - 生成器**保留表达式**而非求值成字面量，正是为了这几处能与 TS 源码逐字对照；求值仍然保留，用于挑选底层类型宽度和检出越界成员。
   - 生成器**不照抄**字符串字面量。JS 的 `\x` 是定长两位、`\0`/`\'` 的规则也与 C# 不同，所以字面量先解码成实际字符，再按 C# 规则重新编码；遇到不认识的转义会中止生成而不是猜。

5. **`types.ts` 的接口分两类生成：链上的 → C# 类，分类接口与 mixin → C# 接口。** 判据是**位置**而非形状：TS 把节点的分类链写在 `extends` 首位、mixin 写在后面（`CallExpression extends LeftHandSideExpression, Declaration`）。
   - 只在首位出现过的接口 → 类：`Expression`、`LeftHandSideExpression`、`LiteralLikeNode`、`BinaryExpression`…
   - 出现过非首位的接口 → 接口：`Declaration`（`SourceFile` 放首位、`Identifier` 放次位）、`JSDocContainer`、`LocalsContainer`、`FlowContainer`，以及 `PrimaryExpression`（`Identifier` 放首位，但 `LiteralExpression extends LiteralLikeNode, PrimaryExpression` 放次位）。
   - 结果：**237 个类 / 302 个接口**（修正见 §12.4；数字随 §15.5 的声明合并修正从 235 升到 237），`CallExpression` 的链解析为 `CallExpression → LeftHandSideExpression → UpdateExpression → UnaryExpression → Expression → Node` ✓
   - 代价：`Identifier` 的 C# 基类因此是 `Node` 而非 `PrimaryExpression`（后者降为接口）。分类接口本身没有实字段（只有 brand），所以类型关系通过接口完整保留，不丢信息。
   - `PunctuationToken`/`KeywordToken`/`ModifierToken`/`NodeArray` 只经**类型别名**到达（`type DotToken = PunctuationToken<SyntaxKind.DotToken>`），位置规则看不到，用显式清单标为类。这也是 token 家族只需要少数几个类、而不是 187 个空类的原因。
   - 泛型参数在比较基名时必须剥离（`PunctuationToken<TKind>` → `PunctuationToken`），否则继承图与角色判定全线失配。

6. **类型模型的输入源是 TypeScript 编译器自己的解析器，不是 C# 侧的行扫描。** 最初生成器用行扫描 + 正则啃 `types.ts`，在修掉五个坑之后仍然是脆弱的：成员省略分号、泛型参数里嵌套尖括号、单行接口、`}` 与下一个声明同行、`const enum` 值跨行。既然 checkout 的 `lib/typescript.js` 就是完整的 6.0.3 编译器，改由 `tools/Gen/extract.cjs`（Node）用它解析并输出 JSON，C# 侧只做反序列化。
   - 提取出的模型与行扫描在**接口数与别名数上完全一致**（523 / 274），但**成员数从 3011 降到 1832** —— 行扫描把 `foo(x): void;` 这类方法签名也当成了数据成员，而 `ts.isPropertySignature` 正确排除了它们。
   - `node` 依赖被限定在生成环节：`src/` 下没有任何东西依赖它，提取出的 JSON 写在 `artifacts/`（不提交）。

7. **诊断表的成员名必须逐字符复刻 TS 自己的改写，不能另起一套命名。** 源表（`diagnosticMessages.json`）以**消息文本本身**为 key，C# 成员名是它经过 `scripts/processDiagnosticMessages.mjs` 的 `convertPropertyName` 得到的结果。移植代码里写的正是 `Diagnostics.Unexpected_character` 这类名字，改名等于每个调用点都编译不过。
   - 规则：`*`、`/`、`:` → `_Asterisk`、`_Slash`、`_Colon`；其余非 `\w` 字符 → `_`（JS 的 `\w` 是 ASCII-only，生成器里写成显式字符类）；连续下划线压缩成一个；开头的下划线若后面不是数字则去掉；结尾下划线去掉（TS 的正则没有 `/g`，只去一个）。
   - `key`（本地化用）＝"成员名前 100 字符 + `_` + code"，`slice(0, 100)` 照抄；表里 281 条消息的名字超过 100 字符。
   - 生成器多做两项检查（TS 的脚本只查 code 唯一）：成员名不得重复（JS 对象字面量会静默保留后者，C# 里是编译错误），成员名必须是合法 C# 标识符（`1st line` 在 JS 里是合法 key，在 C# 里不是）。

---

## 12. 节点模型（M2b，已完成）

### 12.1 交付

| 项 | 结果 |
|---|---|
| 提取 | `tools/Gen/extract.cjs`（用 TS 编译器）→ `TsModel`；76 个枚举 / 537 个接口 / 276 个别名（三个源文件合并） |
| 映射 | `TsTypeMapper`：TS 类型 → C# |
| 分类 | 239 个类（继承链）/ 295 个接口（mixin），`tools/Gen --stats` 口径（数字随模型修正而变，见 §12.4） |
| 生成物 | `Model/NodeInterfaces.cs`、`Model/NodeClasses.cs`，以及全部枚举 |
| 手工 | `Node.cs`（抽象根 + `abstract Kind`）、`NodeArray.cs`、`Token.cs`（token 家族） |
| 验收 | `NodeModelTests` + `GeneratedTablesTests`（逐字节比对生成物） |

### 12.2 关键设计：成员类型沿接口链收窄

TS 允许子接口**收窄**成员类型 —— `FunctionDeclaration.body` 是 `Block`，而 `FunctionLikeDeclarationBase.body` 是 `ConciseBody`。C# 要求**隐式**实现与每一个接口精确匹配，而**协变返回只适用于 `override`，不适用于接口实现**。解法是两者组合：

- 类保留**自己的精确类型**：`public Block? Body { get; set; }`
- 类型不同的接口用**显式接口实现**桥接：`Node? IFunctionLikeDeclarationBase.Body => Body;`

这样读取永远走协变方向（安全），类的精确类型也不丢。可写成员的 setter 会向下 cast，安全性由 TS 侧保证（这些字段只由 parser/factory 写入，且 TS 里多为 `readonly`）。

### 12.4 分类规则的修正（M3 期间）

§12.1 的位置规则单独用会**高估**节点集合。`Symbol` 只被 `TransientSymbol` 首位继承、`Type` 只被它的子类型首位继承、`CommentRange` 只被 `SynthesizedComment` 首位继承 —— 形状与 `Expression` → `LeftHandSideExpression` 这类分类台阶一模一样，但它们与语法无关。它们此前被生成为 `Node` 的派生类（`abstract class Symbol : Node`），而 `CommentRange` 还挡住了 M3 的 `getLeadingCommentRanges`：那里需要一个**可构造**的注释范围值。

修正：位置规则之上再加一条**必要条件** —— 该接口的 `extends` 链必须能到达 `Node`（`NodeArray`/`MutableNodeArray` 是显式强制的类，不走这条路径）。于是 268 类 / 269 接口 → **239 类 / 295 接口**（`tools/Gen --stats` 的当前口径；§15.5 的声明合并修正又动过这两个数），节点链不受影响（`CallExpression → LeftHandSideExpression → UpdateExpression → UnaryExpression → Expression → Node` 依旧）。

代价：`MapUnion` 的 `CommonBase` 对"到不了 `Node`"的成员会落到 `Node`，于是这类 union 的映射从"某个具体类"退化成 `Node`。这些 union 本来也只能降级（C# 里它们没有共同基类），M4/M5 真正用到时再收紧。

---

## 14. 词法：`Scanner`（M3，已完成）

### 14.1 交付

| 项 | 结果 |
|---|---|
| 数据表 | `Lexer/ScannerTables.cs`（生成）：4 张 Unicode 范围表（4,454 个端点）、关键字与 token 拼写、正则 flag 表、Unicode 属性表 |
| 纯函数 | `Lexer/ScannerUtilities.cs`：拼写查找、行列换算、字符分类、标识符判定（含范围表二分）、trivia 与注释范围、码点编解码 |
| Scanner | `Lexer/Scanner.cs`：`Scan`、`reScan*`、JSX、JSDoc、speculation 与正则字面量的完整语法走查 |
| 支撑类型 | `Core/CommentRange.cs`、`Core/CommentDirective.cs`、`Core/TextRange.cs`、`Core/LineAndCharacter.cs` |
| 验收 | `ScannerTests`（14 个）：token 流、运算符最长匹配与 `reScanGreaterToken`、trivia、数字与 bigint 的规范化拼写、字符串与转义、模板分片、正则字面量、未终止字符串/正则、错误回调、JSX 文本与 JSX 标识符、JSDoc 标记、`ScriptTarget` 对标识符字符的影响、注释指令、`lookAhead` 回退 |

### 14.2 与 TS 的结构差异

- TS 的 `createScanner` 是一个闭包；移植版是 `Scanner` 的字段（状态）与方法。公开契约一一对应。
- `scanRegularExpressionWorker` 在 TS 里是**一个函数套 19 个嵌套函数**（约 1,000 行）。C# 的局部函数跨不了这么大的主体，所以那 19 个语法辅助落成 `Scanner` 的私有方法，TS 的闭包变量（`anyUnicodeMode`、`mayContainStrings`、命名捕获组作用域栈…）随之成为私有字段 —— 这是本次移植里唯一一处"为语言限制而改结构"的地方。
- `CommentKind` 在 TS 是字面量联合（`SyntaxKind.SingleLineCommentTrivia | SyntaxKind.MultiLineCommentTrivia`），C# 里直接用 `SyntaxKind`；`CommentRange`/`CommentDirective`/`TextRange` 在 TS 是接口 + 对象字面量，需要可构造的具体类型。
- 三处 JS↔C# 的固定套路：`return pos += 3, token = X;` 拆成两步；switch 的 fall-through 用 `goto case`；对泛型结果的 truthy 判断落成"不等于 `default`"。

### 14.3 测试抓到的两处真 bug

- `Js.ToNumber` 最初只认十进制，而 JS 的一元 `+` 认 `0x` 前缀 —— 于是 `0x1F` 的字面量值成了 `NaN`。
- `ScannerUtilities.CodePointAt` 用 `s[i]`，越界抛异常；JS 的 `codePointAt` 越界返回 `undefined`，而调用点依赖它表现为"不是标识符字符"（`checkForIdentifierStartAfterNumericLiteral` 会在文本末尾读一次）。

### 12.3 其他映射规则（都由生成器自动应用）

| TS | C# | 说明 |
|---|---|---|
| 联合类型 | 公共基类，或一组 C# 重载 | 见 §12.5 |
| 交叉类型 | 第一个成员 | `EmitNode & { … }` 的主体总是第一个 |
| 泛型基类引用 | 连同实参一起映射 | `ObjectLiteralExpressionBase<ObjectLiteralElementLike>` |
| 裸引用泛型 | 参数补 `object?` | TS 有类型参数默认值，C# 没有 |
| `Pick`/`Record`/`Partial`… | `object?` | TS 内置工具类型无对应 |
| `Map`/`Set` | `Dictionary`/`HashSet` | key 去掉可空（`notnull` 约束） |
| `readonly T[]` / `ReadonlyArray<T>` | `List<T>`（与 `T[]` 相同） | TS 的 `readonly` 只是编译期约束，运行时就是可变的 `Array`；映射成只读视图会与收 `IList<T>` 的数组工具不兼容（§10.2） |
| 引用类型成员 | 补 `?` | 生成物跑在 `#nullable enable` 下，而这些字段由 parser/factory 填充 |
| 字符串字面量键（`"amd-dependency"`） | 跳过 | 无合法 C# 拼写，属内部管线 |
| `_xxxBrand` | 跳过 | 只为让 TS 的结构类型名义化 |
3. **字符串值枚举生成为 `readonly struct` 包装，而不是 C# enum，也不是裸 `const string`。** TS 里有 4 个这样的枚举（`Extension`、`InternalSymbolName`、`PrivateIdentifierKind`、`ForegroundColorEscapeSequences`）。
   - C# enum 不能携带字符串值 —— 无法表示 `"__call"`、`"export="`、`"\u001b[90m"`。
   - 裸 `const string` 会丢掉名义类型，`fileExtensionIs` 这类函数的签名就退化成 `string, string`，没有任何东西能区分 `Extension` 与随手写的字面量。
   - 生成的 struct 同时保留两者：参数与字段可以声明为 `Extension`，底层字符串通过 `.Value` 取出。实现 `IEquatable<T>`、`==`/`!=`、`GetHashCode`（与 `string.GetHashCode(..., Ordinal)` 一致，保证字典键可用）、`ToString()`。
   - **不生成到 `string` 的隐式转换**：包装的意义就是调用点必须显式写 `.Value`，类型信息不在赋值处悄悄丢失。代价是 TS 里「把枚举成员直接当字符串传」的位置（例如把 `readonly Extension[]` 传给接受 `readonly string[]` 的 `getAnyExtensionFromPath`）在 C# 中要逐个 unwrap，`Paths` 里已按此处理并加了 PORT-NOTE。
   - 与之相关：生成器**不照抄**字符串字面量。JS 的 `\x` 是定长两位、`\0`/`\'` 的规则也与 C# 不同，所以字面量先解码成实际字符，再按 C# 规则重新编码；遇到不认识的转义会中止生成而不是猜。
   - `default(T)` 在 C# 里总是存在（TS 没有这个概念），struct 的 `Value` 对 `default` 返回空串而不抛异常，且与任何真实成员都不相等。
4. 另外还有一类**字符串字面量联合类型**（`TypeOfTag`、`BufferEncoding`、`OrganizeImportsTypeOrder`）。它们没有成员名，C# 中直接用 `string` 表示、值在调用点内联；到实际用到时（`TypeOfTag` 在 checker，`BufferEncoding` 在 `sys.ts`）再评估是否需要命名常量。

### 12.5 联合类型与索引访问类型（M4c 期间）

M4c 开工前把"退化成 `object?`"的每一处都复核了一遍：`INodeFactory` 的 503 个成员里有 185 个参数位是 `object?`。四条改动之后剩 109 —— 其中大多数由重载承担（218 条转发重载，覆盖 106 个槽位），真正收敛不了的只剩 3 处、且都带注释。

**1. 联合类型：能收敛就收敛，收不住就用重载。** 先把成员**映射后的 C# 类型**分成两组：

| 分组 | 判据 | 处理 |
|---|---|---|
| 节点类型 | 名字能在模型里回查（`Identifier`、`IDeclaration`） | 求 `CommonBase`，收敛成一个 |
| 原子 | `string`/`int`/`bool`/生成的枚举/BCL 集合 | 各自保留 |

- 只有节点组 → 公共基类，**不产生重载**。`ModifierLike`（`Modifier \| Decorator`，而 `Modifier` 又展开成一串 token 别名）因此收敛到 `Node`，`readonly ModifierLike[]` 得到 `List<Node>`。
- 只剩原子组 → 每个原子一条重载：`string \| number` → `(int)` + `(string)`。
- 两组都有 → 核心签名收 `object?`，再逐条重载：`string \| Identifier` → `object?` 核心 + `(Identifier)` + `(string)`。
- 重载体是接口的**默认实现**，薄转发到核心，实参显式转成核心参数类型 —— 不转的话它会解析回自己，递归到栈溢出。**实现方因此每个方法只写一个方法体**，与重载条数无关。
- 代价：互不相关的重载之间 `null` 有歧义（`CreateBreakStatement(null)` 同时命中 `string?` 与 `Node?`，CS0121）。调用点加一次 cast 即可，不为它另造类型。

**2. 类型别名走同一条路。** 非泛型别名先展开再判联合，所以 `DeclarationName`（6 路、成员本身还是别名）能一路展开到具体节点类型。`__String` 是例外：它必须命中手写 override（→ `string`）而**不能**展开，否则 TS 的 `(string & {…}) | (void & {…}) | InternalSymbolName` 里那个 `void` 会变成一个 `void name` 参数。TS 的多行联合以 `|` 开头（`type Modifier = | AbstractKeyword | …`），切分时空段要先丢掉 —— 漏掉它等于给每个成员集合掺一个 `object?`。

**3. 索引访问类型交给 tsc 的 checker。** `extract.cjs` 现在会建一个 `Program`（约 1 秒，拉进约 170 个文件），对 `X["y"]` 调 `checker.typeToString(checker.getTypeAtLocation(...))`：

| TS | checker 给出的类型 |
|---|---|
| `LiteralTypeNode["literal"]` | `NullLiteral \| BooleanLiteral \| LiteralExpression \| PrefixUnaryExpression` |
| `HeritageClause["token"]` | `SyntaxKind.ExtendsKeyword \| SyntaxKind.ImplementsKeyword` |
| `CompilerOptions["module"]` | `ModuleKind` |

推导不出来就保留原文（`T["name"]` 依赖未解析的类型参数，递归别名解析回自身），C# 侧照常降级并注释；`Program` 建不起来时提取照旧进行。索引访问类型在模型里一共 20 处。

**4. 未提取类型保留 `object?`，但加一行注释。** `BaseNodeFactory`（在 `baseNodeFactory.ts`）、`NodeFactoryFlags`（`nodeFactory.ts` 的 `const enum`）都在 `TypeModelSources` 之外，映射器因此返回 `object?` 并附上原因，由 emitter 写成 `<remarks>PORT-NOTE: …</remarks>`。同一个接口里 `converters: NodeConverters` 是真实类型而 `baseFactory: BaseNodeFactory` 是 `object?`，差别只在这份清单。（TS 自身的 `never`/`undefined` 不在这一档，它们有自己的类型，见 §12.6。注释里的名字按限定名整体识别，否则 `SyntaxKind.JsxTextAllWhiteSpaces` 会被拆成两个标识符、把成员名当成未声明的类型。）

**副作用：显式接口实现的两个方向都要 cast。** `DeclarationName` 现在收敛到 `Node`（以前是 `object?`），而 `ArrowFunction.name` 是 `never`（→ `object?`），类上的类型于是比接口更宽。`NodeEmitter` 的显式实现因此读写两侧都写 cast（读经 `object?` 中转，两个无关引用类型之间没有直接转换）。

### 12.6 TS 里"没有值"的两个类型

`object?` 一个人扛了三种含义：收敛不了的联合、声明在被提取源文件之外的类型、以及 TS 自身的特殊类型。前两种各自有了注释（§12.5），第三种现在有自己的落点：

| TS | C#（`Core/JsTypes.cs`） | 形态 |
|---|---|---|
| `undefined` | `JsUndefined` | JS 的 `undefined` 只有一个值，所以是私有构造的**单例**：`JsUndefined.Value` |
| `never` | `JsNever` | 底类型，没有任何值，所以构造私有、**不存在**实例 |

两者都实现标记接口 `IJsType`。用接口而不是共同基类：TS 的这些特殊类型彼此没有共同结构，而 C# 的基类槽位只有一个 —— 将来若有别的 JS 值语义（JS 的 number/string）要表达，这个接口还能容纳。

- **联合里它们不参与。** `A | undefined` 就是 `A`（可选性由 `?` 注解表达），`A | never` 也是 `A`（底类型）。漏掉后者的后果与漏掉多行联合的前导 `|` 一样：成员集合里掺进一个 `object?`。
- **它们与其它引用类型成员一样可空**（`JsNever? Name`）。生成类的成员由 parser/factory 在构造之后填充，非可空会撞上 CS8618；而 `JsNever?` 仍然比 `object?` 说得更多 —— 它写明"这里只可能是空"。
- **没有纳入的**：`void`（116 处几乎全是方法返回类型，C# 的 `void` 已经是对应物）、`any`/`unknown`（只出现在 `_xxxBrand` 这类被跳过的成员上，而 `object` 本来就是"任意值"的正确对应）、`null`（C# 的引用类型已经表达）。

实测落点 9 处：`ArrowFunction.name`（`never`）、`FunctionTypeNode.modifiers`、`FlowUnreachable.node`/`antecedent`、`FlowLabel.node`、`ThisTypePredicate` 与 `AssertsThisTypePredicate` 的 `parameterName`/`parameterIndex`、`DiagnosticWithDetachedLocation.file`、`NodeFactory.createYieldExpression(asteriskToken)`（都是 `undefined`）。另外 `readonly never[]`（"永远是空数组"）现在写成 `List<JsNever>`。

### 12.7 节点类不再 `sealed`

生成器原先给"没有生成类继承它"的节点类加 `sealed`（`NodeClasses.cs` 里有 203 个）。看上去只是给编译器留一点优化余地，实际代价是 **TS 的泛型约束没法照写**：`T extends Identifier` 在 C# 里虽然合法，但当 `Identifier` 是 sealed 时它的意思就变成"T 就是 `Identifier`"，于是手写代码里那一处（`EmitNodes.SetIdentifierTypeArguments`）被放宽成了 `where T : Node`，注释还写着"caller's static type carries the rest"。

现在节点类一律开放：生成物里 `sealed` 为 0，手写的 `NodeArray`/`MutableNodeArray`、`Token`/`ModifierToken`、`SyntaxList` 同步去掉，`SetIdentifierTypeArguments` 的约束照 TS 写成 `where T : Identifier`。代价是少了一点去虚拟化机会 —— D7 已经定了"M6 之后再谈性能"，而类型精度是 parser/checker/transformer 全程都要用的东西。

没有跟着改的是**非节点类型**：`CommentRange`、`DiagnosticMessage`、`MultiMap` 这类值包装，`EmitNode`（节点上的附加槽），以及 `TypedEnum` 的 359 个成员 —— 它们的 `sealed` 表达的是"本来就没有子类"，与 TS 的约束表达力无关。

### 12.8 能力基与分类台阶：字面量的 `Expression` 链

§12.4 的位置规则只看一个接口**是否出现在首位**。于是 `LiteralExpression extends LiteralLikeNode, PrimaryExpression, Declaration` 里的 `LiteralLikeNode` 被判成类，而它恰好排在首位 —— 生成物成了 `StringLiteral → LiteralExpression → LiteralLikeNode → Node`：**整条 `Expression` 链断掉**，所有 `is Expression` 判断都会失败，而 checker 与 transformer 到处依赖它。

TS 在这一行里同时继承了两件事：**能力**（`LiteralLikeNode` 带来 `text`）与**分类台阶**（`PrimaryExpression` → `Expression`）。C# 只允许一个基类，所以要选分类台阶、把能力降为接口 —— 这个判据 TS 没有写出来，移植版用一份显式清单表达：`s_capabilityBases = ["LiteralLikeNode"]`（与 `s_forcedClasses` 同形，§11.3 已有先例）。清单里的类型在 `RoleOf` 上就是接口，于是它被**实现**（`ILiteralLikeNode` 保住了 `text`）而不是被继承。

基类的选择随之改成 `TsTypeModel.BaseReferenceOf`：先看直接基里的分类台阶，没有就沿接口链上溯（`LiteralExpression` 经 `PrimaryExpression` 拿到 `Expression`）。修完的链是

```
StringLiteral → LiteralExpression → MemberExpression → LeftHandSideExpression → … → Expression → Node
```

`CallExpression` / `Identifier` / `ArrayLiteralExpression` 的链不受影响。**这份清单是维护点**：TS 增一个新的"能力基"时要补进来，否则那一族的链会静默失效 —— 它不会编译错，只会让下游的多态判断答错。

M4e 期间它又发作一次，这次的形态是**分类台阶被能力基挤掉**而不是断链：`ClassExpression extends ClassLikeDeclarationBase, PrimaryExpression`，`ClassLikeDeclarationBase` 排首位、被判成类，于是类表达式满足不了 `Expression`，`new` 之外的任何地方都用不了它 —— 而 `Utilities.IsExpression`（kind 判断）一直答 true。清单因此加了 `"ClassLikeDeclarationBase"`（`ClassDeclaration`/`ClassExpression` 都改用 `IClassLikeDeclarationBase` 接口）。

同一族在 M4e 一共出现 3 次（另两次是 `ImportExpression` 不被工厂实例化、`NonNullExpression` 不是 `MemberExpression` 的兄弟判断被写成硬转换），三者都不会编译错，所以新增 `tests/…/Model/NodeChainTests.cs`（§15.10）把"kind 谓词 ↔ 类链 ↔ 工厂产出"三方钉住：谓词认可的东西必须真是那个类，工厂必须造得出谓词认可的东西。

### 12.9 分类接口的共同锚点：`INode`

§12.4 的位置规则把一部分分类台阶留成了接口（`TypeNode`、`Statement`、`Declaration`、`JSDocContainer`…），因为它们在别处出现在非首位。于是这些"节点"在 C# 里没有共同类型：`Expression` 是类，`Statement` 是接口，而 C# 的接口继承不了类 —— 那条链到这里就断了。

这层差距在 M4c 第一次把生成物纳入编译时集中爆发：`CreateNodeArray<T> where T : Node` 收不下一个 `List<ITypeNode>`，`PropagateChildFlags(Node?)` 也收不下 `ITypeNode`。TS 里没有这个问题，因为它的 `TypeNode` 是 `Node` 的**子类型**。

修法是手写一个 `INode`（`Model/Node.cs`）：`Node` 实现它，生成器让**每一个能到达 `Node` 的接口**声明它为基（判据就是 §12.4 已有的 `ReachesNode`）。`INode` 只声明 `Kind`/`Flags`/`TransformFlags` —— 工厂对"任意节点"提出的全部要求。

边界：
- 接口因此继承 `INode` 的成员，而实现方是 `Node` 派生类，本来就有它们，没有额外负担。
- 工厂里收"任意节点"的那几个辅助（`CreateNodeArray`/`AsNodeArray`/`PropagateChild*`/`AggregateChildrenFlags`）的约束从 `Node` 放宽到 `INode`。
- `Node` 仍然是类：`Update`/`SetOriginalNode`/`CreateBase*` 的 `T : Node` 约束不变 —— 它们要访问的 `Original`/`EmitNode`/`Pos` 在类上，而不在 `INode` 里。

---

## 13. 宿主替换：`sys.ts` 的 Node 绑定（M2c，已完成）

### 13.1 交付

| 项 | 结果 |
|---|---|
| `sys.ts` 纯逻辑 | `Core/SysWatch.cs` + `Core/SysWatch.System.cs`：轮询队列、动态/固定优先级轮询、按名字复用 watcher、原生 watcher 与轮询之间的回退、递归目录 watcher、由 `IWatchOptions` 选择策略的 `createSystemWatchFunctions` |
| 宿主契约 | `Core/Sys.cs`：`ISystem`、`IModifiedTimeHost`、`HostWatchFile`/`HostWatchDirectory`、`FileWatcherCallback`/`DirectoryWatcherCallback` |
| 计时埋点 | `Core/Performance.cs`（`performance.ts` 全量） |
| 常量与纯函数 | `Core/SysCore.cs`：djb2、`getFileWatcherEventKind`、`getModifiedTime` 兜底、`sysLog`、`ignoredPaths`、`missingFileModifiedTime`、`IsSystemWatcherLimitExceeded` |
| 写文件 | `Core/Files.cs`：`ensureDirectoriesExist`、`writeFileEnsuringDirectories` |
| 拉前依赖 | `Core/Wildcard.cs`（`matchesExclude` 链路，见 §10.2） |
| 验收 | 全套单测通过，其中 21 个针对 watch 与写文件；时间与计时器全部由测试驱动（`FakePollingWatchFileHost`），不依赖真实时钟 |

### 13.2 `getNodeSystem`（`sys.ts:1468-1953`）不逐行移植

它是 Node 的绑定：`fs`/`path`/`os`/`crypto`/`inspector`/`process`/`__filename`。C# 侧由 `ISystem` 的一个实现承接（`System.IO`、`Environment`、`SHA256`、`FileSystemWatcher`），逐行翻译既没有意义也没有验收手段。逐项对照：

| `getNodeSystem` 的成员 | 承接方式 |
|---|---|
| `readFile`/`writeFile` | `File.ReadAllBytes`/`WriteAllBytes`。**BOM 处理不必重写**：TS 手写 UTF-16 BE/LE 与 UTF-8 BOM 的识别与剥离，.NET 的 `StreamReader`（`detectEncodingFromByteOrderMarks`）做的就是这件事；唯一差别是 TS 对 UTF-16 BE 采取"翻转字节对当 LE 读"，而 .NET 直接支持 BE |
| `getAccessibleFileSystemEntries` | `DirectoryInfo.EnumerateFileSystemInfos` + 排序。TS 里的 `.`/`..` 过滤与"符号链接再 stat 一次"是 Node 的怪癖，.NET 不返回 `.`/`..` |
| `readDirectory` | 依赖 `utilities.ts` 的 `matchFiles`（Layer 5）→ M5 之后再接 |
| `require` | 依赖 `resolveJSModule`（Layer 5）→ M5 之后再接；CLR 上对应 `AssemblyLoadContext`，且只在宿主插件路径（`source-map-support`）上需要 |
| `enableCPUProfiler`/`disableCPUProfiler`/`cpuProfilingEnabled` | CLR 没有等价物（那是 Node inspector 协议）。保持 `ISystem` 的默认实现（返回 `false`）；真要剖析就用 EventPipe/`dotnet-trace`，与编译器无关 |
| `realpath`/`getModifiedTime`/`setModifiedTime`/`deleteFile`/`statSync` 家族 | `File.ResolveLinkTarget`/`GetLastWriteTime`/`SetLastWriteTime`/`Delete` |
| `createHash`/`createSHA256Hash` | `SHA256.HashData`。TS 的 `createHash` 会在 Node 缺 `crypto` 时退回 djb2，CLR 不缺 |
| `isFileSystemCaseSensitive`/`swapCase` | 平台 API（`OperatingSystem.IsWindows()`）；`swapCase` 只是那个探测的实现细节 |
| `fsWatchFileWorker`/`fsWatchWorker` | `FileSystemWatcher`（配 `NotifyFilter`/`IncludeSubdirectories`），即 `HostWatchFile`/`FsWatchWorker` 的实现 |
| `getWidthOfTerminal`/`clearScreen`/`setBlocking`/`base64decode`/`base64encode`/`getMemoryUsage` | `Console.WindowWidth`、`GC.GetTotalMemory`、`Convert.ToBase64*`；终端控制序列按需内联 |

该实现（暂称 `SystemHost`）排在 **M8**：它要等 Layer 5 的 `matchFiles`/`resolveJSModule` 才能完整。在此之前，策略层（`SysWatch`）已可用 `FakeSystem` 完整驱动。

### 13.3 `patchWriteFileEnsuringDirectory` → 显式调用

TS 在 `sys` 初始化时把 `sys.writeFile` 换成"先建目录再写"的包装（猴子补丁）。C# 不能替换活动对象上的接口成员，等价改写就得写一个转发全部 `ISystem` 成员的装饰器——纯样板，而且会让"这一处写文件的行为不同"变得不可见。

移植做法：保留函数体（`Core/Files.cs` 的 `WriteFileEnsuringDirectories`），把调用点写成显式的 `Files.WriteFileEnsuringDirectories(system, path, data, bom)`。具体哪些调用点需要它，留到 M8（`executeCommandLine`/`builder`）落地时决定。

### 13.4 `sys.ts` 里三处"改写运行时结构"的等价表达

| TS 手法 | 位置 | C# 表达 |
|---|---|---|
| 给 `fsWatch` 返回的对象挂 `referenceCount` | `createUseFsEventsOnParentDirectoryWatchFile`、`createSystemWatchFunctions` | `ReferenceCountedWatcher` 包装 |
| 用 `queue[i] = undefined` 打洞、用 `queue.length = n` 收缩 | `pollWatchedFileQueue` | `List<T?>` 的 `null` 洞 + `RemoveRange` |
| `setCustomLevels(…, PollingInterval)` 直接改 enum 成员的值 | `setCustomPollingValues` | 时长存进 `Levels`，由 `PollingIntervalMilliseconds` 读；`PollingInterval` 本身保持不可变 |

### 13.5 `sys` 全局单例与 `setSys` 不保留

TS 的 `sys` 是模块级可变单例（`setSys` 负责替换它），其它模块直接 `import { sys }`。C# 里不保留这个单例：宿主由调用方显式传入（D9 已经规定 `System`/`CompilerHost` 照搬为接口），`setSys` 随之取消。好处是测试可以直接注入 `FakeSystem`，不必替换全局状态 —— `SysWatchTests` 驱动轮询用的就是这个方式。

`sysLog`/`setSysLog` 是另一回事：它是日志钩子（`curSysLog`）而非宿主，模块级可变状态照原样保留在 `SysCore` 上。

---

## 15. M4 计划：Layer 3（`Parser` + `NodeFactory`）

### 15.1 分批

M4 是约 25,600 行 TS —— Layer 2 的六倍，也远超单轮能保持质量的量，所以按文件、按依赖自底向上切：

| 批次 | 内容 | 行数 | 状态 |
|---|---|---|---|
| M4a | `factory/nodeTests.ts` → `Parser/NodeTests.cs`（生成） | 1,201 | 已完成（§15.4） |
| M4b | `factory/emitNode.ts` → `Parser/EmitNodes.cs`；`factory/nodeChildren.ts` → `Parser/NodeChildren.cs`；提前移植 `getSourceFileOfNode`/`isParseTreeNode`/`getParseTreeNode` | 442 + 3 个函数 | 已完成（§15.5） |
| M4c | `factory/nodeFactory.ts` → `Parser/NodeFactory.cs`（最大单件，再按 create/update 家族细分） | 7,542 | 已完成（§15.8：生成物已纳入编译，428 个成员里 417 个已移植，余 11 个逐条指名等 M4d/M5/M7/M4e） |
| M4d | `factory/utilities.ts` + `nodeConverters.ts` + `parenthesizerRules.ts`，以及 `nodeFactory.ts` 里生成器表达不了的那半 | 2,656 | **已完成**（§15.9：三个文件全部移植；工厂尾部补齐，生成的 11 个存根清零；残留逐条指名） |
| M4e | `parser.ts` → `Parser/*`（递归下降主体：声明 / 语句 / 表达式 / 类型 / JSDoc 各自成批） | 10,823 | **已完成**（§15.10：九个 partial 文件、JSDoc 与增量重解析都在；与真编译器的差分验证逐字节一致；集成期抓出并修掉 6 个真 bug） |
| M4f | `emitHelpers.ts` + `expressionToTypeNode.ts` | 2,821 | **一半已完成**：`emitHelpers.ts` 以 §15.14 落地（helper 的 name/importName/scoped/priority/text/dependencies 全量复刻 + 补上 `GetEmitJsx`）。`expressionToTypeNode.ts` 仍等 M6 —— 它的唯一入口 `createSyntacticTypeNodeBuilder(options, resolver)` 需要 checker 侧的 resolver，其余前置（选项模型、JSDoc 家族、`findAncestor`/`forEachReturnStatement`/`getFunctionFlags`/`hasDynamicName`/`introducesNewScope`/`getEffective*tNode`）都已随 M5 与 M4f 前置到位 |
| M4g | baseline 外环。**先做 §7 的 P1a**（解析差分，工具 `tools/AstDiff`，对 `tests/cases/**` 全量跑），P1b 的 `.errors.txt`/`.js` 等发射器落地后再做 | — | P1a **已完成**（§15.11：19,585 个用例逐字节一致；期间修掉 7 个缺陷） |

顺序的理由：`nodeFactory` 的 459 个 import 里有 47 个来自 `nodeTests`、8 个来自 `emitNode`，而 `nodeTests` 只依赖 `types.ts`（227 个函数里 228 个 import 中的 228 个）—— 先把最底层的表与槽位立起来，往上的每批就只依赖已经存在的东西。

### 15.2 两处不逐行移植

- **`factory/baseNodeFactory.ts` 省略**：它是 `objectAllocator` 之上的间接层，存在的理由是让每个编译实例拿到**自己**的一套 `Node` 构造函数（跨 realm 时 `instanceof` 才正确，TS 因此把构造函数做成可注入的）。C# 里 `new Identifier()` 就是确定构造，没有对应问题。节点的 `pos = -1` / `end = -1` 初始范围改由 `Node` 自身承担。
- **`factory/nodeChildren.ts` 的 `WeakMap` 双层缓存**：TS 用它做增量解析的 "每个 source file 一张 node → children 表"，键不阻碍回收。C# 用 `ConditionalWeakTable<SourceFileLike, …>` 表达同一生命周期，其余语义按 TS 保留。

### 15.3 M4 期间复核出的实测偏差

§3 的那批行数来自早期统计，逐文件复核后发现**系统性偏低**（Layer 2 的 `scanner.ts` 是唯一核对过的）。M4 涉及的 11 个文件全部重新实测并已回写 §3；另有三处数字要改：

- `parser.ts` 9,775 → **10,823**
- `factory/nodeFactory.ts` 6,848 → **7,542**
- `tests/cases/conformance` 5,908 → **5,695**；`tests/baselines/reference` 53,026 → **60,931**

### 15.4 M4a 交付

`Parser/NodeTests.cs`（生成，227 个谓词）。源文件的 227 个导出函数**全部**是
`return node.kind === SyntaxKind.X;`（唯一例外是 `isModuleExportName`，它认 `Identifier` 与 `StringLiteral`
两个 kind），所以这是表不是码，按 §9 第 5 条并入生成器而不手抄：`extract.cjs` 的 `collectPredicates` 遇到
别的 body 形态直接抛错 —— 正是这条规则在首次运行时就抓出了 `isModuleExportName`。

**谓词保留 kind 比对，没有写成 `node is NumericLiteral`**：若干谓词测的 kind 在 C# 里共享一个类
（所有关键字都是 `KeywordToken`，而 `isExportModifier` 测的是 `ExportKeyword`），类型测试并不等价。
这一层同样适用于后续的 `update*` 家族 —— 它们要按 kind 判断能否复用节点。

### 15.5 M4b 交付，以及 M4 期间修掉的两个生成器 bug

交付：`Parser/EmitNodes.cs`（`emitNode.ts` 的 40 个导出 + 手写的 `EmitNode` 实现类）、`Parser/NodeChildren.cs`（5 个函数）、`Utilities/Utilities.cs`（`utilitiesPublic.ts` 的 parse-tree 谓词 + `getSourceFileOfNode`，作为 Layer 5 的前瞻部分），以及支撑类型 `SourceMapRange`、`SynthesizedComment`、`ModeAwareCache<T>`。

`ModeAwareCache<T>` 是 `moduleNameResolver.ts` 的类型（M8 的领域），本身不该在这里出现 —— 是下面第一个 bug 修好之后才被 `IProgram` 引用到的。已在文件头记明，M8 要回头核一遍。

**bug 1：同名接口的声明合并没有处理。** `types.ts` 里 `SourceFile` 声明了两次（L4320 `extends Declaration, LocalsContainer` 与 L4480 `extends ReadonlyPragmaContext {}`），提取器把两条都收进模型，生成器按名字建字典，后者覆盖前者 —— 于是 `ISourceFile` 的基接口只剩 `IReadonlyPragmaContext`，`SourceFile` 也因为没有了可达 `Node` 的 `extends` 链而**没被分类成类**。受影响的名字有三个：`SourceFile`、`Program`、`TypeNode`。修法是让提取器按 TS 的合并语义折叠（`extends` 取并集、成员按名去重、行号取范围），接口数 537 → 534、类数 235 → 237。

**bug 2：`Node` 的成员排除表是全局按名字生效的。** `s_nodeMembers`（`kind`/`flags`/`pos`/`end`/`parent`…）原本对**所有**接口和类都生效，但它描述的是"这些成员已经在手写的 `Node` 基类上"，只对 `Node` 派生者成立。于是 `IEmitNode.Flags`（`EmitFlags`，与 `Node.Flags` 毫无关系）被吃掉了，`ICommentRange` 也拿不到 `Kind`/`Pos`/`End`（它们来自 `CommentRange extends TextRange`，不是来自 `Node`）。修法是让排除以"该类型能否到达 `Node`"为前提，`kind` 的排除同样以此为前提。

两个 bug 是同一类错误：**排除规则按名字匹配，却没有限定它适用的类型范围**。

修完之后的附带变化：`ITextRange` 现在带 `Pos`/`End`，因此 `Node` 需要**同时**实现 `ITextRange` 与 `IReadonlyTextRange` —— TS 靠结构类型让一个节点充当 `TextRange`（`getSourceMapRange` 的回退分支就是这么写的），C# 没有这个能力。

`SyntaxList._children` 是第三个被迫处理的成员。它一度让下划线规则开了个按类型判定的口子（brand 成员的声明类型字面就是 `undefined`，而 `_children` 是 `readonly Node[]`），但更好的做法是**根本不改生成器**：生成物全部改成 `partial` 之后，`_children` 由 `Model/SyntaxList.cs` 补上（§9 第 8 条），下划线规则恢复成一句"跳过所有下划线成员"。

### 15.6 M4c 的前置：接口方法签名（bug 3）

提取器原本只收属性签名，方法签名整批丢弃，理由写在注释里——"TS 的接口方法在 port 里按需用委托表达"。M4c 一开始就撞上它：`NodeFactory` 有 **520 个方法**（`createIdentifier(name: string): Identifier`），加上 `TypeChecker` 181、`Program` 62、`EmitResolver` 40、`ParenthesizerRules` 31、`CompilerHost` 27，共 **1,091 个方法**分布在 **40 个接口**上。缺了它们，生成的接口没有任何实现能拿来自检——M4c 的整批工作都会失去对照。

现在 `isMethodSignature` 与属性一起收，参数、返回类型、类型参数（含 `extends` 约束）都进模型。随之必须处理的四件事：

1. **C# 只能按参数重载，TS 允许只靠返回类型区分。** `createToken` 有 20 多个声明只在返回类型上不同，这在 C# 里是 CS0111。同签名的成员被合并，返回类型取候选的**共同基类**（`TsTypeModel.CommonBase`）——调用方能依赖的那个类型。判重键用*映射后*的参数类型并去掉可空注解：`SyntaxKind.EndOfFileToken` 与 `SyntaxKind.Unknown` 是两种 TS 类型却都映射到 `SyntaxKind`，而 C# 的重载解析本来就忽略 nullable。
2. **`ConvertGeneric` 的兜底分支用了 TS 原名**，泛型路径因此不带 `I` 前缀：`KeywordTypeNode<TKind>` 生成了定义，引用处却写成未加前缀的名字。兜底改走 `TypeName`。
3. **泛型参数不参与映射**：`createNodeArray<T extends Node>` 的 `T` 掉成了 `object`，参数变成 `List<object?>`。`MapMember` 现在带着当前成员的类型参数集合（映射是递归且单线程的，所以按调用设置、`finally` 还原）。约束只在能映射成具体类型时才保留（`T extends Node` 保留，映射成 `object` 的丢弃）。
4. **补两个模型外的泛型映射**：`MultiMap<K, V>` → `Dictionary<K, List<V>>`（`core.ts` 的实现就是 `Map<K, V[]>`）、`IterableIterator<T>` → `IEnumerable<T>`（返回 `IEnumerator` 会丢掉 TS 迭代器可重复枚举的性质）。另外 `RoleOf` 现在把 `s_forcedClasses` 视为类，`NodeArray`/`ModeAwareCache` 这类手写类型不会再被加 `I` 前缀。

参数名撞上 C# 关键字（`createTypeOperatorNode(operator, type)`）时加 `@` 转义，而不是改名。

### 15.7 M4c 的实现方式：固定形态交给生成器

`factory/nodeFactory.ts` 有 488 个函数体，按形态分类：

| 形态 | 数量 |
|---|---|
| `create*`：`const node = createBase*<T>(kind)` + 字段赋值 + `return node` | 157 |
| `update*`：单条 `return node.a !== a \|\| … ? update(createX(…), node) : node` | 141 |
| `create*`/`update*` 带真逻辑（if/switch/回退） | 56 |
| 辅助（`as*`、`update`、`createNodeArray`、非节点方法与模块级函数） | 134 |

那 298 个（84% 的 create/update）形态固定，因此与谓词表、数据表、typed enum 同样处理：**由 `tools/Gen` 生成到 `Parser/NodeFactory.Generated.cs`**（`partial class NodeFactory`，与手写的 `Parser/NodeFactory.cs` 共存，§9 第 8 条）。形态不符时报错退出，不猜。

代价与边界（实测，见下）：

- 生成需要**表达式翻译**。赋值右侧的形态分布（按出现次数）：参数名 301（其中含 `undefined` —— 它在 TS 的 AST 里也是标识符）、位运算 59、`node.x` 47、`asNodeArray` 32、`createNodeArray` 24、`asName` 22、`propagateChild(ren)Flags` 16、`~x` 11、`asEmbeddedStatement` 8、`parenthesizerRules().parenthesize*` 约 25、其余零星。
- 语句形态：`=` 477、`|=` 86、`&=` 11、`setTextRange(node, …)` 2。
- `update` 的比较链共 393 处 `node.x !== x`；转发是 `update(createX(…), node)`，实参 399 个是参数名、15 个是 `node.x`。
- **`parenthesizerRules()` 那约 25 处依赖 M4d**（`parenthesizerRules.ts` 是 M4d 的交付）。生成物照 TS 写 `Parenthesizer.Xxx(…)`，`Parenthesizer` 属性由 M4d 提供；在那之前这些方法只保证编译，不保证能跑。

**第一批（已交付）**是其余一切的底座：`CreateNodeArray`、`AsNodeArray`、transformFlags 聚合的四个函数（`AggregateChildrenFlags`/`PropagateChildFlags`/`PropagatePropertyNameFlagsOfChild`/`GetTransformFlagsSubtreeExclusions`）、`NodeFactoryFlags`，以及 `Utilities` 里的 `setTextRange`/`isNamedDeclaration`/`isPropertyName`。`NodeFactory` 暂不声明实现 `INodeFactory`：接口有 503 个抽象成员，分批期间编译器分不清"漏了"与"还没写"，全部就位后再挂上它做逐成员自检。

### 15.8 M4c：生成物纳入编译，手写半与存根机制

转折点是**去掉 `Parser/NodeFactory.Generated.cs` 的 `<Compile Remove>`**（§15.7）。代价是先把"生成物自己过不了编译"的问题修干净 —— 它们全是映射规则问题，不是个别成员问题：

| 症状 | 修法 |
|---|---|
| `List<ITypeNode>` 进不了 `CreateNodeArray<T> where T : Node` | 手写 `INode`，分类接口继承它，辅助与数组的约束改收 `INode`（§12.9） |
| `a && b`、`x ? y : z`、`if (x)`、`!x` 直接落成 C# 运算符 | 条件位置走 `Js.IsTruthy`；值位置的 `a && b` 译成 `a is null ? null : b`（`nodeFactory.ts` 里它总是守着可选节点）；`!` 先求真值 |
| `isIdentifier(…)`、`setTextRange(…)` 裸调用 | 谓词加 `NodeTests.`，辅助加 `Utilities.`/`Core.`；方法组引用（`sameFlatMap(x, flattenCommaElements)`）同样处理 |
| `createBaseNode<NonNullChain>` 找不到类型 | 类型参数改按 kind 反查实际类；kind 别名（`AssertClause = ImportAttributes`）按**值**命中 |
| `SyntaxKind.A \| SyntaxKind.B` 参数退成 `object?` | 全枚举成员的联合收敛成 `SyntaxKind`，也不再为它们生成窄化重载 |
| `isExportEquals: boolean \| undefined` 退成 `bool`（丢可空） | `A \| undefined` 在必填位置也可空 —— 可空是类型的事实，不是参数可选性的副产品 |
| `LiteralTypeNode["literal"]` 等索引访问类型未解析 | 兜底查模型里该成员的声明类型 |
| 泛型类的成员 `NodeArray<T>` 退成 `NodeArray<object?>` | 类的类型参数进入成员映射的作用域 |

**存根机制**：生成器看不到手写的那半个 partial class，所以它需要一份"哪些成员已经有主"的清单 —— `NodeFactoryEmitter.s_implemented`。不在清单里的成员，生成器要么按定形生成，要么出一个**签名来自同一套映射、body 直接 `throw`** 的存根；`update*` 若它转发的 `create*` 还不可用，自己也会变成存根（否则 `Update<T>(T, T?)` 的 `where T : Node` 会收到 `object?`）。**实现一个成员 = 把它的名字加进 `s_implemented` + 重跑生成器**，存根随之消失。

代价是生成物里有一段明确会抛的代码。这是临时的：清单每长一条就短一条，全部就位后生成物里不再有 `NotImplementedException`。

**进度**（第十八批结束时）：428 个工厂成员中，**11 个仍是存根，且全部是跨批依赖**——本层能做的已经做完。

已手写的部分覆盖：字面量与模板、标识符与 punctuation、parameter/property/method 元素、signature 家族、函数表达式与箭头函数、函数与类声明、heritage clause、表达式家族、类型节点家族、variable / import / export / module / source file 声明、emit helper 家族、JSDoc 全族、JSX 与 property assignment、outer expression 与 use-strict prologue，以及一批辅助。

剩下的 11 个，每一个都指名卡在哪：

| 成员 | 卡在 |
|---|---|
| `createBinaryExpression` / `updateBinaryExpression` | `containsObjectRestOrSpread` 要 `forEachChild`（`parser.ts`，**M4e**） |
| `createTypeCheck` / `createIsNotTypeCheck` | 经 `factory.createStrictEquality` 落到上面那条 |
| `createTempVariable` / `createLoopVariable` / `createUniqueName` / `createBaseGeneratedIdentifier` / `createBaseGeneratedPrivateIdentifier` / `createUniquePrivateName` | 参数是 `GeneratedNamePart`（transformers 的类型，**M7**），且要 `formatGeneratedName`（`factory/utilities.ts`，**M4d**） |
| `createCallBinding` | 要 `createTempVariable` 与 `skipOuterExpressions`（`utilities.ts`，**M5**） |

**取舍写在这里**：能在本层做完的一件不留，做不了的一个不蒙 —— 每一个都在上表里指名它等的是哪一批。这些存根会抛，而不是返回半成品。

**存根清单有契约测试守着**：`NodeFactoryStubTests` 直接读生成物，断言存根恰好是上面那 11 个，并且 `NotImplementedException` 在生成物里只出现在这 11 处 —— 实现了成员却忘了往 `s_implemented` 登记、或生成器开始给新成员出存根，都会在这里失败。每减少一个存根，这份清单要同步。

**关于接口自检（§15.7 说"全部就位后再挂上 `INodeFactory`"）**：试过了，它同样是跨批的。接口的 503 个成员伸出了这个文件——`createAdd`/`createBitwiseAnd`/`createAssignment`、`CloneNode` 与三个 prologue copier 由 `factory/utilities.ts` 声明（M4d），`baseFactory`/`converters` 属于根本不移植的 `baseNodeFactory.ts`（§15.2）。今天挂上它是 284 个错误（272 个成员未实现 + 12 个签名不匹配），这些错误不说明移植面的任何问题，只说明接口比 M4c 大。这一步归 **M4d**：等那些成员到位，并且生成器能给永远不会存在的两个留出余地。

**针对性单测**（判据 4）：`NodeFactoryFamilyTests` 覆盖手写半里那些"需要决定"而不是"照抄"的行为 —— 模板的 cooked 文本、条件表达式补标点 token、`++x` 只对真标识符置 `ContainsUpdateExpressionForIdentifier`、`update*` 在没变时返回同一实例、source file 的初值与克隆式更新、heritage clause 的 kind → flags、类声明的 ambient 分支、JSDoc 的默认 tag 名与两个布尔位、typedef 从 fullName 派生 name、`use strict` 序言的新行标志。

**测试抓到的两处真 bug**：

1. `NullParenthesizerRules` 的列表成员一律新建 `NodeArray`，把 `createNodeArray` 刚刚设好的文本范围与 `HasTrailingComma` 丢掉了 —— `createArrayLiteralExpression` 的尾随省略元素正是靠那个标志（`[1, 2, ,]` 与 `[1, 2, ]` 的区别）。修法是"入参已经是 `NodeArray` 就原样交回"，四个列表成员一起改。这与 §14.3 记的两处 Scanner bug 同属一类：**规格化/包装函数悄悄改写了入参的身份**。
2. keyword 类型节点在 TS 里**没有自己的对象**：`createToken(SyntaxKind.NumberKeyword)` 的静态类型是 `KeywordTypeNode<TKind>`，靠结构兼容 —— 同一个 token 既能当 token、又能当类型节点。C# 的 `Token` 没实现 `IKeywordTypeNode`，于是 `CreateKeywordTypeNode` 出来的东西一放进 `List<ITypeNode>` 就 `InvalidCastException`。修法是新增 `KeywordTypeToken : Token, IKeywordTypeNode<SyntaxKind>`（`Model/Token.cs`），并让 `CreateTokenNode` 对 `KeywordTypeSyntaxKind` 的那 12 个 kind 建它。**不能**把 `ITypeNode` 直接挂在 `KeywordToken` 上：那会让 `export`/`const` 也变成类型节点，而 `ModifierToken` 还是它的子类。

---

## 15.9 M4d：工厂的三个文件与工厂尾部

### 交付

| 项 | 结果 |
|---|---|
| `factory/utilities.ts`（1,757 行） | `Parser/FactoryUtilities.cs`（`internal static partial class FactoryUtilities`：绑定/赋值元素分析、JSX 表达式、生成名格式化、外层表达式、prologue 谓词、运算符分类、二元表达式蹦床） |
| `factory/parenthesizerRules.ts`（706 行） | `Parser/ParenthesizerRules.cs`：`IParenthesizerRules` 的 31 个成员全部实现，构造时接一个 `NodeFactory` |
| `factory/nodeConverters.ts`（193 行） | `Parser/NodeConverters.cs` + `NullNodeConverters`（`nullNodeConverters` 的移植：九个成员都抛，`NodeFactoryFlags.NoNodeConverters` 选它） |
| 工厂尾部（`nodeFactory.ts` 的 `get createX()` 与真逻辑成员） | 生成名 8 个、运算符快捷 30 个、JSDoc tag 绑定 34 个、`cloneNode` 与四个专用克隆、prologue 四个、`mergeLexicalEnvironment`/`liftToBlock`/`inlineExpressions`、`restoreOuterExpressions`/`restoreEnclosingLabel`、`replaceModifiers`/`replaceDecoratorsAndModifiers`/`replacePropertyName`、`createBinaryExpression`/`updateBinaryExpression`/`createTypeCheck`/`createIsNotTypeCheck`/`createCallBinding` |
| 生成物存根 | **11 → 0**。`NodeFactoryEmitter.s_implemented` 增收 100 个名字后，生成物里不再有 `NotImplementedException`，`NodeFactoryStubTests` 随之退役（改为 `NodeFactoryInterfaceTests`，见下） |
| 生成器 | `utilities.ts` 的两个 `const enum`（`Associativity`、`OperatorPrecedence`）按名字抽取（`enumSources`，与 `tableSources` 同形的机制），`Specs` 里给它们显式声明 `Flags: false` —— 默认的"值是独立位"启发式会把 `Associativity` 的 0/1 判成标志位。另外 `NodeFactoryEmitter` 现在会按**赋值目标槽的映射类型**给 `asName` 补一次窄化转换（真 bug 3）；槽的类型从模型的接口链上查，查不到就不加转换 |
| Layer 5 前瞻切片（`Utilities/`） | 表达式分类与优先级（`GetExpressionPrecedence`/`GetOperatorPrecedence`/`GetBinaryOperatorPrecedence`/`GetExpression-`+`GetOperatorAssociativity`）、`GetLeftmostExpression`、`SkipParentheses`/`SkipPartiallyEmittedExpressions`、`IsOptionalChain`、`IsNodeArray`、`IsLiteralKind`/`IsLiteralExpression`、`IsLeftHandSideExpression`/`IsUnaryExpression`/`IsExpression`、绑定模式谓词、语句谓词（`IsStatementOrBlock` 与两个 kind 表）、`GetNodeId`（声明在 `checker.ts`，工厂先要它）、`GetEmitFlags`、`IsGeneratedPrivateIdentifier`、`IsAssignmentExpression`/`IsAssignmentOperator`、`IsLogicalOrCoalescingAssignmentOperator`、`IsPrologueDirective`/`IsCustomPrologue`/`IsHoistedFunction`/`IsHoistedVariableStatement`、`GetOriginalNode`、`SetParent` |
| 支撑类型 | `Parser/AutoGenerateInfo.cs`、`Parser/CallBinding.cs`、`Parser/GeneratedNamePart.cs` —— `types.ts` 里靠对象字面量填的接口，C# 需要一个可构造类型（与 `LineAndCharacter`、`TextRange` 同样的处理） |

### `NullParenthesizerRules` 退回它的原职

M4c 期间每个工厂都拿它当 `Parenthesizer`（工厂里约 25 处 `parenthesizerRules().…` 因此都能编译、都不能跑）。现在 `NodeFactory.Parenthesizer` 是懒建的 `ParenthesizerRules`，只有 `NodeFactoryFlags.NoParenthesizerRules` 才落到 `NullParenthesizerRules` —— 与 TS 同一个开关、同一个对象。`Converters` 同理。

### 三处真 bug

1. **工厂节点的文本范围是 0 而不是 -1。** TS 的 `baseNodeFactory.ts` 把每个节点建成 `new Node(kind, /*pos*/ -1, /*end*/ -1)`，"工厂造的节点在解析器给它范围之前是 synthesized" 就来自这里；移植版把这条不变量交给了 `Node` 自己（§15.2），但 `Node.Pos`/`End` 是普通自动属性、默认 0，于是 `CreateBaseNode<T>` 造出来的节点**从不是** synthesized：`nodeIsSynthesized` 一路答 false，parenthesizer 的 `isIgnorableParen`、`cloneNode` 的范围重置与 M7 的 source map 全都跟着偏。修法是 `Node` 上的初值 `= -1`（`NodeArray` 同样），并由 `NodeFactoryRangeTests` 守住。
2. **`Utilities.IsLocalName` 的 body 是错的。** TS 里 `isLocalName` 只有一处（`factory/utilities.ts:565`），判的是 `(getEmitFlags(node) & EmitFlags.LocalName) !== 0`；移植版名下写着 `utilities.ts:565`（那里其实是一个 import 列表），body 判的却是 `escapedText is "arguments" or "this"` —— 一个在原源码里不存在的函数。后果落在 `CreatePrefixUnary-`/`CreatePostfixUnaryExpression` 的 `ContainsUpdateExpressionForIdentifier` 上：`++arguments` 少一个 flag、带 `LocalName` 位的名字多一个 flag，而 module 转换器正是靠这个 flag 决定要不要把 `x++` 改写成 `exports.x++`。现在它是 `FactoryUtilities.IsLocalName`（正确 body），两个调用点随之改旗，`FactoryUtilitiesTests` 与 `NodeFactoryTailTests` 各留一条回归。
3. **`AsName` 把名字收窄成了 `Identifier`。** TS 的 `asName` 是 `typeof name === "string" ? createIdentifier(name) : name` —— **节点原样传回**，因为这一个 helper 同时服务 `Identifier`、`PropertyName`、`MemberName`、`BindingName`、`EntityName` 五种槽。移植版写的是 `name as Identifier`，于是 `createVariableDeclaration({a}, …)` 的 `Name` 变成 `null`（绑定模式整个丢掉）、`createPropertyDeclaration("#p", …)` 丢掉私有名、`createParameterDeclaration([a], …)` 丢掉参数绑定模式 —— 而 M4e 的解析器正是大批量造这些节点的地方，所以这条必须在解析器落地前修掉。修法是 `AsName(object?) → Node?`（节点原样传回）；真正**需要** `Identifier` 的槽（`ImportEqualsDeclaration.name`、三个函数/类声明的 `name`）在生成的创建体里显式转换：映射器知道每个槽映射成 C# 的什么类型，`NodeFactoryEmitter` 因此在 `node.X = asName(...)` 上按目标类型加一次窄化转换 —— 这正对应 TS 靠类型系统完成的收窄，而绑定模式/私有名所在的槽（`Node?`/`object?`）保持原样。回归见 `FactoryUtilitiesTests` 的两条 `IsLocalName` 与 `CreateAccessorPropertyBackingField`，以及 `NodeConvertersTests` 里走绑定元素的那批。

### 残留（指名，不留半成品）

| 项 | 卡在 | 归属 |
|---|---|---|
| `INodeFactory` 的 6 个 `getName` 家族成员（`GetInternalName`/`GetLocalName`/`GetExportName`/`GetDeclarationName`/`GetNamespaceMemberName`/`GetExternalModuleOrNamespaceExportName`） | `getName` → `getNameOfDeclaration` → `getNonAssignedNameOfDeclaration`，其中有 JS 文件专用的 `exports.x =` 分析（`getAssignmentDeclarationKind` 及其十余个辅助，全在 `utilities.ts`） | M5 |
| `FactoryUtilities.IsJSDocTypeAssertion` | `getJSDocTypeTag` → `getJSDocCommentsAndTags` 家族（JSDoc 归属与父链遍历）。`isOuterExpression` 的 `ExcludeJSDocTypeAssertion` 分支引用它 —— 目前**抛**，因为静默答"不是 JSDoc 断言"会让 `skipOuterExpressions` 跳过多余的括号 | M5 |
| `factory/utilities.ts` 的 `createExpressionForAccessorDeclaration`、`createExpressionForObjectLiteralElementLike` | `getAllAccessorDeclarations` | M5 |
| `createExternalHelpersImportDeclarationIfNeeded`、`getLocalNameForExternalImport`、`getExternalModuleNameLiteral`、`tryGetModuleNameFromFile` 等 | `CompilerOptions`/`EmitHost`/`EmitResolver`/模块种类 | M5/M8 |
| `INodeFactory` 挂到 `NodeFactory` 上做逐成员自检 | 上面第一行 + 一条生成器规则：`CreateNodeArray<T>` 的约束在移植版是 `INode`（§12.9），接口里是 `Node`，C# 不接受"实现方更宽" | M5 |

`NodeFactoryInterfaceTests` 把这份清单变成了断言：它用反射比对 `INodeFactory` 的成员与工厂实际提供的成员，差集必须**恰好**是上表前两行那 7 个名字（6 个 name 家族 + 永不存在的 `BaseFactory`），同时断言生成物里没有 `NotImplementedException`。

### 取舍

`replaceModifiers` 的 `modifiers` 槽在生成物里类型不一致：类型参数、属性签名、方法签名、构造器类型、函数表达式与箭头函数收 `List<ModifierToken>?`，其余收 `List<Node>?`。TS 靠数组协变把同一个数组交给两者，C# 的 `List<T>` 不变，所以从 `ModifierFlags` 建出来的数组要多投影一次。走接口声明的两个重载时调用方给的是 `List<ModifierToken>`，因此"原样传回同一个数组"这条身份在类型不同的那一半目标上本来也到不了 —— 投影不会改变任何可达调用方的行为，`PORT-NOTE` 记在方法上。

`cloneNode` 的等价物是 `MemberwiseClone` + 重置（`Node.ShallowClone`）：TS 把源节点的 own 属性拷到一个**裸**节点上，于是范围、`id`、`parent`、`modifierFlagsCache` 保留新节点的值，其余（含 `emitNode` 与 binder/checker 留下的链接）都跟着源走；C# 没有 prototype/own 之分，逐个字段拷贝 + 显式重置那五个槽正是同一件事。`emitNode` 是复用引用，随后 `setOriginalNode` 的合并对它是自合并 —— TS 的同一段代码也是这么做的（`clone.emitNode` 先被拷进来，再被 `mergeEmitNode` 合到它自己上）。

`createAssignment` 的返回类型是 `BinaryExpression`，不是 TS 的 `AssignmentExpression<EqualsToken>`：`types.ts` 那个接口在移植版里生成了一个**抽象类**（没有任何具体子类继承它），TS 靠结构类型让每个 `x = y` 都满足它，C# 没有这层结构兼容，所以工厂造出来的对象就是 `BinaryExpression`，把它 cast 到抽象类型会抛。`INodeFactory` 里那条 `AssignmentExpression<PunctuationToken>` 签名因此属于"接口比工厂大"的那一类（§15.7 开头那段的同一种情况），`PORT-NOTE` 记在方法上。

---

## 15.10 M4e：`parser.ts` 全量移植

### 交付

`parser.ts`（10,823 行）落到 9 个 partial 文件里，按 TS 注释里的分节切：

| 文件 | 行数 | 对应 `parser.ts` |
|---|---|---|
| `Parser/Parser.cs` | 654 | 状态与入口（`initializeState` 折叠进构造函数）、上下文标志族、`ParseErrorAt*`、token 族（含 `ReScan*`/`ScanJsx*`）、`SpeculationHelper`/`LookAhead`/`TryParse`、`WithJSDoc` |
| `Parser/Parser.Entry.cs` | 992 | `CreateSourceFile` ×2、`ParseJsonText`、`UpdateSourceFile`、模块判定（`IsExternalModule`/`IsFileProbablyExternalModule` 及其 6 个辅助）、pragma 处理（`ProcessCommentPragmas`/`ExtractPragmas`）、`ReparseTopLevelAwait`、`IsDeclarationFileName`/`GetDeclarationFileExtension`/`ParseResolutionMode` |
| `Parser/Parser.NodeCreation.cs` | 734 | `CreateNodeArray`/`FinishNode`/`CreateMissingNode`、identifier 家族、可选 token 与分号族、上下文修饰符辅助 |
| `Parser/Parser.Lists.cs` | 1,300 | 列表机制（`ParseList`/`ParseDelimitedList`/`ParseBracketedList` 与复用）、`ParseEntityName`、模板与字面量片段族 |
| `Parser/Parser.Statements.cs` | 999 | 语句产生式、`IsStartOfStatement`/`IsDeclaration`、`ParseDeclarationWorker` |
| `Parser/Parser.Declarations.cs` | 1,651 | 声明产生式（变量/函数/类/接口/类型别名/枚举/模块/import/export）、`ParseModifiers` |
| `Parser/Parser.Expressions.cs` | 2,472 | 表达式产生式（含箭头函数的括号化推测）、JSX |
| `Parser/Parser.Types.cs` | 1,693 | 类型产生式、参数与签名成员 |
| `Parser/JSDocParser.cs` | 1,818 | 嵌套的 `internal static class JSDocParser`（TS 的 `namespace JSDocParser`）与 `CommentParser` |
| `Parser/IncrementalParser.cs` | 1,004 | `ISyntaxCursor`、`CreateSyntaxCursor`、`UpdateSourceFile`、`MarkAsIntersectingIncrementalChange` |
| `Parser/ForEachChildTable.cs` | 3,957 | 生成物：`forEachChild` 的 175 个 kind 分派 |
| `Utilities/Utilities.Parser.cs` | 768 | `utilities.ts` 里解析器依赖的那批（`CanHaveModifiers`、`SupportedDeclarationExtensions` 等） |
| `Parser/ParserEnums.cs` | 107 | `ParsingContext`（**顺序**枚举，掩码是 `1 << (int)ctx`）、`SpeculationKind`、`ParserSignatureFlags`、`Tristate` |

配套新增：`Core/Pragmas.cs`、`Core/SourceFileReferences.cs`、`Core/TextSpan.cs`、`Core/TextChangeRange.cs`、`Parser/CreateSourceFileOptions.cs`、`Parser/JsDocWithDiagnostics.cs`、`Model/JSDocArray.cs`、`Model/NodeArray.cs` 的 `INodeArray<out T>` 协变视图。

### parser 是实例类，不是静态状态

TS 用 `namespace Parser` + 模块级 `var`，靠 `initializeState` 每次重置。移植版是**每个源文件一个 `Parser` 实例**：`initializeState` 折进构造函数，`ClearState()` 只做引用释放。与 `Scanner` 同一处理（§14.2），并行测试因此安全 —— `_notParenthesizedArrow` 这类状态不会跨次解析泄漏，`Parser.ParseSourceFile`/`ParseJsonText`/`ParseIsolatedEntityName` 与 `IncrementalParser.UpdateSourceFile` 每个入口都新建实例。

`objectAllocator`/`baseNodeFactory`/节点计数不移植（§15.2）：`SourceFile.NodeCount` 记 **0**（`PORT-NOTE` 指明），`identifierCount` 是解析器侧的真值，照常精确。

### 与真编译器的差分验证（本批最强证据）

`artifacts/ts-ast-dump.cjs`（用 `lib/typescript.js`）与一次性端口侧导出对同一份源文本各打印一份规范化 AST —— 每个节点的 **kind 数值、pos、end、NodeFlags、标识符与字面量文本**，外加每条解析诊断的 **code/start/length** —— 然后逐字节 diff：

| 语料 | dump 行数 | 结果 |
|---|---|---|
| 10 个手写片段（变量/函数/类/类型与枚举与命名空间/模块/表达式与类表达式与动态 import/JSDoc/JSX/JSON/故意报错） | 各 11–108 | 全部一致 |
| `src/compiler/`：`binder.ts`、`scanner.ts`、`nodeFactory.ts`、`parser.ts`、`utilities.ts`、`checker.ts` | 17,877 / 22,526 / 37,864 / 50,766 / 54,317 / 298,061 | 全部一致 |
| `lib/typescript.d.ts`（声明文件与 pragma 路径）、两个 `.tsx`、一个 `.js` | 48,389 / 71 / 101 / 11 | 全部一致 |

`checker.ts` 一份就是 29.8 万行 dump —— 这同时压过了条件类型、JSDoc、装饰器、修饰符与声明合并的语法面。两侧遍历都用 `forEachChild`（端口是生成表，TS 是真实现），所以**子槽集合**也在比较范围内。

### 集成期修掉的 6 个真 bug

前四个都是「编译通过、测试通过、行为错」，第 3、4 个更是**真崩溃**（`InvalidCastException`），只有差分验证能抓到：

1. **每个文件都被当成声明文件。** `IsDeclarationFileName` 把 `GetDeclarationFileExtension` 的结果按 `is not null` 判空，而 `Paths.GetAnyExtensionFromPath` 在移植版里对"没有该扩展名"答 `""`（TS 是 `undefined`）—— 空串非 null，于是 `test.ts` 也是声明文件，全文件节点成了 ambient、隐式导出、不做函数体检查。TS 里这个 helper 只有一份（`parser.ts:10561`），但 `path.ts` 会 import 它（`changeFullExtension`），C# 表达不了这条上行边，所以唯一实现留在 `Core/Paths.cs`，解析器委托过去。
2. **两个 JSDoc 工厂成员丢槽。** `NodeFactory.SetJSDocType` 少 `JSDocNullableType`/`JSDocNonNullableType`（`?T`/`!T`/`T?`/`T!` 的操作数静默变 null）；`SetJSDocTypeExpression` 少 `JSDocThrowsTag`（`@throws {T}` 丢类型表达式）。两者都是「TS 用结构类型表达的槽，移植版必须写成 switch」，各补一个 case 加一条回归。
3. **`ParseMemberExpressionOrHigher` 的硬转换。** TS 声明它返回 `MemberExpression`，但它内部那个循环的 `!` 分支会把输入包成 `NonNullExpression` —— 在 TS 的类型图里那是 `MemberExpression` 的**兄弟**（都继承 `LeftHandSideExpression`），所以 TS 的声明是个谎，靠类型断言蒙过去。移植版照抄了这个断言，于是 `const x = y!;` 直接抛。改成 `LeftHandSideExpression`（分支能产出的诚实上界）。
4. **`Utilities.IsSuperProperty` 的联合转换。** TS 转的是**联合** `PropertyAccessExpression | ElementAccessExpression` 再读 `.expression`，两边都有该成员所以永不失败；移植版只转前者，`super[x]` 一调就抛。改成 kind switch。
5. **`ClassExpression` 不在 `Expression` 链上。** §12.8 的能力基维护点又发作一次：`ClassExpression extends ClassLikeDeclarationBase, PrimaryExpression` 里能力基占了类槽，类表达式无法构造，而 `IsExpression`（kind 判断）一直答 true。修 `s_capabilityBases` 加 `"ClassLikeDeclarationBase"`。
6. **`ImportExpression` 从不被实例化。** `createToken(ImportKeyword)` 应该给出 `ImportExpression`（它有自己的类），端口只给了裸 `Token`，而谓词按 kind 说它是表达式 —— 动态 `import()` 一到就崩。

### 新增守卫（永久，与 §12.8 的维护点配套）

那 6 个 bug 里有 3 个属于「谓词答 X、而模型或工厂根本造不出 X」这一族，且都不会编译错，所以本批把三方（kind 谓词 ↔ 类链 ↔ 工厂产出）钉成了反射测试 `Model/NodeChainTests.cs`：

- `EveryKindTheExpressionPredicatesAccept_IsAnExpressionClass` 与 `EveryExpressionSubclass_IsAlsoAnExpressionKind` 双向比对；
- `EveryKindTheStatementPredicateAccepts_IsAStatement`；
- `EveryTokenKindTheFactoryBuilds_AgreesWithTheExpressionPredicates` 遍历 `CreateToken` 接受的每个 kind，问工厂造出的对象是不是谓词说的那个类。

两份豁免清单是**显式且钉死**的，都逐个用 `lib/typescript.js` 验过，并且测试断言"清单恰好等于模型今天产出的集合"（多一条、少一条都失败）：

- 真实现的类型图与它自己手写的 `isExpression`（kind 列表）**本来就不一致**的 7 个 kind：`JsxAttributes`/`JsxOpeningElement`/`JsxOpeningFragment`/`JsxClosingFragment`/`JsxExpression`（JSX 的结构节点，是 `JsxElement` 的子件）与 `SyntheticExpression`/`SyntheticReferenceExpression`（transformer 层构造）。`ts.isExpression({kind})` 对这 7 个都答 false，对 `JsxElement`/`JsxSelfClosingElement` 答 true —— 端口两侧都照实复刻。
- `PrivateIdentifier`：`ts.factory.createToken` 对它返回**裸 token**（`escapedText` 为 undefined，而 `createPrivateIdentifier("#x").escapedText` 是 `"#x"`），因为 TS 的 `createToken` 对任何 kind 都造 `Token`、只用重载表达返回型；解析器从不把该 kind 走 `createToken`，端口照实复刻而不臆造 TS 不造的类。

`Parser/ParserSmokeTests.cs`（17 条）是端到端解析断言：文件范围与输入回填、各类声明的形状、诊断与恢复（含与真编译器对齐的 2 条 1134 及其位置）、`@ts-check`、`/// <reference>`、模块判定、JSON、父链、`identifierCount` 精确而 `nodeCount` 记 0、`.d.ts` ambient、`NonNullExpression` 的两种位置。

### 残留（指名，不留半成品）

| 项 | 卡在 | 归属 |
|---|---|---|
| `convertToJson`（`parseJsonText` 的分支） | 它住在 `commandLineParser.ts`，建立在 M8 的选项模型上；解析器唯一那次调用的 `jsonConversionNotifier` 是 `null`，选项驱动的分支不可达 —— 调用点删掉并记 `PORT-NOTE` | M8 |
| `FactoryUtilities.IsJSDocTypeAssertion` | `getJSDocTypeTag` → JSDoc 归属与父链遍历（§15.9 同条） | M5 |
| `SourceFile.NodeCount` | 节点计数属于 `baseNodeFactory`（§15.2 不移植），解析器侧记 0 | 不移植 |
| `Utilities.SupportedDeclarationExtensions` | 目前只有 `Paths` 的私有副本在用；`utilities.ts:9982` 的忠实位置在 `Utilities`，等 M5 的 `moduleNameResolver` 一起来 | M5 |

### 取舍

- **`TryParse`/`SpeculationHelper` 的泛型不受约束**：TS 的 `tryParse(f)` 用 `!result` 判空，而 `result` 可能是 `0`/`false`/`""` —— 那是**真值**判断，不是 null 判断。端口用 `!Js.IsTruthy(result)` 表达同一件事，因此 `T` 不能加 `where T : class`（那会改变语义）。
- **`AsName` 返回 `Node?`**（§15.9 bug 3 的收尾）：生成器按赋值目标槽的类型给收窄转换，手写的创建成员里有 4 个因此自己转到 `Identifier?`。
- **`convertToJson` 之外，`parseJsonText` 的表达式调用是无参的**：`parseArrayLiteralExpression`/`parseObjectLiteralExpression` 在 TS 里有可选参数，但**没有任何调用点**传它们（装饰器路径走的是别的成员），所以端口声明成无参。
- **`ProcessCommentPragmas`/`ProcessPragmasIntoFields` 收 `IReadonlyPragmaContext`**：TS 写 `sourceFile as {} as PragmaContext`，端口里 `SourceFile` 就实现该接口、八个槽可写，不需要转换。

---

## 15.11 M4g P1a：解析差分外环

### 交付

`tools/AstDiff`（新项目，`tsc-cs.slnx` 已收录，编译器项目加了一条 `InternalsVisibleTo("AstDiff")`）：C# 驱动 + 端口侧 dumper；`tools/AstDiff/ts-dump.cjs` 是 node 批量侧，一次进程内加载 `lib/typescript.js` 把整份清单解析、dump、哈希。两侧用**同一套规范化格式**（kind 数值、pos、end、NodeFlags、标识符/字面量文本 + 每条诊断的 code/start/length），子节点顺序都走 `forEachChild`（端口是生成表，TS 是真实现），脚本种类按扩展名在两侧同样推导。比对用 FNV-1a 哈希，**只对分歧文件落盘**整棵 dump，所以 19,585 个用例的产物是 KB 级而不是 GB 级。

```
dotnet run --project tools/AstDiff -- --cases <dir> [--filter <glob>] [--out artifacts/astdiff]
```

结果（`D:/Source/GitHub/TypeScript/tests/cases`，19,585 个文件，23 秒）：

| | 首轮 | 修完 |
|---|---|---|
| same | 19,539 | **19,585** |
| diverged | 36 | **0** |
| port-crash | 10 | **0** |
| ts-error | 0 | 0 |

没有 kind 错、没有范围错、没有 flags 错、没有只差诊断条数的用例 —— 剩下的每一处都归因到了下面 7 个缺陷之一，7 个缺陷全部修掉。

### 修掉的 7 个缺陷

**全部由差分发现**：编译零警告、697 条单测全绿时它们依然存在（其中 4 个是真崩溃）。前 5 个的根因是同一族 —— TS 靠 JS 的数字/字符串语义、靠结构类型或未检查转换，端口必须把那些语义显式实现出来。

| # | 现象 | 根因（TS 侧真相 → 端口原先的写法） | 影响 |
|---|---|---|---|
| 1 | `for await (…)` 丢 `awaitModifier`；AST 里没有那个 `AwaitKeyword` 子节点 | `CreateTokenNode` 的 kind→类映射少了 **keyword 与 modifier** 两支，`parseTokenNode` 因此给出普通 `Token`；而 `ForOfStatement.AwaitModifier` 的 C# 类型是 `KeywordToken?`，`x as KeywordToken` 对普通 `Token` **答 null 而不抛** | 27 个用例 |
| 2 | `NumericLiteral.Text` 数字格式不对（`1E+25` vs `1e+25`、`8.14E-05` vs `0.0000814`） | TS 调 `String(value)`（引擎实现 ECMA-262 `Number::toString`）；端口只处理安全整数，其余交给 .NET 的 `"R"`（大小写、阈值、指数位数都不同） | 7 个用例 |
| 3 | `\u{FFFFFFFF}` 报 `Hexadecimal digit expected`（1125）而不是范围诊断（1198） | TS 用 `parseInt(hex, 16)`（double，永不溢出）；端口用 `Convert.ToInt32`，8 位十六进制过半时溢出/答 -1 | 2 个用例 |
| 4 | `\u{D800}` 直接崩（`ArgumentOutOfRangeException`） | TS 的 `utf16EncodeAsString` 先走 `codePoint <= 0xFFFF ? String.fromCharCode(…)`，而 JS 的 `String.fromCodePoint` **接受代理区**（只拒绝 > 0x10FFFF）；.NET 的 `char.ConvertFromUtf32` 拒绝代理区。原先的 PORT-NOTE 断言"该差异不可达" —— 4 个用例证伪 | 4 个用例 |
| 5 | 数百位的二进制/八进制字面量崩（`OverflowException`） | TS 用 `parseInt(text, 2/8)`（double 累加，逐渐失精度直至 `Infinity`）；端口用 `Convert.ToInt32`。修法是 `Js.ParseInt`（按同样算术折叠），并让 `Js.NumberToString` 负责 `Infinity` 的字符串形态 | 4 个用例 |
| 6 | JSDoc 注释首行缩进小于 margin 时崩（`Substring(-1)`） | TS 写 `whitespace.substring(margin - indent)`，**JS 的 `String.prototype.substring` 把负起点钳到 0**；C# 的区间 `[x..]` 直接抛。上方的卫语句只限制了上界 | 1 个用例 |
| 7 | `@callback` 后跟 `@this` 时崩（`InvalidCastException`） | TS 自己把 `JSDocThisTag` 塞进 `JSDocSignature.parameters`（`parseChildParameterOrPropertyTag` 的声明包含它，而 `parseCallbackTagParameters` 用未检查的 `as`）—— 参考实现的 AST 里真的有那个 this-tag。C# 的 `(T)`/`as`/`(T)(object)` 都做运行时检查，所以这是**全端口唯一**处用 `Unsafe.As` 改写类型（`CommentParser.Reinterpret<T>`，带 PORT-NOTE 与 §15.10 级别的说明） | 1 个用例 |

第 1 条尤其值得记：`as` 在 C# 里对引用类型**答 null 而不抛**，所以一个缺失的 kind→类映射表现为"静默丢一个子节点"，而不是错误 —— 这是 P1a 这种整树比对才能抓到的东西。

### 为什么这轮的性价比最高

- 不依赖任何新层：P1a 只用到已经落地的解析器，`.errors.txt`/`.js`（P1b）、`.symbols`（M5）、`.types`（M6）各自等自己那层。
- 它的覆盖面比 `.errors.txt` 大：那些 baseline 只比诊断文本，而且混着语义错误；P1a 比整棵树（kind/范围/flags/文本/诊断），且在 19,585 个真实用例上是**可判定的**——要么逐字节一致，要么有一处差异，没有"看起来差不多"。
- 它现在是常驻工具：改解析器后一条命令就能回归，M5 之后还能把它加进 `.symbols` 的比对。

### 遗留

- **P1b**（`.errors.txt` + `.js` 的官方 baseline）仍等发射器；`tests/TypeScript.Baselines`（§6）会在那时建立。
- §15.10 的 4 个命名残留（`convertToJson` → M8、`IsJSDocTypeAssertion` → M5、`NodeCount` 不移植、`SupportedDeclarationExtensions` → M5）不变。
- `Join`/`NumberToString` 这类 JS 语义函数现在有了单测，但它们只覆盖用例列表；真正的证据是 19,585 个用例的全量一致。

---

## 15.12 M5 计划：`binder.ts`（Layer 4）

`binder.ts` 3,916 行，导出只有 7 个（全部 `/** @internal */`）：`ModuleInstanceState`、`ContainerFlags`、`getModuleInstanceState`、`createFlowNode`、`bindSourceFile`、`isExportsOrModuleExportsOrAlias`、`getContainerFlags`。其余是 `createBinder()` 里的 3,277 行（占 84%）—— 一次侦察（file:line 逐条核对）给出的分片：

| 片 | 内容 | 行数 | 依赖 |
|---|---|---|---|
| **M5-0** 前置 | `Symbol`/`SymbolTable` 具体类、flow 节点形状（`FlowNodeBase` + `SwitchClauseData`/`ReduceLabelData`）与 `IFlowContainer.FlowNode`/`SourceFile.EndFlowNode` 的改型、`Debug` 的 `AssertNode`/`FormatSyntaxKind`/`AttachFlowNodeDebugInfo`、`Utilities/Utilities.Binder.cs` 的 utilities 前瞻切片、binder 的 5 个叶导出 | 288（binder.ts 内）+ 其它文件 | 无（**瓶颈**） |
| **M5-1** 骨架 | binder 状态与 `bindSourceFile`、符号/声明助手（`createSymbol`/`addDeclarationToSymbol`/`declareSymbol`/`declareModuleMember`/`getDeclarationName`）、`bindContainer`、`bindChildren`、`bindEach*`、声明绑定器、`bind`/`bindJSDoc`、`lookupSymbolForName` | 1,548 | M5-0 |
| **M5-2** 流图 | narrowing 谓词、flow 节点工厂、语句/表达式流绑定、`createBindBinaryExpressionFlow`、optional-chain 流 | 976 | M5-0/M5-1 |
| **M5-3** 严格模式 + JSDoc 延迟绑定 | `checkStrictMode*` 全家族 + `bindJSDocTypeAlias`/`delayedBindJSDocTypedefTag`/`bindJSDocImports` | 330 | M5-1 |
| **M5-4** 模块/导出/JS 动态赋值 | `bindSourceFileIfExternalModule`…、`bindObjectDefinePropertyExport`/expando 家族、`bindCallExpression` | 496 | M5-1 |
| **M5-5** 收口 | `bindWorker` 的 `switch (node.kind)`（784 行的唯一串行点；可考虑改成 kind→handler 表以便各片真正并行）+ 集成 + 验证 | 250 | M5-2/3/4 |

M5-1 之后的 M5-2/3/4 互不依赖，可并行；M5-5 是唯一的汇合点。

**这份计划已全部落地**（见 §15.13）。

### 两个规划发现

1. **选项模型要提前**：binder 一开局就读 `getEmitScriptTarget(options)`（`binder.ts:571-618`）与 `getAlwaysStrict(options)`（620-628），而 `CompilerOptions` 在 §8 里属 M8。TS 的 `CompilerOptions` 是约 120 个可选槽的**接口**，真正庞大的是 `CommandLineOption` 的解析元数据（M8 的 CLI/tsconfig）。因此 M5 需要的是**接口本身 + 少量读选项的辅助**（`getEmitScriptTarget`/`getStrictOptionValue`/`getAlwaysStrict`），不是那套解析器 —— 这也顺带解掉 M4f 的两处阻塞（§15.11 末尾）。
2. **M5 的验证怎么办**：`.symbols` baseline 属 §7 的 P2（依赖 checker）。P1a 的做法可以直接搬，而且出口已经探明（`artifacts/ts-symbol-dump.cjs` 是一个跑通的最小探针）：真 `lib/typescript.js` 的 `createProgram` + `getTypeChecker()` 会触发绑定，之后每个节点都带 binder 写下的 `symbol` 与 `locals`，于是可以 dump 出 `(kind, pos, end, symbol 的 flags 名/名字/声明数, locals 的键值表)` 并按名字排序保证确定性 —— 与 `tools/AstDiff` 同形的 `BindDiff`：端口侧 dump 同一批字段，逐字节比。示例输出（一个 5 行的输入）：

```
SourceFile(0,131) symbol=ValueModule:"/p" locals=[C=Class:C:1,a=BlockScopedVariable:a:1,f=Function:f:1,x=AliasExcludes:x:1]
  VariableDeclaration(13,23) symbol=BlockScopedVariable:a locals=[]
  FunctionDeclaration(24,63) symbol=Function:f locals=[p=FunctionScopedVariable:p:1,q=BlockScopedVariable:q:1]
```

同一套比对对 M6（checker）也是现成的：`getTypeChecker()` 之后要什么字段加什么字段。

---

## 15.13 M5：`binder.ts`（Layer 4）

### 交付

`binder.ts` 3,916 行落到 `Binder/` 下 24 个文件：状态与入口（`createBinder` 闭包 → 每文件实例，`Parser`/`Scanner` 同处理）、符号与声明的整套助手（`createSymbol`/`addDeclarationToSymbol`/`declareSymbol` 的冲突合并全路径/`declareModuleMember`/`getDeclarationName` 的五种名字形态与九 kind switch）、`bindContainer`/`bindEach*`/`bindChildren`、声明绑定器、**流图**（narrowing 谓词、flow 节点工厂、语句/表达式/解构/可选链流、`createBindBinaryExpressionFlow` 的蹦床）、严格模式与 JSDoc 延迟绑定（`checkStrictMode*` 全家族 + `delayedBindJSDocTypedefTag`/`bindJSDocImports` + JSDoc tag 家族）、模块/导出/JS 动态赋值（`bindSourceFileIfExternalModule`…、`bindObjectDefinePropertyExport` 与 expando 家族、`bindCallExpression`），以及 `bindWorker` 的分派。

三处设计选择：

- **`bindWorker` 是 kind→handler 表，不是 `switch`。** TS 用一个 250 行的 `switch (node.kind)`，但那个 switch 是 M5 的唯一串行点：若照抄，四条特征切片都要改同一个文件。端口落成按 kind 索引的静态表（`ForEachChildTable` 的分派先例），各切片各自注册自己那一段，表上带"某 kind 被注册两次或漏注册"的断言（73 个 kind，与参考的 switch 一一对应）。
- **选项只有 `ICompilerOptions` 一个类型**（生成物，137 槽）。`bindSourceFile` 收它，而 M8 的配置/CLI 解析器与测试 fake（`FakeCompilerOptions`）才是"构造"发生的地方 —— 与 `IReadonlyPragmaContext` ← `SourceFile`、`INodeArray<T>` ← `NodeArray<T>` 同形。计算型读取 `GetEmitScriptTarget`/`GetAlwaysStrict`/`GetStrictOptionValue` 单独成文件，因为 TS 的 `_computedOptions` 元数据表（其解析半边属 M8）没有移植。
- **`tracing` 子系统照实移植**（`tracing.ts` 322 行），而不是把 binder 的三处 `tracing?.push/pop` 丢掉：侦察表明它卡在 Layer 0 而不是后续层，丢掉的话 Program/Checker/services 以后都得回头处理。可空实例语义对齐 TS 的 `tracing?.…`。

### 验证

| 项 | 结果 |
|---|---|
| `dotnet build` | 0 错误 0 警告 |
| `dotnet test` | **834/834**（三次连跑一致；M5 新增约 140 条） |
| `tools/Gen --check` | 84 项产物无漂移 |
| `tools/AstDiff` 全量 | **19,585 / 19,585 逐字节一致**（M5 没有扰动解析器） |
| `tools/BindDiff`（新增，绑定的流程/流图差分） | **19,233 / 19,233 逐字节一致**（diverged=0、port-crash=0；3 个 `ts-error` 是参考实现自己对深递归压力用例栈溢出，见下） |

**`tools/BindDiff`** 是 P1a 的做法搬到 binder：真 `lib/typescript.js` 的 `createProgram` + `getTypeChecker()`（后者是触发绑定的唯一途径）dump 出每个节点的 `flowNode`/`endFlowNode`/`flowNodeWhenTrue`/`flowNodeWhenFalse`/`fallthroughFlowNode`/`possiblyExhaustive` 以及流节点自身的 `FlowFlags`、payload 与前驱（`F#<id>` 行、前驱按首现编号 `#id`），端口侧 dump 同一套字段，逐字节比。字段选择本身就是合同：**只有 binder 写下的槽**参与比较，所以一条差异必然是绑定行为的差异，而不是 checker 的。

覆盖面：语料里 18,876 个 `.ts`、333 个 `.tsx`、24 个 `.js`（合计 19,233）参与比较；351 个 `.json` 与 1 个 `.txt` 由解析差分覆盖（JSON 没有可比的流图）。3 个 `ts-error` 全部是**参考实现自身的限制**：`binderBinaryExpressionStress.ts`、`binderBinaryExpressionStressJs.ts`、`largeControlFlowGraph.ts` 上 TS 自己的 binder 抛 `Maximum call stack size exceeded`，而端口侧 0 崩溃（工具在 256 MiB 预留栈的线程上跑 binder，与 `tools/AstDiff` 同一处理）。

### dump 的内存：从数十 GB 修到数百 MB

第一次全量跑时 C# 侧峰值达 **40 GB**（`BindDiff.exe`），node 侧在 5,908 个文件的子集上就有 897 MiB。两个真因都在 dump 的写法上，而不是在绑定本身：

1. **编号表建在 `key` 调用里**：同一个流图在文件里被引用上千次，每次引用都重新编号、重新整份打印。
2. **前驱描述被拼成嵌套字符串**：`ante=<另一个节点的完整描述>`，于是每个祖先的字符串里都嵌着一整棵子图。

两条都让输出随"引用次数 × 子图大小"增长，而不是随流图大小增长。修法是格式级的：**每文件一张编号表、每个流节点只打印一次、所有指向流节点的字段一律是 `#id` 引用**（`F#<id> flags=… ante=…` 加可选的 payload 行）。两侧（`Program.cs` 的 `FlowDumper` 与 `ts-flow-dump.cjs` 的 `flowDumper`）同步改，格式即契约。

修后实测（全量 19,233 个文件，34.6 秒）：

| | 修前 | 修后 |
|---|---|---|
| C# 侧峰值 | ~40 GB（用户实测） | **229 MiB** |
| node 侧峰值 | 897 MiB @5,908 文件（全量更高） | **≤250 MiB**（每批 250 个文件，`--node-batch`） |
| 端口遍历耗时 | 3.5s @5,908 | **2.2s @5,908** |

工具现在在报告里自报自己的峰值工作集，并在每批 node 的输出里带上它自报的 `maxRSS` —— 这类"dump 比图长得快"的回归会直接显示在两行数字上。

### 集成期修掉的两个问题

1. **测试程序集必须串行**（`AssemblyInfo.cs` 的 `CollectionBehavior(DisableTestParallelization = true)`）。症状是**失败集合每次运行都不同**：端口忠实保留了参考实现的若干进程级状态（`Tracing.Current` 单例、flow 节点 id 计数器），而 xUnit 默认并行跑不同 collection —— 一个 tracing 测试开启单例期间，另一个测试里的 `bindSourceFile` 会调 `Tracing.Current?.Pop()`，于是弹出别人的事件并撞上 "results are not supported for events with separateBeginAndEnd" 断言。每个测试自己的 `finally` 收尾挡不住*并发*读者，所以这是程序集级的决定，而不是某个测试的问题。
2. **两处随进度变化的断言**：`BinderSkeletonTests` 的 `ClassifiableNames` 期望值随 `ImportSpecifier` 臂落地而多了 `q`（`Classifiable` 包含 `Alias`，新值才是对的，已重钉并注明缘由）；同一文件里的 `SymbolCount == 11` 则是"走到哪算哪"的数字（从 11 变 13），按 §5.5 删掉而不是重钉 —— 符号本身由 `BinderModuleExpandoTests`/`BinderFlowTests` 断言。

### 遗留

- **`bindWorker` 未做成真正的"注册表 + 各切片自注册"**：现在是一张表加四个分段注册方法（`AddCoreWorkerHandlers` 等），仍在同一批文件里。若 M6 之后还有切片要加 kind，应该把注册点拆到各自文件。
- **flow 节点 id 是进程级的**（与参考一致）：`BindDiff` 的 dump 因此不能跨文件比较 id，工具只比较同一次运行内的相对编号。
- **`.symbols` baseline 仍属 §7 的 P2**：`BindDiff` 比的是 binder 写下的槽，不含 checker 的符号解析结果。
- M5-0 已记的 `TransientSymbol`/`SymbolLinks` 属 M6；`convertToJson`（M8）、`IsJSDocTypeAssertion` 的 JSDoc 归属家族、`SupportedDeclarationExtensions` 三处 M4e 残留不变。

---

## 15.14 M4f：`factory/emitHelpers.ts`

### 交付

`emitHelpers.ts`（1,491 行）→ `Parser/EmitHelpers.cs` + `EmitHelpers.CreateEmitHelperFactory(IEmitHelperFactoryContext)`（对齐 TS 的 `createEmitHelperFactory`）。每个 helper 的 `name`/`importName`/`scoped`/`priority`/**`text`**/`dependencies` 照参考复刻；文本用 C# 逐字字符串承载，两处由选项选择的文本（低于 ES2015 才请求的 `__assign`、`jsx: Preserve` 分支）由测试钉住。顺带补上 M4f 唯一缺的前置 `Utilities.GetEmitJsx`（`utilities.ts` 的 `jsx` 计算项），与既有三个读选项辅助同形。

### 为什么这半个现在能做、另半个不能

- `emitHelpers.ts` 只读两处选项：`getEmitScriptTarget(context.getCompilerOptions())` 与 `context.getCompilerOptions().jsx`。两者在 M5-1b 与本次到位，加上 M4f 前置落下的 `EmitHelper`/`IEmitHelperFactoryContext`/`EmitFlags`/`InternalEmitFlags`/`GeneratedIdentifierFlags`/`JsxEmit` 与 `Parser/EmitNodes.cs` 的 `setEmitFlags`，它不再依赖任何后续层。
- `expressionToTypeNode.ts` 的唯一入口 `createSyntacticTypeNodeBuilder(options, resolver)` 里的 `resolver` 是 checker 侧的类型解析回调 —— 仍等 M6。

### 验证

| 项 | 结果 |
|---|---|
| `dotnet build` / `dotnet test` | 0 错误 0 警告 / **865/865** |
| `tools/Gen --check` | 84 项无漂移 |
| `tools/AstDiff` 全量 | 19,585 / 19,585 |
| `tools/BindDiff` 全量 | 19,230 / 19,230（0 分歧、0 端口崩溃，端口峰值 196 MiB） |

**helper 文本的验收方式**：真编译器不把这些 helper 放在 `lib/typescript.js` 的公开面上（`createEmitHelperFactory` 需要 emitter 的 `requestEmitHelper` 上下文，且多数成员要真实 AST 参数），所以**端到端**验收放在 M7 —— `.js` baseline 会把每一段文本与官方输出直接比对。本次补一个源码级机械抽查：从 `emitHelpers.ts` 的模板字面量抽出文本，与端口逐字符比，`decorateHelper`（644 字符）、`extendsHelper`（944）、`awaitHelper`（139）三例**逐字节相同**。

### 遗留

- `expressionToTypeNode.ts`（M6 之后）。
- `createExternalHelpersImportDeclarationIfNeeded` 与 `getLocalNameForExternalImport`/`getExternalModuleNameLiteral` 仍等模块与选项面（§15.9 记的同一批）。

---

## 15.15 M6 计划：Layer 5（`Program` + `Checker`）

### 量级（实测）

| 文件 | 行数 | 归属 |
|---|---|---|
| `checker.ts` | **54,434** | M6（`createTypeChecker` 54,000 行里的 96% 都在一个函数体里） |
| `utilities.ts` | 12,447 | M6 需要其中 ≥200 个名字（`createEvaluator`/`createNameResolver`/`createDiagnosticCollection`/`compareDiagnostics`/`createTextWriter`…） |
| `program.ts` | 5,201 | M6 |
| `moduleNameResolver.ts` | 3,428 | M6-late |
| `moduleSpecifiers.ts` | 1,470 | M6-late（checker 只用到两处） |
| `expressionToTypeNode.ts` | 1,330 | M6（M4f 的等待项） |
| `programDiagnostics.ts` / `symbolWalker.ts` | 427 / 198 | M6 |
| `resolutionCache.ts` / `commandLineParser.ts` | 1,716 / 4,300 | M8（program 只在宿主给出 cache 时才用那两个工厂） |

合计约 7.5 万行 —— 是 M4（25,600）的三倍，因此按 §8 的分期（基础类型 → 泛型 → 高级特性）切成八片：

| 片 | 内容 | 依赖 | 并行 |
|---|---|---|---|
| **M6-0** 前置 | 手写 `Type`/`Signature` + 约 28 个子类、`SymbolLinks`/`NodeLinks` 与按 id 惰性分配的表、`TransientSymbol`、`SymbolTrackerImpl`、`SymbolTable` 保序形态 + 槽位迁移、`Checker/CheckerEnums.cs`、`Checker.cs` 状态骨架与 partial 布局 | 无（**瓶颈**） | 内部两半可并行 |
| **M6-1** Program | `program.ts` 宿主/诊断/mode/loader/状态族 + `createProgram` + `program` 字面量、`programDiagnostics.ts` 全量、`moduleNameResolver.ts` 全量、`commandLineParser` 的无条件薄片 | M6-0 | 与 M6-2/3/4/6 |
| **M6-2** 类型构造与查询 | `createType`/`createObjectType`/`getUnionType`/`getIntersectionType`/`instantiateType`/`getIndexType`/`getConditionalType`、链接访问器、`getTypeOfSymbol`/`getDeclaredTypeOfSymbol`/`createSignature`/`getPropertiesOfType`/`getSignaturesOfType`/`getReturnTypeOfSignature` | M6-0 | 与 M6-1 |
| **M6-3** 类型关系 | `isTypeRelatedTo`/`recursiveTypeRelatedTo`/`structuredTypeRelatedTo`/`isSignatureAssignableTo`/`compareSignaturesRelated` + 三张关系缓存 | M6-2 | 与 M6-1 |
| **M6-4** 推断与调用解析 | `inferTypes`/`inferTypeArguments`/`getContextualType`、`resolveSignature`/`resolveCall`/`chooseOverload` | M6-2/3 | 推断半边与 M6-5 并行 |
| **M6-5** 检查器主体 | `checkExpression` 家族、各 `checkXDeclaration`、`checkSourceFile`、诊断收集与排序去重、`initializeTypeChecker` 与返回的 `TypeChecker` 组装 | M6-2/3/4 | 声明半边与 M6-6 并行 |
| **M6-6** 节点构建器 + EmitResolver + 字符串化 | `typeToTypeNode`/`symbolToNode` 家族、`createResolver`、`NodeBuilderContext`/`SymbolTrackerImpl`；`typeToString` 等受 printer 门控 | M6-2/3/5 | 与 M6-5 的声明半边 |
| **M6-7** 外环差分 + 文档 | 新建 `tools/TypeDiff`（形制照 `tools/BindDiff`：FNV-1a 哈希、只落盘分歧、node 分批、自报峰值、256 MiB 栈线程）；把 §15.12 的符号 dump 探针扩成类型 dump | M6-1/2 | 与 M6-3…6 |

**M6-0 已落地**（`f98a201` + 本次提交）：类型系统具体类、链接表与保序 `SymbolTable` 的全量迁移、`TransientSymbol`、`Checker` 枚举与状态骨架。验证：build 0/0、test 892/892、`Gen --check` 无漂移、`AstDiff` 19,585/19,585、`BindDiff` 19,230/19,230（迁移未扰动解析与绑定）。

### 侦察揪出的静默高危项

`SymbolTable` 在端口里是 `Dictionary<string, ISymbol>`（**无序**），而 TS 的 `SymbolTable = Map<__String, Symbol>` 按插入序迭代 —— checker 的属性列表、联合归约与显示字符串都从它派生。`tools/BindDiff` 看不见这个差异，因为它的 dump 对 key 排了序。因此 M6-0 必须先把 `SymbolTable` 换成保序形态并迁移全部槽位与调用点，**在 checker 开始写 members 之前**。这条与 M5 的教训同类：缺失/错误的模型槽会晚爆，而这次它连差分都躲得过。

### M6→M7 边界（loud blocker 清单，M7 落地时逐条接管）

`Program` 侧由 `Program.EmitNotLanded` 统一抛出（消息里带成员名与 TS 行号）：

| 成员 | TS 位置 | 归 M7 的理由 |
|---|---|---|
| `Program.Emit` | program.ts:2688 | 需要 emitter（Layer 7） |
| `Program.EmitBuildInfo` | program.ts:2650 | 同上（build info 的序列化在 emitter 侧） |
| `Program.GetDeclarationDiagnostics` / `…Worker` | program.ts:2827 / 3244 | 声明发射的诊断通道 |
| `Program.GetEmitHost` | program.ts:2598 | 发射宿主 |

另有三个**声明在 emitter.ts/utilities.ts、但本质是 program 状态**的辅助，M6-1 不得不先落在 `Program.EmitBoundary.cs`（各带 PORT-NOTE）：`Program.GetCommonSourceDirectoryFromOptions`（emitter.ts:636）、`GetCommonSourceDirectoryOfConfig`（:685）、`GetComputedCommonSourceDirectory`（utilities.ts:10261）。**M7 必须调用它们而不是再声明一份**——这与 `formatMessage` 家族同一条规则。

### 实现偏差与调用点归属（M6）

归属的裁决依据是**"已有调用点已经写在哪里"**——它同时满足"可编译"与"一个名字一处声明"，且搬迁成本最低。这条规则在 M6 期间被用错了两次（我先按文件名判断类归属），所以这里记下判据本身：**看命中所在的类，而不是命中所在的文件**——`Checker/Checker.Support.cs` 里就有两个 partial 类（`Checker` 与一个 `Utilities`）。

| 名字 | 参考声明位置 | 端口归属 | 依据与状态 |
|---|---|---|---|
| `IsPartOfTypeNode`、`IsPartOfTypeExpressionWithTypeArguments`、`IsNodeDescendantOf`、`IsExpressionWithTypeArgumentsInClassExtendsClause`、`GetFirstIdentifier`、`IsNumericLiteralName` | `utilities.ts`（逐个核过：`checker.ts` 声明数为 0） | **已经是 `Utilities` 成员**，声明在 `Checker/Checker.Support.cs:1978-2242` 的 `Utilities` partial 里；唯一缺陷是 `Support.cs:717` 两处未限定调用（CS0103），改为 `Utilities.IsNodeDescendantOf(…)` | 类是对的、声明唯一；文件位置在切片旁边（`Program/Utilities.Program.cs` 同形），接受并登记——"同一类"才是规则，不是"同一目录"。其余调用点已全部限定 |
| `GetCheckFlags`（`utilities.ts:8040-8042`）、`GetObjectFlags`（`utilities.ts:8280`） | `utilities.ts` | **`Checker`**（`Checker.Symbols.cs` / `Checker.Support.cs`），登记为偏差 | 约四十处既有调用点都是**未限定**的，声明到 `Utilities` 要在正在写的文件里改四十处；`Checker.Support.cs:52` 的 PORT-NOTE 本就计划这个家 |
| `GetNodeId` | `checker.ts:1460` | **`Utilities.GetNodeId`**（`Utilities/Utilities.cs:480`），登记为反方向偏差 | 工厂自 M4 起就需要它，端口的家比本里程碑更早且有意为之；不因"参考声明在 checker.ts"被改回 |

集成期新增的反射守卫（"`Utilities` 与 `Checker` 不得声明同名成员"）以这张表为**显式允许清单**：被测试钉住的偏差是决定，没登记的偏差才是漂移。**M6-4 待办（本轮定位）**：`checker.ts:53816` 的 `array.sort(compareSymbols)` 是 `sortSymbolsIfTSGoCompat`（53812-53819）的函数体，调用点在 35290/35300 的 `getSpellingSuggestion`，属 M6-4 的 34740-37809 范围（`Checker/Checker.Inference.cs`，尚未落地）；端口应写成 `_stableTypeOrdering && array is not null ? Core.StableSort(array, (a, b) => Core.CompareValues(CompareSymbols(a, b), 0)) : array`（参考实现是原地排序）。

**M6-3 起规则收紧**：参考声明在 `utilities.ts`/`utilitiesPublic.ts` 的成员一律落 `Utilities`（类），调用点带限定。

### 快照验证的掩蔽效应（M6 的方法学发现）

Roslyn 只要在任何一处遇到**声明级**错误，就跳过**整个编译**的方法体绑定。于是"排除掉某个含声明级错误的文件后，我的文件零诊断"是**不可信**的读数：你会只看到那一处错误，而所有 CS0103/CS1061 一律消失。

本轮两个实例：`Program/Program.Verify.cs` 的 `SymlinkCache` CS0246；以及更隐蔽的一个——生成的 `Model/NodeInterfaces.cs` 引用了两个 Program 侧接口（`IFileIncludeReason`/`IFilePreprocessingDiagnostics`，声明在 `Program/Program.ProgramDiagnostics.cs`），所以"排除 `Program/**`"这一常见做法会让那两个 CS0246 变成全编译的声明级错误。它甚至骗过了有经验的切片：`M6Program` 据此报过"我的文件零诊断"，随后在解除掩蔽的运行里撤回，实测约 85 条机械性错误。

**判别法（照此读任何快照日志）**：声明级代码集合（CS0246/0101/0102/0111/0535/1513/1733/1002/1026）为空，**且**文件里有非零的 CS0103/CS1061/CS1593 —— 二者同时成立才说明方法体确实绑定了。反过来，只要有声明级代码，文件里为零就什么都不能说明。**修法（经 M6Conditional 实测修正）**：单独把 `Program.ProgramDiagnostics.cs` 包含回来**无效**——它自己会拉出 9 个声明级 CS0246（它对 `Program` 的兄弟 partial 与 `JsObject` 的引用）。掩蔽只能通过**等整个 Program 切片能编译后整块包含**来解除。在那之前，任何 Checker 切片的诚实证据路径是：文本证据（重复扫描、签名核对、括号配平、`ported from` 范围逐条核对）**加上**一份声明级代码集合为空的日志。

### 生成器缺口（M6 期间修掉）

`MapUnion` 的公共基只看 C# 的**类**链，而对没到达 `Node` 的联合会退化成 `Node`——两处显形：`IProgram.getFileProcessingDiagnostics()` 生成为 `List<Node>?`，`getFileIncludeReasons()` 生成为 `Dictionary<string, List<Node>>`，而端口的值分别是 `List<IFilePreprocessingDiagnostics>` 与 `List<IFileIncludeReason>`（TS 的 `readonly FilePreprocessingDiagnostics[]` 与 `Map<string, FileIncludeReason[]>`）。修法与 M5-0 的 `FlowNode`、M6-0b 的 `SymbolTable` 同一机制：两条 `s_overrides`。容器形状不动（§12.3 的 `Map`→`Dictionary` 本就是对的），`Program` 在边界上构造它。

### 四项裁定

1. **受 printer 门控的字符串化**（`typeToString`/`symbolToString`/`signatureToString`/`typePredicateToString` 用到 `checker.ts:6194-6264` 的 `createPrinter*`，属 M7）：采用端口既有惯例 —— **显式命名 + 测试钉住的 loud blocker**（先例是 `FactoryUtilities.IsJSDocTypeAssertion`，§15.9），并逐条进本节的残留清单。不使用静默降级：静默的错字符串会污染 `.types` baseline，而 loud blocker 会在 M7 接上时立刻变成绿灯。
2. **`moduleSpecifiers` 的两个调用点**（`checker.ts:8534`/`8638`）：归 M6-late，作为 M6-6 的契约项，不在 M6 主体里提前引入那 1,470 行。
3. **`Type`/`Signature` 的构造子收 `object?` 而不是 `ITypeChecker`。** 参考实现自己也只在 `Debug.isDebugging || tracing` 时才把 checker 存进那个字段（`utilities.ts:8489-8503`）——它纯粹是给调试器/服务层读的，checker 本身从不读它。端口因此把参数收成 `object?`、体内 `as ITypeChecker`：代价为零，却让"构造一个类型"不再依赖 `Checker` 类已经实现完那个 380 成员的接口（只有 M6-5 的组装能做到）。等组装完成后同一个 `as` 自然开始成立，**任何调用点都不需要再改**。这条解开的不只是 21 个编译错误，而是"在 M6-5 之前所有 checker 测试都跑不起来"。
4. **checker 的交付形态是 `Checker : ITypeChecker`**，与 `NodeFactory : INodeFactory` 同形：TS 的 `const checker: TypeChecker = {…}` 是一个字面量，而端口的成员是 landed partial 的实例方法，所以要由类实现那个生成的接口（380+ 成员，M6-5 组装完成后才可能转换成功；中间的"无法转换"是预期状态）。若到 M6-5 仍有接口成员表达不了（M4d 的 `INodeFactory` 有 6 个这样的），走 §15.9 的"接口比类大"处理：逐条指名 + 反射测试钉住差集，而不是给出发不出正确结果的实现。

---

## 16. `const enum` 成员作类型：typed enum

### 16.1 TS 的两条路径

`NodeFactory` 有一批方法用 **enum 成员的字面量类型**做参数，靠它区分重载：

```ts
createToken(token: SyntaxKind.EndOfFileToken): EndOfFileToken;
createToken(token: SyntaxKind.Unknown): Token<SyntaxKind.Unknown>;
createToken<TKind extends PunctuationSyntaxKind>(token: TKind): PunctuationToken<TKind>;
…
/** @internal */ createToken<TKind extends SyntaxKind>(token: TKind): Token<TKind>;
```

关键是**两条路径共存**，最后那条 `@internal` 的重载是它们的交汇点：

- **静态路径**：调用点写的是字面量 `SyntaxKind.EndOfFileToken`，TS 把 `TKind` 推断成那个字面量类型，于是命中前面的具体重载，返回 `EndOfFileToken`。parser 里绝大多数调用是这种 —— 不需要断言，也不需要 switch。
- **动态路径**：`token` 是运行时算出来的 `SyntaxKind` 值（`scanner.Scan()` 的结果、从别处传来的 kind），此时 `TKind` 只能是 `SyntaxKind` 本身，落到最后那条宽泛重载，返回 `Token<SyntaxKind>`。

两条路径能由**同一个签名集**承载，靠的是 `SyntaxKind.EndOfFileToken` 是 `SyntaxKind` 的**子类型**。C# 的 enum 成员是常量而不是类型，没有这层子类型关系 —— 所以 §15.6 的"合并 + 共同基类"会把两条路径**压成一条**，静态精度整个丢掉。

### 16.2 映射：每个成员一个类型

用 static abstract interface member，把每个 enum 成员映射成一个**类型**。参数类型因此各不相同，静态路径的重载重新可用；动态路径另外给一条宽泛重载 —— 正好是 TS 的两条。

```cs
public interface ITypedEnum
{
    SyntaxKind SyntaxKind { get; }
}

public interface ITypedEnum<TSelf> : ITypedEnum
    where TSelf : ITypedEnum<TSelf>
{
    /// <summary>供直接查询（`TypedEnum.CommaToken.StaticSyntaxKind`），不必经 Instance。</summary>
    static abstract SyntaxKind StaticSyntaxKind { get; }

    static abstract TSelf Instance { get; }
}

public abstract class TypedEnum<T>
    where T : TypedEnum<T>, ITypedEnum<T>
{
    private protected TypedEnum() { }
}

public static partial class TypedEnum
{
    public sealed class EndOfFileToken : TypedEnum<EndOfFileToken>, ITypedEnum<EndOfFileToken>
    {
        public static EndOfFileToken Instance { get; } = new();
        public static SyntaxKind StaticSyntaxKind => SyntaxKind.EndOfFileToken;
        public SyntaxKind SyntaxKind => SyntaxKind.EndOfFileToken;

        // 私有构造让 Instance 成为唯一实例；单例语义靠它，而不是靠类型系统。
        private EndOfFileToken() { }
    }

    /// <summary>动态路径的入口：编译期算不出的 SyntaxKind 值反查其 typed enum 实例。</summary>
    public static ITypedEnum? FromSyntaxKind(SyntaxKind kind) => kind switch
    {
        SyntaxKind.EndOfFileToken => EndOfFileToken.Instance,
        SyntaxKind.CommaToken => CommaToken.Instance,
        _ => null,
    };
}
```

`TypedEnum.EndOfFileToken`（嵌套在静态类 `TypedEnum` 里）与节点类型 `EndOfFileToken`（顶层）不在同一作用域，不冲突 —— 这一点在探针里用两个同名类型同时存在验证过。

工厂方法于是照 TS 写两条：

```cs
EndOfFileToken CreateToken(TypedEnum.EndOfFileToken token);      // 静态路径
PunctuationToken CreateToken(TypedEnum.CommaToken token);
Token CreateToken(SyntaxKind token);                             // 动态路径（对应 `@internal` 那条）
```

调用点分别是 `factory.CreateToken(TypedEnum.EndOfFileToken.Instance)` 和 `factory.CreateToken(runtimeKind)`。

### 16.3 两个接口的分工

| 接口 | 路径 | 用途 |
|---|---|---|
| `ITypedEnum<TSelf>` | 静态 | 编译期已知是哪个成员时用；`Instance` 是参数，`StaticSyntaxKind` 直接取 kind |
| `ITypedEnum` | 动态 | 编译期算不出是哪个成员时用；`FromSyntaxKind` 从值反查到实例，代码在运行时判断 |

非泛型那个不是"泛型版的退化形式"，而是**运行时投影**：当类型信息在编译期不存在时（值来自 scanner、来自外部输入、来自别的 kind 变量），它是唯一能问到"这个 kind 对应谁"的东西。

### 16.4 取舍与时机

- **量必须生成**：`SyntaxKind` 约 360 个成员 → 约 360 个嵌套类，加一张同样大的 `FromSyntaxKind` 表。手写不可行，由 `tools/Gen` 产出 —— 与谓词表、数据表同样的处理：形态固定就交给生成器，形态不符则报错。
- **调用点更啰嗦**（静态路径要多一个 `.Instance`），换回 TS 的静态精度。
- **只在需要处推广**：关键字与 `TextToToken` 等也能受益，但成本与收益要逐个看，不预先铺开。
- **时机是 M4c 的第一件事**：工厂签名一旦按 §15.6 的合并版落地，后面全部调用点都要返工。

这个形态已用独立探针验证过可编译可运行：静态重载按参数类型区分、宽泛重载与它们共存、反查表对未映射的 kind 返回 null、两个同名类型互不干扰、`StaticSyntaxKind` 与 `Instance` 在 `where TToken : ITypedEnum<TToken>` 的泛型上下文里都可用。

### 16.5 M4c 的接入：工厂签名恢复成两条路径

类型与反查表在 M4c 第一步就生成了，但映射器还没用它们 —— 当时 `createToken` 的 7 条具体重载仍被 §15.6 的合并压成一条、返回类型退成 `Node`。接入分三处：

| 位置 | 规则 |
|---|---|
| 映射器 | `SyntaxKind.X` **只在参数位置**换成 `TypedEnum.X`：别的位置上它是一个值，不是类型选择器。`X` 没有嵌套类型时（别名 `FirstAssignment`、哨兵 `Count`）即使处在参数位置也仍是 `SyntaxKind` |
| 签名分组 | `MemberKey` 改用"参数位置"的映射结果，于是那 7 条具体重载不再被合并 |
| 枚举约束的类型参数 | `createToken<TKind extends SyntaxKind>(token: TKind)` 在 C# 里没有对应写法（类型参数约束不到具体枚举），而调用点传的就是枚举本身 —— 类型参数因此被枚举替换、并从泛型参数表里去掉。这一条同时把 5 条泛型重载合成一条，正是 §16.1 说的动态路径 |

结果（`INodeFactory`，`Node` 是那条宽签名）：

```cs
Node CreateToken(SyntaxKind token);                                                          // 动态路径
SuperExpression CreateToken(TypedEnum.SuperKeyword? token) => (SuperExpression)CreateToken(token!.SyntaxKind);
ThisExpression  CreateToken(TypedEnum.ThisKeyword? token)  => (ThisExpression)CreateToken(token!.SyntaxKind);
… 7 条，每个成员一条
```

这 7 条静态重载是**默认实现**，所以实现方仍然只写一个方法体（`CreateToken(SyntaxKind)`）；转发目标由"参数是某个成员类型 vs 参数是枚举本身"配对（`WideningTargetOf`）。

两处与 §16.2 的示意不同，都是模型决定的：

- `EndOfFileToken` 在 `types.ts` 里是**别名**（`Token<SyntaxKind.EndOfFileToken> & JSDocContainer`），不是类，所以那一条的返回类型是 `Token`；
- 宽签名的返回类型是 `Node` 而不是 `Token`：5 条泛型声明里 `KeywordTypeNode` 不在 `Token` 链上，`CommonBase` 因此只到 `Node`（§15.6 的合并口径，未变）。


