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

### 9.x Go 端（TSGO / TSC 7.0）作为**静态类型化**参考（用户裁定，本会话）

`D:/Source/GitHub/TypeScript` 的 `origin/main` 内 **vendored** 了完整的 Go 端移植：`tsc/internal/`（`checker/` 含 `checker.go`、`relater.go`、`inference.go`、`nodebuilder*.go`、`flow.go`、`jsx.go`、`mapper.go`、`nodecopy.go`、`printer.go`、`pseudotypenodebuilder.go`、`services.go`、`grammarchecks.go`、`links.go`、`exports.go`、`emitresolver.go`；另有 `binder/`、`parser*`、`ast/`、`api/`）。**无需 checkout**：

- 读文件：`git -C D:/Source/GitHub/TypeScript show origin/main:tsc/internal/checker/relater.go`
- 全树检索：`git -C D:/Source/GitHub/TypeScript grep -n "<pattern>" origin/main -- "tsc/internal/**/*.go"`

**用法边界（强制）：**

- **可以**用作**机械/类型层**参考：凡 TS 里靠**结构类型**成立、而 C#/Go 这类（更）名义化的语言无法照抄的写法——兄弟类之间的 `as` 强转、联合类型读取、可选槽、稀疏数组槽、以基类为入口的遍历、`Kind` 判别、`Node`/`NodeArray` 的泛化面等。Go 端被**同一类**问题逼出过答案，直接借形状可省大量试错。
- **不可以**当**行为**参考：`release-6.0` 的 `checker.ts`/`program.ts`/`utilities.ts` 是语义的唯一真源；TSGO 与 6.0.3 不一致处**一律以 6.0.3 为准**（7.0 是重写，有真实行为差异）。
- 借此修出的每个缺陷仍须给出 **6.0.3 的行号**，并在 PORT-NOTE 里注明"形状参考 `tsc/internal/...:<行>`"，以便日后核对。
- **泛型反指示（`AGENTS.md` 新版原文：Go 的泛型设计不好；移植到 C# 时可以更激进地使用泛型）**：Go 端为了绕开其弱泛型而做的**退化写法不要照抄**——典型如 `any`/空接口 + 类型断言、按具体类型复制多份辅助函数、把本可参数化的槽位统一降到基类。C# 侧应当用泛型表达（`where T : Node` 约束、`IList<T>`、`Func<T, …>`、`NodeArray<T>`），形状参考只借"同一类问题被怎么想清楚"，不借其语法退化。已就位的正面例子：`Core.Map<T,U>`、`NodeArray<T>`、`GetParseTreeNode<T>(…) where T : Node`、`Core.Some<T>(IList<T>?, Func<T,int,bool>)`。
- **已验证实例**：`NewExpression → CallExpression` 兄弟类强转（该崩溃类 362 条）在 Go 端的解法是 **kind 判别 + 共享槽位读取**——`case ast.KindCallExpression, ast.KindNewExpression, ast.KindTaggedTemplateExpression, ast.KindDecorator, ast.KindJsxOpeningElement:`（`tsc/internal/checker/checker.go:2553`）与 `ast.IsNewExpression(expr)` 谓词（`checker.go:7547/7946/8506`）。这正是端口那 11 处结构型转换崩溃该采用的形状。

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
| 7 | `@callback` 后跟 `@this` 时崩（`InvalidCastException`） | TS 自己把 `JSDocThisTag` 塞进 `JSDocSignature.parameters`（`parseChildParameterOrPropertyTag` 的声明包含它，而 `parseCallbackTagParameters` 用未检查的 `as`）—— 参考实现的 AST 里真的有那个 this-tag。C# 的 `(T)`/`as`/`(T)(object)` 都做运行时检查，所以这类"参考里 unchecked、C# 里必须做检查"的转换在端口里落在**具名 punning 辅助**上：`CommentParser.Reinterpret<T>`（本条）、`Checker.ReinterpretAsTypeParameter`（Checker.Relations.Identical.cs:2331）、`Utilities.ReinterpretVisitedNode<TNode>`（Utilities.Visitor.cs:186，visitorPublic.ts:176 的未检查返回），以及 `Core.Flags` 的位运算 `Unsafe.As<T, uint/ulong>`；每个都带自己的 PORT-NOTE 与"读路径为何安全"的论证（§15.10 级别） | 1 个用例 |

第 1 条尤其值得记：`as` 在 C# 里对引用类型**答 null 而不抛**，所以一个缺失的 kind→类映射表现为"静默丢一个子节点"，而不是错误 —— 这是 P1a 这种整树比对才能抓到的东西。

### 为什么这轮的性价比最高

- 不依赖任何新层：P1a 只用到已经落地的解析器，`.errors.txt`/`.js`（P1b）、`.symbols`（M5）、`.types`（M6）各自等自己那层。
- 它的覆盖面比 `.errors.txt` 大：那些 baseline 只比诊断文本，而且混着语义错误；P1a 比整棵树（kind/范围/flags/文本/诊断），且在 19,585 个真实用例上是**可判定的**——要么逐字节一致，要么有一处差异，没有"看起来差不多"。
- 它现在是常驻工具：改解析器后一条命令就能回归，M5 之后还能把它加进 `.symbols` 的比对。

### 遗留

- **P1b**（`.errors.txt` + `.js` 的官方 baseline）仍等发射器；`tests/TypeScript.Baselines`（§6）会在那时建立。
- §15.10 的 4 个命名残留（`convertToJson` → M8、`IsJSDocTypeAssertion` → M5、`NodeCount` 不移植、`SupportedDeclarationExtensions` → M5）不变。（**后续状态**：`IsJSDocTypeAssertion` 与 `SupportedDeclarationExtensions` 已在 M5/M6 期间落地，见 §15.15 残留账。）
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
- M5-0 已记的 `TransientSymbol`/`SymbolLinks` 属 M6；`convertToJson`（M8）、`IsJSDocTypeAssertion` 的 JSDoc 归属家族、`SupportedDeclarationExtensions` 三处 M4e 残留不变。（**后续状态**：后两条已在 M5/M6 期间落地，见 §15.15 残留账。）

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

### M6 期间路由出、编译器看不见的风险（须在收口时逐条确认）

| 项 | 内容 | 处置 |
|---|---|---|
| **AST 形状风险** | `Checker/Checker.TypeOfSymbols.cs` 用 `FactoryUtilities.ParseNodeFactory`（`NoParenthesizerRules`）造节点，而其 PORT-NOTE 声称那就是 TS 的 `factory` —— 参考实现用的是带括号化规则的 `factory`，所以该文件可能产出与真编译器不同的 AST（编译器不会报错） | **已收口（第六轮后）**：20 处 checker 侧代码用途逐条对着参考核过 —— 6 处参考用 `factory`（`nodeFactory.ts:7407`）的改回 `NodeFactory.Factory`（`getFlowTypeFromCommonJSExport` checker.ts:12026-12028、`getFlowTypeInStaticBlocks` :12039/:12043、`getFlowTypeInConstructor` :12060/:12063、`getArgumentArityError` :36481、JSX children 假属性声明 :33927、`createCallSignature` :44068），3 处参考自己就用 `parseNodeFactory`（`getSyntheticElementAccess` :11694-11696、`createSyntheticExpression` :36251、`serializeAsNamespaceDeclaration` :10209 的 ModuleDeclaration 半与 `factory` 的 ModuleBlock 半混用）保持原样，其余 11 处仅是注释提及；`types/**`（845 文件）与 `classes/**`（466 文件）前后 `SUMMARY` 完全一致，无回归 |
| **生成器缺陷** | `Model/NodeInterfaces.cs:3526` 的 `GetIndexInfosOfIndexSymbol` 生成成了损坏的属性（`List<Func<…>>?`），而参考 `types.ts:5083` 是函数类型 `(indexSymbol: Symbol, siblingSymbols?: Symbol[]) => IndexInfo[]` | **已收口**：病根是 `tools/Gen/TsTypeMapper.MapSingle` 把 `T[]` 判定放在函数类型判定**之前**，于是返回类型以 `[]` 结尾的签名被读成"函数数组"（`List<Func<…>>`）。判定顺序调正后重新生成（只改这一行）：`Func<ISymbol, List<ISymbol>, List<IIndexInfo>>? GetIndexInfosOfIndexSymbol { get; set; }`，`Checker` 用后备委托实现（语义同参考 checker.ts:1658 的对象字面量赋值，函数本体 checker.ts:16693），`CheckerCreateTypeCheckerTests` 的"接口减类"差异表随之清空（测试改为断言该成员可答且形状正确） |
| **测试侧缺口** | `FakeTypeCheckerHost` 缺 8 个 `ITypeCheckerHost` 成员 | 集成时由我补齐（切片级报告都指了它） |
| **工具风险（自动限定器）** | 一次自动化限定把 `// LanguageFeatureMinimumTarget.X` 注释插到闭括号**之前**，`//` 吞掉 `)`，产生 6 处 `CS1026` → 掩蔽全编译 | 两侧已各自修好；后续批量脚本改动前后必须查声明级集合 |
| **并行构建** | 多个代理同时 `dotnet build` 会在 `obj/` 上互撞 | 配方：`-p:IntermediateOutputPath=obj/<name>/ -p:OutputPath=bin/<name>/`（`BaseIntermediateOutputPath` **不行**，会让默认 `obj/` 不再被排除而撞出 CS0579） |

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

**收口期补记（本轮）**：反射守卫 `CrossSliceOwnershipTests` 现在只允许**一个**同名例外，并逐个附参考行 —— 判据就是本节那句"看参考把它声明在哪个类"：

| 允许的同名成员 | 参考的两个声明处 | 为什么两边都必须留 |
|---|---|---|
| `SetTextRange` | `factory/utilitiesPublic.ts:10-12`（`setTextRange`，纯 pos/end 写入）与 `checker.ts:6517-6538`（node builder 的局部函数：跨文件不复制范围，并顺带种下 `original` 指针） | 参考自己就是两处声明，甚至在 `checker.ts:1010` 把 utilities 那一条改名导入为 `setTextRangeWorker` 来分开它们；C# 不能给局部函数改名而保留参考名，所以 `Utilities.SetTextRange`（调用点带限定）与 `Checker.SetTextRange`（不带限定）并存就是该分歧的 C# 形态 |

同一轮还消掉了该守卫**真正**抓到的两处漂移（不是加白名单）：`TypeParametersOf` 曾同时声明在 `Utilities`/`Checker`/`Program` 三处，现收敛为 `Utilities.TypeParametersOf`（`DeclarationWithTypeParameterChildren` 的唯一读法，见 types.ts:3536-3541）；`Checker` 那份私有 `GetNamedDeclarationName`（checker.ts:28353 的局部读）改名 `GetObjectLiteralMemberName`，让 `Utilities.GetNamedDeclarationName`（utilitiesPublic.ts:946-948 那条鸭子类型读）保持唯一主人。守卫本身也修了一处**测试缺陷**：`GetMembers` 不带 `DeclaredOnly` 会连 `object` 的六个成员一起收进来，于是交集永不为空、且失败消息前六个名字都是 `Equals, Finalize, …`，把真重叠藏在截断之后。

**三个崩溃级收口（本轮，全部有参考行）**：`Utilities.TypeParametersOf` 漏掉 `InterfaceDeclaration` 面（release-6.0 的 `InterfaceDeclaration` 不再 `extends ClassLikeDeclarationBase`）使**每个泛型接口**被判为无类型参数，`getGlobalType` 的 arity 检查因此失败、`globalArrayType` 退化成 `emptyGenericType`；`Program.IsSourceFileDefaultLibrary` 的体是自造且**自递归**的（参考 program.ts:2680-2682 只有 `libFiles.has(file.path)` 一行）；`Checker.GetTypeOfExpression` 把 `flowTypeCache` 当稠密表索引，越界即抛（参考 checker.ts:41691/41701 是稀疏数组的洞与延长）。另修 `Program` 构造里缺的 `structureIsReused = tryReuseStructureFromOldProgram()`（program.ts:1741-1743/1954 同时暴露给 program 对象）。

### 快照验证的掩蔽效应（M6 的方法学发现）

Roslyn 只要在任何一处遇到**声明级**错误，就跳过**整个编译**的方法体绑定。于是"排除掉某个含声明级错误的文件后，我的文件零诊断"是**不可信**的读数：你会只看到那一处错误，而所有 CS0103/CS1061 一律消失。

本轮两个实例：`Program/Program.Verify.cs` 的 `SymlinkCache` CS0246；以及更隐蔽的一个——生成的 `Model/NodeInterfaces.cs` 引用了两个 Program 侧接口（`IFileIncludeReason`/`IFilePreprocessingDiagnostics`，声明在 `Program/Program.ProgramDiagnostics.cs`），所以"排除 `Program/**`"这一常见做法会让那两个 CS0246 变成全编译的声明级错误。它甚至骗过了有经验的切片：`M6Program` 据此报过"我的文件零诊断"，随后在解除掩蔽的运行里撤回，实测约 85 条机械性错误。

**阳性对照（M6ProgUtils 的做法，值得照抄）**：与其从代码集合反推，不如往**快照副本**的目标文件里注入一个故意的体级错误（如 `NoSuchNameInThePort`）再编译——错误被报出来就证明该配置下方法体确实绑定了，于是"我的文件为零"是强读数。最强的一档是让整编译错误数为 0（不存在任何声明级错误可供掩蔽）然后**运行**产物。快照里把 `NodeInterfaces` 引用的那两个类型（`IFilePreprocessingDiagnostics`/`IFileIncludeReason`）用桩顶上，是等 `Program` 整体可编译之前解除掩蔽的可行做法。

**第二条掩蔽来源：写下一个端口不存在的类型名**（M6NodeBuilder 一般化，我自己也踩过）。生成器把若干 TS 节点**联合**统一放宽成了 `Node`，于是端口里根本没有那个类型名：`EntityName`、`SignatureDeclaration`、`PropertyName`、`BindingName`、`ModuleName`、`HasModifiers`、`HasInferredType`、`EntityNameExpression`、`EntityNameOrEntityNameExpression`、`LateVisibilityPaintedStatement`，以及**裸写的** `Declaration`/`Statement`/`TypeNode`（端口的写法是 `IDeclaration`/`IStatement`/`ITypeNode`）。写错其中任何一个都是 `CS0246` —— **声明级**，于是整个编译的方法体绑定被跳过，所有人的读数一起失真。`BindingPattern` 是同一类：它只是 `ObjectBindingPattern | ArrayBindingPattern` 的联合，没有共同类（M6 期间它作为 `Checker.Types.cs` 的一个状态槽元素类型写下，一次就让全树诊断归零，直到改成 `Node`）。

**写任何类型名之前先跑这一步**：

```
grep -rn "interface <Name>\b\|class <Name>\b\|enum <Name>\b" src/TypeScript.Compiler/Model
```

为空就说明该名字在端口里不存在，不要写——用它的 C# 上界（多数是 `Node`／`INode`／`IDeclaration` 等接口），并在需要收窄处按参考实现的分支做模式匹配。

**判别法（照此读任何快照日志）**：声明级代码集合（CS0246/0101/0102/0111/0535/1513/1733/1002/1026）为空，**且**文件里有非零的 CS0103/CS1061/CS1593 —— 二者同时成立才说明方法体确实绑定了。反过来，只要有声明级代码，文件里为零就什么都不能说明。**修法（两轮实测后的最终形态）**：**保留整个 `Program` 切片不排除**，只在快照副本里桩上两个真正无声明的类型 `RedirectInfo` 与 `LibResolution<T>`（`Program/Program.Graph.cs:343/773/817` 用到）——一次编辑即可让声明级集合归零，不需要排除任何文件。注意 `SymlinkCache` **是已移植的**（`Program/Program.ProjectReferences.cs:1450`），所以"排除 `Program.Verify.cs`"这个常见做法是**自己制造**了那个 CS0246；单独包含 `ProgramDiagnostics.cs` 也无效（它自带 9 个）。

**这不是形式主义——修好取证管道直接换回了真 bug**：`M6Generics.M6Conditional` 在第一次真正未掩蔽的运行里，于自己文件发现三个被历次掩蔽运行掩盖的缺陷（两处 CS8604、一处 CS8602），根因是端口把 TS 的两个重载（`ResolveExternalModuleSymbol` 的可空/非空两条）合并成一个返回 `ISymbol?` 的签名，而调用点对应的是非空那条——一行空值断言修好（同 `Checker.Instantiation.cs` 对 `InstantiateType` 的写法）。这印证了上面那句"被掩蔽的零读数是假象"。

**旧的、已被取代的修法记录**：单独把 `Program.ProgramDiagnostics.cs` 包含回来**无效**——它自己会拉出 9 个声明级 CS0246（它对 `Program` 的兄弟 partial 与 `JsObject` 的引用）。掩蔽只能通过**等整个 Program 切片能编译后整块包含**来解除。在那之前，任何 Checker 切片的诚实证据路径是：文本证据（重复扫描、签名核对、括号配平、`ported from` 范围逐条核对）**加上**一份声明级代码集合为空的日志。

### 残留与跨里程碑指派（M6 期间累积的完整账）

M6 期间报出的每一个"有调用者但无人声明"或"有意不移植"的名字都在这里，附参考位置与归属。§15.16 的残留表由这张表汇总而来。

| 名字 | 参考位置 | 处置 | 依据 |
|---|---|---|---|
| `Program.Emit` / `EmitBuildInfo` / `GetDeclarationDiagnostics`(+Worker) / `GetEmitHost` | program.ts:2688 / 2650 / 2827+3244 / 2598 | **M7**；经 `Program.EmitNotLanded` 响亮抛出，`ProgramTests` 钉住 | 需要发射器 |
| `GetCommonSourceDirectoryFromOptions` / `GetCommonSourceDirectoryOfConfig` / `GetComputedCommonSourceDirectory` | emitter.ts:636 / :685 / :668 | 已声明在 `Program`；**M7 必须调用而不是重申** | 声明在 emitter.ts 但本质是 program 状态 |
| `GetOutputDeclarationFileName` | emitter.ts:563（调用点 Program.cs:304、ProjectReferences.cs:849） | **M7** | 发射路径 |
| `forEachEmittedFile` / `getTsBuildInfoEmitOutputFilePath` | emitter.ts:446-474 / :481-497（调用点 program.ts:4395 / 4588+4609） | **M7** | 同上；无桩 |
| `TsBuild.ResolveConfigFileProjectName` | tsbuild.ts:177-183（调用点 ProjectReferences.cs:880） | **M8** | builder |
| `IProgram.GetBuildInfo()` | types.ts:4934（可选成员）、builder.ts:1686 赋值 | 响亮抛出，消息点名 **M8 的 builder** | 生成的签名非空，表达不了"未赋值" |
| `IProgram.GetModuleSpecifierCache()` / `GetNearestAncestorDirectoryWithPackageJson()` | program.ts | 响亮抛出，**M8-late** | 同上 |
| `Checker.CreateTypeChecker(ITypeCheckerHost) : ITypeChecker` | checker.ts `createTypeChecker` | 签名已冻结；**M6-5 组装时落地** | `Program.GetTypeChecker()` 惰性调用它 |
| `TypeToString` / `SymbolToString` / `SignatureToString` / `TypePredicateToString` | checker.ts:6240 / 6169 / ~6202 | **M6-6**，受 printer 门控 → 响亮抛出 + 测试钉住 | §15.15 裁定 1 |
| `moduleSpecifiers` 的两个调用点 | checker.ts:8534 / 8638 | **M6-late / M8**，不提前引入那 1,470 行 | §15.15 裁定 2 |
| `nodeCoreModules` | utilities.ts:12165-12171 | 登记不移植（两个输入已落地，一行即可组装） | 无调用者 |
| `tryGetClassExtendingExpressionWithTypeArguments` / `tryGetClassImplementingOrExtendingExpressionWithTypeArguments` | utilities.ts:7444-7447 / 7455-7468 | 登记不移植 | 无调用者（留给 checker.ts:50042/50392 的切片） |
| `_globalRegExpType` / `_globalThisType` | checker.ts:2265 / 2266 | 登记不移植 | 无读者（赋值随 M6-5 的 `initializeTypeChecker`） |
| `ICancellationToken` 无任何实现 | — | 记录为假设：`Program` 的 `runWithCancellationToken` 以 `OperationCanceledException` 过滤 | 实现属 M8 的 CLI/host；测试可用 fake |
| ~~`Utilities.SupportedDeclarationExtensions`、`FactoryUtilities.IsJSDocTypeAssertion`~~ | §15.10 / §15.9 | **已关闭（M5/M6 期间落地）**：`Utilities/Utilities.Parser.cs:363`（`readonly IList<string>`，三个声明后缀）与 `Parser/FactoryUtilities.cs:509`（`IsParenthesizedExpression && IsInJSFile && GetJSDocTypeTag is not null`，其依赖 `getJSDocTypeTag` 随 `Utilities/Utilities.JSDoc.cs` 落地），两者都在 `ported from` 注释里记了这段历史 | M4e/M4d 记的残留（已清） |

已**关闭**（曾报为缺口、实测已落地或系取证假象）：`ElaborateNeverIntersection`（`Checker.Members.cs:933`）、`GetSignatureInstantiation`（`Checker.TypeSupport.cs:1847`）、`GetSignatureInstantiationWithoutFillingInTypeArguments`（`:1877`）、`isArrayOrTupleSymbol`/`isReadonlyArraySymbol`（`Checker.TypeSupport.cs:1407/1419`）、`Utilities.IsTypeDeclaration`（`Checker/Utilities.Checker.cs:465`）、`Utilities.GetInterfaceBaseTypeNodes`（`Utilities/Utilities.Checker.cs:400`，参数 `InterfaceDeclaration`）、`Utilities.GetEffectiveImplementsTypeNodes`（同文件 `:381`，参数 `IClassLikeDeclarationBase`）、`15871`/`15799` 段、`16577-16674` 段。

**可发现性陷阱（记下以免后人再踩）**：`Utilities` 的 partial 分散在多个目录且**同名文件存在两份**——`Utilities/Utilities.Checker.cs`（heritage 辅助）与 `Checker/Utilities.Checker.cs`（`IsTypeDeclaration` 等）。加上 `Checker/Checker.Support.cs` 与 `Program/Utilities.Program.cs`、`Program/Program.ProgramDiagnostics.cs` 里各自的 `Utilities` partial，"这个名字在哪个类里"不能靠**文件名**判断，只能按声明形状定位后读**包住它的类**。本里程碑为此付出了三轮返工，也因此把判据写进了本节的偏差表开头。

行号提示：本节所引行号是各次复核当时的快照；文件仍在被追加，`M6LateBinding` 复核时 `ElaborateNeverIntersection` 已从 `:928` 漂到 `:933`、`GetSignatureInstantiation` 从 `:1842` 漂到 `:1847`——按名字与参考范围检索，不要按行号。

### 生成器缺口（M6 期间修掉）

`MapUnion` 的公共基只看 C# 的**类**链，而对没到达 `Node` 的联合会退化成 `Node`——两处显形：`IProgram.getFileProcessingDiagnostics()` 生成为 `List<Node>?`，`getFileIncludeReasons()` 生成为 `Dictionary<string, List<Node>>`，而端口的值分别是 `List<IFilePreprocessingDiagnostics>` 与 `List<IFileIncludeReason>`（TS 的 `readonly FilePreprocessingDiagnostics[]` 与 `Map<string, FileIncludeReason[]>`）。修法与 M5-0 的 `FlowNode`、M6-0b 的 `SymbolTable` 同一机制：两条 `s_overrides`。容器形状不动（§12.3 的 `Map`→`Dictionary` 本就是对的），`Program` 在边界上构造它。

### 四项裁定

1. **受 printer 门控的字符串化**（`typeToString`/`symbolToString`/`signatureToString`/`typePredicateToString` 用到 `checker.ts:6194-6264` 的 `createPrinter*`，属 M7）：采用端口既有惯例 —— **显式命名 + 测试钉住的 loud blocker**（先例是 `FactoryUtilities.IsJSDocTypeAssertion`，§15.9），并逐条进本节的残留清单。不使用静默降级：静默的错字符串会污染 `.types` baseline，而 loud blocker 会在 M7 接上时立刻变成绿灯。
2. **`moduleSpecifiers` 的两个调用点**（`checker.ts:8534`/`8638`）：归 M6-late，作为 M6-6 的契约项，不在 M6 主体里提前引入那 1,470 行。
3. **`Type`/`Signature` 的构造子收 `object?` 而不是 `ITypeChecker`。** 参考实现自己也只在 `Debug.isDebugging || tracing` 时才把 checker 存进那个字段（`utilities.ts:8489-8503`）——它纯粹是给调试器/服务层读的，checker 本身从不读它。端口因此把参数收成 `object?`、体内 `as ITypeChecker`：代价为零，却让"构造一个类型"不再依赖 `Checker` 类已经实现完那个 380 成员的接口（只有 M6-5 的组装能做到）。等组装完成后同一个 `as` 自然开始成立，**任何调用点都不需要再改**。这条解开的不只是 21 个编译错误，而是"在 M6-5 之前所有 checker 测试都跑不起来"。
4. **checker 的交付形态是 `Checker : ITypeChecker`**，与 `NodeFactory : INodeFactory` 同形：TS 的 `const checker: TypeChecker = {…}` 是一个字面量，而端口的成员是 landed partial 的实例方法，所以要由类实现那个生成的接口（380+ 成员，M6-5 组装完成后才可能转换成功；中间的"无法转换"是预期状态）。若到 M6-5 仍有接口成员表达不了（M4d 的 `INodeFactory` 有 6 个这样的），走 §15.9 的"接口比类大"处理：逐条指名 + 反射测试钉住差集，而不是给出发不出正确结果的实现。

---

## 15.16 M6：Layer 5（`Program` + `Checker`）交付记录

**结论：M6 达标。** 编译器项目 **0 错误 / 0 警告**，测试从"从未执行"变为 **1,044/1,044 通过**，两条差分外环在**同一棵树**上仍然零分歧。计划、裁定、偏差表、残留账与 M7 边界清单见 §15.15，本节只记交付与实测数字。

### 交付物（实测行数）

| 区域 | 文件 | 行数 | 覆盖的参考范围 |
|---|---|---|---|
| checker（含身体、装配、node builder） | `Checker/*.cs` | **55 个文件 / 84,739 行** | `checker.ts` 1318-54434 的主体范围 |
| 其中：检查身体 + 装配 | `Checker.Check*.cs`、`Checker.Initialize.cs`、`Checker.CreateTypeChecker.cs` | 14 个文件 / 26,487 行 | 33262-34739、37810-53668、53761-54200；`createTypeChecker` 180/180 抽象成员 |
| 其中：node builder + 协调 | `NodeBuilder.{Types,Members,Signatures,Names,Declarations,Serialize}.cs` + `Checker.NodeBuilder.cs`、`Checker.Stringify.cs`、`SyntacticBuilderResolver.cs`、`EmitResolver.cs` | 10 个文件 / 9,736 行 | 5980-11323、51380-51554、54260-54434 |
| Program | `Program/*.cs` | **21 个文件 / 18,735 行** | `program.ts` 主体、`programDiagnostics.ts`、moduleSpecifiers 宿主 |
| M6 合计 | — | ≈103,500 行 | Layer 5 |

### 五条验收（集成点独立复跑，非切片自述）

| # | 命令 | 结果 |
|---|---|---|
| 1 | `dotnet build src/TypeScript.Compiler --nologo --no-incremental` | **0 错误 / 0 警告** |
| 2 | `dotnet test --nologo` | **1,044 通过 / 0 失败** |
| 3 | `dotnet run --project tools/Gen -- --check` | 无漂移（全部生成目标 up to date） |
| 4 | `tools/AstDiff`（全语料） | **files=19585 same=19585 diverged=0** |
| 5 | `tools/BindDiff`（全语料） | **files=19233 same=19230 diverged=0 port-crash=0**（余 3 为参考自身栈溢出） |

错误趋势（唯一计数，全程 0 警告）：794 → 725 → 672 → 656 → 639 → 619 → 555 → 521 → 505 → 472 → 469 → 474 → 434 → 360 → 272 → 268 → 246 → 232 → 176 → 156 → 134 → 132 → 100 → 79 → 70 → **0**。

### 收口时 11 个测试失败的处理（7 端口缺陷 / 4 错误期望 / 5 测试或环境缺陷，无一条测试被削弱）

| 类别 | 例 |
|---|---|
| **端口缺陷（真 bug，只有能跑的测试能发现）** | `Program.IsSourceFileDefaultLibrary` 此前是**自造的无限递归**（`libFiles → GetSourceFileFromReference → 自身`），参考只有一行 `libFiles.has(file.path)`（program.ts:2680-2682），且它还答错了问题；`Checker.GetTypeOfExpression` 用 node id 直接索引 `List<IType>` 越界抛出——参考是**稀疏数组**，越界读为 undefined、越界写会扩张；`Program.VerifyProjectReferences` **急切**求值 `getTsBuildInfoEmitOutputFilePath`（参考 program.ts:4588 只在 `forEachProjectReference` 的回调里读，而该回调无 project references 时不会执行），使 M6 路径的 `createProgram` 撞上 M7 响亮阻断 |
| **错误期望** | 4 条，均给出参考行号后改测试语义（不是放宽） |
| **测试/环境缺陷** | 5 条，含两个测试助手把 stub 库放在 `"/" + GetDefaultLibFileName(options)`，而宿主的名字是 `combinePaths(getDefaultLibLocation(), …)`（program.ts:467/475）→ 从未解析到 stub；以及 `CrossSliceOwnershipTests` 里 `Type` 被解析成 `TypeScript.Compiler.Type`（应 `System.Type`） |

**方法教训（写入纪律）：** 测试**必须能跑**——M6 的实质缺陷几乎全部由"第一次运行的测试"和"注入式阳性对照快照"两类证据发现，而非靠读代码；只编译通过不算验证（§15.15 的两条掩蔽危害即为此）。

### 响亮阻断与 M7/M8 边界（M6 只留阻断，不留占位）

printer 门控的 `TypeToString`/`SymbolToString`/`SignatureToString`/`TypePredicateToString` 及其 `write*` 转发；`Program.Emit`/`EmitBuildInfo`/`GetDeclarationDiagnostics`/`GetEmitHost`/`GetTsBuildInfoEmitOutputFilePath`；`IProgram.GetBuildInfo`（M8）；`Program.GetModuleSpecifierCache`/`GetNearestAncestorDirectoryWithPackageJson`（M8）；`Checker.SyntacticNodeBuilder`（M4f 的 `expressionToTypeNode.ts`）；`moduleSpecifiers` 的两个调用点（M6-late/M8）；生成器缺陷 `Model/NodeInterfaces.cs:3526` 的 `GetIndexInfosOfIndexSymbol`（参考 types.ts:5083 是函数类型）。逐条清单与所有者见 §15.15 的残留账/M7 边界表。

### `tools/TypeDiff`（最强验收）首次端口侧运行

首轮已跑通并**给出了实测清单**（本工具此前从未在端口侧执行过）。同一棵树上的结果：

| 语料 | files | same | diverged | port-crash | ts-error | 耗时 |
|---|---|---|---|---|---|---|
| `conformance`，`--filter classes/** --limit 20` | 20 | 2 | 0 | 18 | 0 | <1 s |
| 同上 `--limit 200` | 200 | 23 | 37 | 140 | 0 | — |
| **全 `conformance`（5,908 文件）** | 5,908 | **1,058** | **801** | **4,049** | 0 | 32 s（端口峰值 69-241 MiB） |

**判定语义先修正过：** 工具原先在"什么都没比成"时返回 exit 0（本该是 1），且 `Program.VerifyCompilerOptions` 撞 M7 阻断是**参考的真实路径**（`program.ts:4392` 的 `if (!noEmit && !suppressOutputPathCheck)` 就是端口 `Program.Verify.cs:462-472` 的原文，删除它会静默丢掉 `verifyEmitFilePath` 的输出路径校验），因此修的是 harness 而**不是**那个守卫：`tools/TypeDiff` 的端口侧与 node 侧现在都设 `SuppressOutputPathCheck = true`（保持两半选项集一致），并打印可审计的一行 `typediff: VERDICT …`。

**崩溃分类（首轮 4,049 → M6-late 第一轮后 2,682，实测再分类）：** 其中 **2,671 条（99.6%）就是 M7 门控本身**（按 §15.15 裁定 1、非缺陷）：`TypeToString` 2,080、`SymbolToString` 489、`SyntacticNodeBuilder` 102；**非 printer 的只有 11 处**，现场已定位：
- 7 处 `JsxAttribute -> INamedDeclaration` @ `Checker.Relations.Structured.cs:955`（`HasExcessProperties`，参考 `checker.ts:36386` 的联合读取）
- 1 处 `ClassExpression -> ClassDeclaration` @ `Checker.Queries.cs:422`（`GetReturnTypeFromAnnotation`）
- 1 处 InvalidCast @ `Checker.Conditional.cs:1381`
- 1 处 `TypeReferenceNode/TupleTypeNode -> IDeclaration` @ `Checker.Instantiation.cs:431`（`GetObjectTypeInstantiation`）
- 1 处 `QualifiedName -> Expression` @ `Checker.Check.cs:1840`（`CheckNonNullExpression`）

**638 条分歧的分组（降序）：** 200 条诊断码错配（**主导且是真缺陷**：端口在 `getCannotFindNameDiagnosticForName`（`checker.ts:27734-27742`）该报**定位过的 2583** 时报了**全局 2318** `Cannot find global type`，经 async/Promise 全局取值可达）；191 条类型标志（56 条仅 id 不同 + props/obj）；91 + 23 条记录种类 `(diagnostics vs diagnostic)`（同一行上"诊断数组 vs 单个诊断"的形态错配）；43 条 node-vs-symbol；38 条符号标志；20 条单侧放弃；14 条 type-vs-node；8；5；3。

**M6-late 第二轮（同一命令，集成点独立复跑）：`same=2938 diverged=295 port-crash=2675`（`same` +350 / `diverged` −343，零回归：测试 1,044/1,044、AstDiff 19,585/19,585、BindDiff 19,230/19,230）。本轮三个真缺陷：`Checker.IsErrorType` 的函数体；`GetPropertiesOfType` 的**表观类型归约**；**14 个延迟全局取值器把回退放在记忆化之外**。**

**A′ 的裁定前提被实测推翻（重要的方法记录）：** A′ 按裁定实施完毕（`Diagnostic.MessageText` 惰性+记忆化；参考里三处 spread 诊断的副本改走 `ShareMessageText`；比较器本就在 code/位置之后才读 `messageText`，与 `utilities.ts:8787-8794` 一致），但**全语料逐位无移动**（2588/638/2682 → 2588/638/2682）。原因是那个字符串在**调用点**就已算出，与参考完全一致——`reportImplicitAny` 在 `checker.ts:26105` 先算 `typeToString(getWidenedType(type))` 再创建诊断，因此 `Diagnostic`/`FormatMessage` 内的任何惰性化都无济于事。崩溃直方图给出 **83 条不同调用链**（前五：`ReportImplicitAny` 1,170、`CheckTypeRelatedTo/ReportRelationError` 301、`CheckIdentifier` 256、`ReportNonexistentProperty` 249、`SyntacticNodeBuilder` 102）。

**门控的掩蔽量已被量出（对 M7 后的工作量是硬数据）：** 用一次性构造让四个 printer 门控改**返回 writer 而非抛出**（跑完即回滚、文件逐字节还原），全语料为 `same=3774 diverged=1679 port-crash=455` —— 即实参级延迟能把 `same` 抬升约 836，但同时让 `diverged` **增加约 1,041**：**门控当前正掩蔽着约 1,041 条真实分歧**（例如 2318/2583 类在其中是藏着的）。因此裁定：**保持门控响亮**、**保留 A′**（忠实，且严格更优——三处副本不再强制格式化），而"实参级延迟"（约 200 个调用点、83 条首崩链上引入惰性诊断实参包装）**是一项新偏差，需在 295 条分歧收完之后单独裁定**，以免两种效应混淆。

### M6-late 第三轮：11 处非 printer 崩溃现场全部关闭，剩余崩溃 100% 是里程碑门控

**同一命令、集成点独立复跑：`same=3042 diverged=187 port-crash=2679`（`same` +104 / `diverged` −108）。** 全语料 **`InvalidCastException`/`NullReferenceException` 归零**，`port-crashes.txt` 只剩 M7 门控：`TypeToString` 2,080 / `SymbolToString` 497 / `syntacticNodeBuilder` 102。唯一上升的 +7 崩溃，全部是门控在**新跑通**的检查路径上首次触发（诚实进展，非回归）。

| 现场 | 6.0.3 依据 | 采用的形状（含 Go 端参考） |
|---|---|---|
| 7× `JsxAttribute → INamedDeclaration` @ `Checker.Relations.Structured.cs:955` | `checker.ts:22980-23060` 的 `isJsxAttribute(prop.valueDeclaration)` 分支（`:22996`）；`types.ts:3274` 中 `JsxAttribute extends Declaration`（**不是** `NamedDeclaration`），故 `name` 槽只有它 | 转 `JsxAttribute`（`tsc/internal/checker/relater.go` 同为 kind 判别） |
| `ClassExpression → ClassDeclaration` @ `Checker.Queries.cs:438/:450` | `checker.ts:16512-16514/16519` 的 `declaration.parent as ClassDeclaration` 是无检查断言，且只读 `.symbol` | 转二者共享的 `IClassLikeDeclarationBase`（`Model/NodeInterfaces.cs:170`） |
| `JSDocNonNullableType → ParenthesizedTypeNode` @ `Checker.Conditional.cs:1381` | `checker.ts:20496-20499` 的联合 `ParenthesizedTypeNode \| JSDocTypeReferencingNode \| …` | 按 kind 读同一个 `ITypeNode? Type` 槽（`checker.go:2553` 式判别） |
| `TupleTypeNode/TypeReferenceNode → IDeclaration` @ `Checker.Instantiation.cs:432` | `checker.ts:20740-20758`：`allDeclarations` 虽按 `symbol.declarations` 定型，实际装的是类型节点，只当 `Node` 读 | `List<Node>` + `Core.Map(… (Node)d)` —— 用基类/泛型表达形状，不复制按类型退化的辅助函数（§9.x 的泛型反指示） |
| `QualifiedName → Expression` @ `Checker.Check.cs:1840` | `checker.ts:34639-34641` 参数即 `Expression \| QualifiedName`（`:41753` 同），端口 `CheckExpression` 早收 `Node` 并按 `SyntaxKind.QualifiedName` 分派 | 去掉强转 |

**轮内阶梯**（第三轮起每轮都从 `report.txt` **重新分组**，不沿用前轮结论）：2,942/294/2,672（结构型转换）→ 2,990/246（`IsDeclarationName`）→ 3,028/208（export specifier 的角色）→ 3,042/187/2,679（延迟 drain）。

### M6-late 第四轮：`diverged` 187 → 85

**同一命令、集成点独立复跑：`same=3135 diverged=85 port-crash=2688`（`same` +93 / `diverged` −102；+9 崩溃全部是门控在**新跑通**的检查路径上首次触发）。**

| 组（本轮再次从 `report.txt` 重新分组） | 依据与修法 |
|---|---|
| 类型标志/类型-符号（全部是 TemplateLiteral 的 `types=` 记录） | **工具侧读取分歧（非端口缺陷）**：`ts-type-dump.cjs:194` 是 `Array.isArray(type.types) ? type.types : undefined`，而端口半只在 `IUnionOrIntersectionType` 上读该槽，导致模板字面量打印 `types=-`。修 `tools/TypeDiff/Program.cs`：凡声明该槽的模型都读（`IUnionOrIntersectionType` / `ITemplateLiteralType`），README 的 v1 措辞同步。**格式未变**——两半改为读同一批槽位，只会暴露更多而非更少 |
| 符号标志：环境模块 `exports` 表为空（37 条） | **真 binder 缺陷**：端口测的是**容器**（`!IsAmbientModule(container)`）而非**声明**（`node`）；参考 `binder.ts:911`（其上方 `:902-907` 的注释正是节点测试所实现的"按声明判定"）。于是 `declare module "x" { export var y }` 里的 `export` 落进 `locals`、模块符号 `exports` 保持为空（如 `externalModules/topLevelAmbientModule.ts:18`）。修 `Binder/Binder.Declarations.cs` 为 `!Utilities.IsAmbientModule((Node)node)` + PORT-NOTE |
| "单侧放弃"：TypeQuery 节点（11 条） | `checker.ts:50386-50396` 的 `isExpressionNode(node)` → `getRegularTypeOfExpression(node as Expression)`，而 `isExpressionNode` 对**类型查询下的限定名**故意答真（`utilities.ts:3677-3680`）——参考自己的强转是**类型层的谎**，由它自己的 `checkExpression(Expression \| QualifiedName)`（`checker.ts:41737`）兜住。端口 `(Expression)node` / `(Expression)expr.Parent!` 因此直接抛 InvalidCast（表现为 `N… sym=! type=! sig=!`）。按其真实并集读取 |

**轮内阶梯**：3,042/187/2,679 → 3,122/108/2,678（模板字面量槽 + binder 环境模块）→ **3,135/85/2,688**。

### M6-late 当前状态与续做指引（本步按用户指示停下）

**仓库停在 `8e6ea86`，工作区干净**；构建 0 错误 / 0 警告、测试 **1,044/1,044**、`Gen --check` 无漂移、`AstDiff` **19,585/19,585**、`BindDiff` **19,230/19,230**、`TypeDiff` 全 `conformance` 语料 **`same=3135 diverged=85 port-crash=2688`**（崩溃 100% 是 M7 printer 门控）。

**四轮收敛总账**：`same` 1,058 → 2,588 → 2,938 → 3,042 → **3,135**；`diverged` 801 → 638 → 295 → 187 → **85**；`port-crash` 4,049 → **2,688**，其中**非门控崩溃已归零**（全语料 `InvalidCastException`/`NullReferenceException` = 0）。

**已授权、尚未落地的一项决定（续做时从这里开始）——实参级延迟：** （**已落地**：见下节「M6-late 第五轮」，前提 1-4 逐条满足、三个测量点已报。） 参考在调用点就急切算出诊断实参里的 `typeToString(...)`/`symbolToString(...)`（如 `checker.ts:26105` 的 `typeAsString = typeToString(getWidenedType(type))`，`ReportImplicitAny` 一类独占 1,170 个文件；实测 83 条首崩链）。授权把这些实参改为惰性（仅在消息文本真的被读取时才计算）。**实施前提（绑定）**：

1. **printer 门控保持响亮**——任何读取消息文本的路径仍须撞上 M7 阻断并**抛出**；不得实现 printer、不得吞异常、不得给占位文本；
2. 不读文本的路径可观察行为不变；
3. dump 格式保持 **v1**（不为它改任何记录形状）；
4. 排序/去重的平局读取点**不得移动**（参考 `utilities.ts:8787-8794` / `8917-8926`）；
5. 必须报告**三个测量点**：(a) 起始树、(b) 仅落地延迟、(c) 每轮修复后——其中 (b) 预期使 `diverged` **变差**（被掩蔽的真实缺陷首次显形），这是目的而非回归，不得隐瞒。

**收益（已实测，非估计）：** 让四个门控临时改**返回 writer** 时全语料为 `same=3774 diverged=1679 port-crash=455` —— 即门控当前掩蔽着约 **1,041 条真实分歧**。

**其余待办（不属本工作流）：** §15.15 的 M7/M8 响亮阻断清单（printer 门控的四个字符串化、`expressionToTypeNode`（M4f）、~~`moduleSpecifiers` 的两处调用点~~（**已落地**：`src/TypeScript.Compiler/ModuleSpecifiers.cs` + `Checker/NodeBuilder.Names.cs` 的 `getSpecifierForModuleSymbol` 调用点与 `sortByBestName` 的 `countPathComponents`；见 §17.14.11 末）、`Program.Emit`/`EmitBuildInfo`/`GetDeclarationDiagnostics`/`GetEmitHost`/`GetTsBuildInfoEmitOutputFilePath`、`GetBuildInfo`（M8））、`Model/NodeInterfaces.cs:3526` 的生成器缺陷（`GetIndexInfosOfIndexSymbol`）、以及 §15.15 的残留账与未认领名单。

### M6-late 第五轮：实参级延迟（裁定 A″）落地与三项测量点

**结论：A″ 已落地（253 处调用点 / 24 个 checker 文件 + 3 个机制文件），前提 1-4 逐条满足；收益方向与预期一致（`same` 上升、`diverged` 因显形而变差、崩溃下降），但幅度小于"门控改返回 writer"的上界 —— 原因已实测定位（门控与"平局读文本"相撞，见下）。**

#### 落地内容

| 位置 | 内容 |
|---|---|
| `Core/Diagnostic.cs` | `internal sealed class DiagnosticArgument`（记忆化 `Func<string?>` thunk）+ `Diagnostic.DeferArg(...)`；`DiagnosticMessageChain` 新增 `Deferred(message, args)`，其 `MessageText` 与 `Diagnostic.MessageText` 同形（首次读格式化并记忆） |
| `Utilities/Utilities.Diagnostics.cs` | `ChainDiagnosticMessages` 改走 `DiagnosticMessageChain.Deferred`（A″ 的链级形态；参考 utilities.ts:8751-8765 在构造时就格式化） |
| `Utilities/Utilities.Parser.cs` | `FormatStringFromArgs` 增加 `DiagnosticArgument` 分支（其值为空时渲染成 `"undefined"`，与既有 null 实参同一文本） |
| `Checker/*.cs`（24 个文件、253 处） | 四个 printer 门控字符串化**只作诊断消息实参**的调用点改为 `Diagnostic.DeferArg(() => <原调用>)`；同一串喂多个实参时只建一个记忆化 thunk，参考的一次计算仍是一次（`Checker.IndexAccess.cs:996`、`Checker.Inference.cs:9592`） |
| 跨文件管道（全仓库唯一一处） | `Checker.Diagnostics.cs:227` 的 `AddDeprecatedSuggestionWithSignature` 第 4 参数 `string` → `DiagnosticArgument`，唯一调用点 `Checker.Check.Calls.cs:161` 相应改为 thunk |
| 测试 | `tests/TypeScript.Compiler.Tests/Core/DeferredDiagnosticArgumentTests.cs` 5 例：未读不求值、文本与急切版一致、链级同样延迟、抛出的 thunk 在读取时仍抛（前提 1）、平局排序**确实**读文本且顺序按文本（前提 4） |

**有意保持急切的站点**（逐条有理由，仍是"读文本即撞门控"的路径）：`Checker.Stringify.cs:578-579`（`GetTypeNamesForErrorDisplay` 要**比较**两串）、`Checker.SymbolSupport.cs:940-941`（`GetFullyQualifiedName` 返回串，调用方还会拼串）、`Checker.Symbols.cs:278`（串作字典键）、`Checker.Relations.Structured.cs:966`（`propName` 交给 `GetSuggestedSymbolForNonexistentJSXAttribute` 查表）、`Checker.Check.Classes.cs:1158-1160`（存进 `notImplementedInfo` 元组供后续诊断）、`NodeBuilder.Members.cs:748`（交给 tracker）、`Checker.Stringify.cs:133/134/185/186/196`（`ErrorSymbolName`/`ErrorModuleName`，M7 发射读的字段）、`Core/Tracing.cs:459`（tracing 自身）。

#### 独立审计（另一个 agent 按五条规则逐 hunk 复核，非自述）

- **1 处真违规（规则 2 可变捕获）**：`Checker.Check.cs:558` 的 thunk 读局部 `member`，而该局部在下方 `member = prop` 之后被改写，参考是在改写**前**取串（checker.ts:33609 对 :33621）——改为把当时的符号钉进 `memberForDiagnostic` 供 thunk 读。
- 3 处"可变但可证无害"（重赋值落在互斥/不可达分支：`Checker.Check.cs:1769`、`Checker.Relations.Entries.cs:519/641`）按要求登记。
- 2 处把**非**门控的 `EntityNameToString` 也包了 thunk（无收益、文本相同，保留并登记）。
- `Checker.Relations.Structured.cs:968` 的 `if (suggestion is not null)` 取代参考的串真值测试，是 A″ **之前**就有的既有偏差，非本轮引入。

#### 三个测量点（同一命令全语料 5,908 文件）

| 点 | 树 | same | diverged | port-crash | 落盘 |
|---|---|---|---|---|---|
| (a) | 起树（`8e6ea86` 代码树） | 3,135 | 85 | 2,688 | `artifacts/M6Late5-a` |
| (b) | 仅落地 A″ | **3,303** | **198** | **2,407** | `artifacts/M6Late5-b` |
| (c) | 修掉审计违规 + 第一处显形缺陷后 | **3,551** | **160** | **2,197** | `artifacts/M6Late5-c` |

**A″ 第一次显形出来的真缺陷（本轮已修）**：`Checker.Support.cs` 的 `InitializerOfNode` 起初只覆盖 `VariableDeclaration | PropertyAssignment` 两支（它本来只服务 `IsJSConstructor` 的二元 guard），但 `Checker.TypeOfSymbols.cs:587` 把它用在参考 `checker.ts:11942` 的 `hasOnlyExpressionInitializer(declaration) && !!declaration.initializer` 上 —— 那里的并集是 `HasExpressionInitializer` 六个面（types.ts:1287-1291）。于是**每个 `class C { p = 1 }` 的 `p` 都取不到初值**，掉进静态块 flow-type 支，属性类型退成 `autoType`→`any`，并连带多报 implicit-any 错误。实例：`classes/classStaticBlock/classStaticBlock12.ts` 端口多报 `7008`，同一节点的类型记录 `T#3 = any(id=1)` 而参考为 `T#3 = Number(id=15)`（该文件在 (a) 是崩溃、在 (b) 才是分歧，正是被 A″ 解锁的）。修法按参考并集补全六支：**(c) 相对 (b) `same` +248 / `diverged` −38 / `port-crash` −210**（测试仍 1,049/1,049、`AstDiff`/`BindDiff` 不变——两者不经过 checker）。

零回归：build 0 错误 / 0 警告、测试 **1,049/1,049**（1,044 + 新增 5）、`AstDiff` **19,585/19,585**、`BindDiff` **19,230/19,230**。

#### 为什么只有 +168：门控与"平局读文本"相撞（实测定位）

(b) 的崩溃直方图：`TypeToString` 1,856 / `SymbolToString` 439 / `syntacticNodeBuilder` 111。其中 **2,295/2,407 条（95%）的首崩已不在调用点，而在 `DiagnosticCollection.Add` → `Core.InsertSorted` → `BinarySearch` → `Utilities.CompareMessageText`** —— `insertSorted` 用的是参考自己的比较器，而 `compareMessageText`（utilities.ts:8807-8846）**在 file/start/length/code 全等时读两侧的消息文本**。临时插桩统计到 220,138 次文本读取，其中 **210,657 次是同一个"`code=2318`、无 file/start/length"的全局诊断之间的平局**（没有默认库时每个文件都重报同一批 `Cannot find global type`，靠消息文本去重）。

前提 1 要求门控保持响亮 ⇒ 这些文件必然继续崩。**结论：A″ 只能解锁"从不读消息文本"的文件**；§15.16 记下的约 1,041 条被掩蔽分歧仍要等 M7 的 printer 才能取出（那次 `same=3774 diverged=1679` 的上界正是"门控不抛"的产物，不是 A″ 的上界，本节更正）。

#### per-node `!`（查询抛异常）分析：其中一大类**参考自身也抛**

`!` 表示 dump 调 `getSymbolAtLocation`/`getTypeAtLocation` 时该节点抛异常。全语料 2,140 条 = **1,718 `NullReferenceException` + 422 门控 `NotSupportedException`**。NRE 的构成是 1,556 处 `SourceFile`（kind 308）+ 162 处 `ImportClause`（kind 274）。用 `node` 直探参考（`lib/typescript.js` + 单文件 program）证实**参考对这两类节点同样抛**：external module 的 `SourceFile` 走到 `isDeclarationNameOrImportPropertyName` 的 `name.parent.kind`（父为 `undefined`），`import type { … }` 的 `ImportClause` 走到 `getDeclaredTypeOfSymbol(undefined)` 的 `.flags`。两侧同时 give-up 时 dump 行完全相同，**不计分歧** —— 这正是 (b) 里"单侧放弃"只有 73 条的原因。

#### (c) 的 160 条分歧构成（下一轮起点）

| 组 | (b) | (c) | 性质 |
|---|---|---|---|
| 单侧放弃（`=!`） | 73 | 74 | 多为门控（M7）；须逐条区分 |
| 类型标志 / 类型-符号 | 61 | 69 | 含类型 id 对齐（创建顺序）与新显形缺陷 |
| 记录种类 `diagnostics vs diagnostic` | 55 | 10 | 端口多报/少报诊断；`InitializerOfNode` 那一类已修（55→10） |
| 记录种类 `diagnostic vs diagnostics` | 8 | 0 | 同上反方向 |
| 诊断码错配 | 8 | 6 | 例 `es6/destructuring/restElementWithNullInitializer.ts` 端口 2531 对参考 2461（span 也不同） |
| node vs signature | 1 | 1 | — |

(c) 的 2,197 条崩溃里，按异常类型仍是 `TypeToString` 1,617 / `SymbolToString` 460 / `syntacticNodeBuilder` 114，另有 **6 条 `InvalidCastException`**（非门控，是这次才跑到的新现场，须逐条修）；按首崩机制，(b) 时实测的 `DiagnosticCollection.Add` 平局读文本仍然是主导（前提 1 与参考自己的比较器相撞，M7 才能解除）。

### M6-late 第六轮：A″ 解锁出的真缺陷（8 个切片）与收敛边界

**同一命令全语料 5,908 文件：`same=3637 diverged=80 port-crash=2191`（(c) 为 3551/160/2197）。** 构成：**69 条 id-only 类型分歧全部消失**；**80/80 条分歧都是"M7 printer 门控"**（单侧 `=!`，全部 `NotSupportedException`）；**2,191/2,191 条崩溃都是门控**（`TypeToString` 1,620 / `SymbolToString` 460 / `syntacticNodeBuilder` 111）——**非门控分歧与崩溃均为 0**，即 M6 的收敛已到达 M7 边界，工作树内不再剩可修项（除下面两项需显式裁定的选项）。零回归：build 0/0、测试 **1,049/1,049**、`AstDiff` **19,585/19,585**、`BindDiff` **19,233 文件 / same 19,230 / diverged 0 / port-crash 0**（余 3 条 `ts-error` 是参考自身栈溢出，与 §15.16 基线一致）。

| # | 缺陷（端口 vs 参考） | 参考依据 | 效果 |
|---|---|---|---|
| 1 | `needCheckWidenedType` 掉了参考的前导 `!`：`!some(elements, not(isOmittedExpression))` 被译成 `some(...)`（谓词整个反转） | checker.ts:45139 | 解构组 16 文件：缺 `2322`/`2532`，且空模式不再在检查期计算模式类型 ⇒ 缺 `2318`（`Iterable`） |
| 2 | `StructuredTypeRelatedToWorker` 把参考的三项**析取**折成一个 `sourceFlags & (…)` 掩码：第二项丢掉 `&& targetFlags & Union`，第三项因 `TypeFlags.Intersection` 不在 `Object\|Union\|Instantiable` 掩码内而在 C# 里恒为 0 | checker.ts:23621-23627 | 语义 + 类型 id（少建 `getTypeWithThisArgument`/`getIntersectionType`） |
| 3 | `GetContextualType` / `GetContextualJsxElementAttributesType` 把参考的 falsy `!contextFlags` 写成 `contextFlags is null`，漏掉显式传入的 `ContextFlags.None`(0) ⇒ 缓存失效、重复创建类型 | checker.ts:32788 / :32949 | 类型 id（每个字面量多建 2-3 个类型） |
| 4 | `IsTypeOnlyImportDeclaration` 的 `ImportSpecifier` 支多走一层父链（读 `ImportDeclaration.ImportClause`），而参考是 `specifier.parent.parent`＝`NamedImports.parent`＝`ImportClause` | utilitiesPublic.ts:1536-1548 + types.ts:3790-3794 | `externalModules/typeOnly/*` 4 文件缺 `1361` |
| 5 | `TypeArgumentsOfUnion` 少 `JsxSelfClosingElement` 支（参考 5 支、端口 4 支）⇒ 自闭合 JSX 的类型实参丢失、签名实例化全部命中同一 memo | types.ts:3250 + checker.ts:35569-35571 / :36755 | `tsxTypeArgumentsJsxPreserveOutput` 记录错位 |
| 6 | 三处 unchecked 强转：参数属性→`PropertyDeclaration`（checker.ts:3166 的并集）、`node.Parent`→`TypeLiteralNode`（:53398）、`GetContainingFunction`→`IFunctionLikeDeclarationBase`（:45100-45104 / :45129 读无 `body` 的签名） | 见左 | 6 条 `InvalidCastException` + 1 处潜在现场，全部改为安全形状后归零 |
| 7 | `CheckPropertyNotUsedBeforeDeclaration` 的第三个 guard 读错槽位：端口读 `IsAccessExpression(node.Parent!)`，参考是 `isAccessExpression(node.expression)`（checker.ts:35094）⇒ 整个 `2729` 分支被跳过 | checker.ts:35094 | 语料最后一条真分歧 `initializationOrdering1.ts` 回到 same |
| 8 | `InitializerOfNode` 的并集（前一轮已修，见上） | checker.ts:11942 + types.ts:1287-1291 | same +248 / crash −210 |

**两项有意不落地的选项（留给下一次显式裁定，均有实测）：**

1. `Utilities.DiagnosticsEqualityComparer`（`Utilities/Utilities.Diagnostics.cs:321-332`）在入口处 hoist 了两次 `GetDiagnosticMessage`，因此**每次插入非空诊断集合都会强制格式化消息文本**；把它们移到 file/start/length/code 四项比较之后（谓词可证不变——文本只在四项全等时才参与判断）可让 `callSignaturesWithParameterInitializers{,_2}.ts` 由崩溃转为 `same`。但 `utilities.ts:8917-8926` 正是 §15.16 前提 4 指名的"平局读取点"，参考自己也在入口 hoist（那里文本是普通字符串，端口是 A′ 的惰性 getter），**移动它就是新偏差**，故未落地。
2. `GetTypeNamesForErrorDisplay`（`Checker/Checker.Stringify.cs:576-586`）：参考返回前要**比较**两个渲染名并据此回退到全限定名，`ReportRelationError`（`Checker.Relations.Structured.cs:500`）还再比较一次 ⇒ 无法用实参级延迟消除，属 M7 边界（`types/mapped/*` 等多文件即因此崩溃）。

**排障要点（写入纪律）：** 本轮出现过一次 `same=4781 / port-crash=558` 的"in-flight"读数，它是某个切片临时把门控改成不抛（`// TEMP-PRINTER-STUB`）的树上测得的，**不可用作进度**——任何测量都必须在工作树静止、`git diff tools/` 为空、无临时插桩时重跑（本轮最终读数即 (d)）。同理，`Take(24)` 这类为了取更深栈帧的一次性工具改动会改变工具输出，测量前必须确认已还原。

### M6-late 第七轮：检查器侧 factory 形状审计与生成器缺陷收口（§15.15 的两条风险关闭）

**（a）factory 形状审计**

`Checker/**` 合成节点用的 factory 逐条对着参考核过（20 处代码用途）：**6 处改回 `NodeFactory.Factory`**（参考在 `getFlowTypeFromCommonJSExport` checker.ts:12026-12028、`getFlowTypeInStaticBlocks` :12039/:12043、`getFlowTypeInConstructor` :12060/:12063、`getArgumentArityError` :36481、JSX children 假属性声明 :33927、`createCallSignature` :44068 用的就是带括号化规则的 `factory`，即 `nodeFactory.ts:7407`），**3 处保持 `FactoryUtilities.ParseNodeFactory`**（`getSyntheticElementAccess` :11694-11696、`createSyntheticExpression` :36251、`serializeAsNamespaceDeclaration` :10209 —— 参考在同一行里混用两者：ModuleDeclaration 用 `parseNodeFactory`、ModuleBlock 用 `factory`），其余为注释修正。§15.15 的"AST 形状风险"一行据此标记为**已收口**；`types/**`（845 文件）与 `classes/**`（466 文件）前后 `SUMMARY` 完全一致，全语料复测仍是 **`same=3637 diverged=80 port-crash=2191`**（合成节点形状不进 dump，故读数不变——这一项是形状正确性，不是 dump 可见项），build 0/0、测试 1,049/1,049。

**（b）生成器缺陷收口（`GetIndexInfosOfIndexSymbol`）**

§15.15 记的"生成器缺陷"病根找到了：`tools/Gen/TsTypeMapper.MapSingle` 把 `T[]` 的形状判定放在**函数类型判定之前**，于是**返回类型以 `[]` 结尾**的签名（`(indexSymbol: Symbol, siblingSymbols?: Symbol[] | undefined) => IndexInfo[]`，types.ts:5083）被读成"函数数组"，生成出 `List<Func<ISymbol, List<ISymbol>, IIndexInfo>>?`。修法是把函数类型判定提到 `T[]` 之前（`IsFunctionType` 自身对 `(() => X)[]` 这类数组-of-函数仍返回 false，所以顺序调整不影响别的形状；重新生成后全仓库只改这一行）。生成结果是参考的形状：

```cs
Func<ISymbol, List<ISymbol>, List<IIndexInfo>>? GetIndexInfosOfIndexSymbol { get; set; }
```

`Checker` 用后备委托实现（`??= GetIndexInfosOfIndexSymbol`，语义等于参考 checker.ts:1658 对象字面量把 checker.ts:16693 的函数赋给该槽），原来那两个抛 `NotSupportedException` 的访问器删除；`CheckerCreateTypeCheckerTests` 的"接口减类"差异表因此清空（测试改为断言该成员返回形状正确的委托）。验证：build 0/0、测试 1,049/1,049、`Gen --check` 无漂移、全语料 **`same=3637 diverged=80 port-crash=2191`**（该成员不在 dump 路径上，读数不变）。

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



## 17. M7 计划：Layer 6/7（Transformers + Emitter）

### 17.1 规模（实测 `release-6.0`）

| 区域 | 行数 | 备注 |
|---|---|---|
| `emitter.ts` | **6,378** | 1-1190 发射文件/路径/`emitFiles`/`printSourceFileOrBundle`/buildInfo；**1211-6320 是 printer**（`createPrinter` 1211、`print` 1395、`pipelineEmitWithHint` 家族）；6327-6378 括号表与 list 发射辅助 |
| `sourcemap.ts` | 823 | `SourceMapGenerator`/`SourceMapSource`/`SourceMapWriter` |
| `transformers/**` | **33,556**（26 文件） | es2015 5,065、classFields 3,360、generators 3,284、ts 2,750、esDecorators 2,533、module/module 2,504、module/system 2,050、declarations 1,977 + declarations/diagnostics 812、es2018 1,486、es2017 1,154、jsx 937、utilities 886、legacyDecorators 846、esnext 800、typeSerializer 633、destructuring 626、namedEvaluation 489、module/esnextAnd2015 439、es2020 267、classThis 148、taggedTemplate 132、es2016 122、es2021 106、module/impliedNodeFormatDependent 99、es2019 51 |
| 合计 | **≈41,000** | 相对 Layer 0-5 的 176k 行约 23%，**是多会话里程碑**；本节给出切片、依赖与验收 |

### 17.2 端口已有基础（M4 期间落地，可直接用）

`EmitNode` 槽与辅助（`Parser/EmitNodes.cs`，M4b）、`EmitFlags`/`InternalEmitFlags`/`ExternalEmitHelpers`/`EmitHelper`/`EmitHint`/`ListFormat`/`EmitOnly`（`Model/`）、`NodeFactory` + `ParenthesizerRules`（M4c/M4d）、`factory/emitHelpers.ts` 的半量（M4f，§15.14）、生成接口 `IPrinter`/`IPrinterOptions`/`IPrintHandlers`/`IEmitTextWriter`/`ISymbolWriter`、`Utilities.UsingSingleLineStringWriter`（`SingleLineStringWriter : IEmitTextWriter`）。

### 17.3 阶段 A（M7-0）：printer 优先

理由：printer 是 TypeDiff 上 2,191 条崩溃与 80 条分歧的**唯一**成因（四个 `*ToString` 门控），又是发射与声明发射的共同前置；它也是 M7 里唯一"做完立刻能用全语料量化"的切片。

| 片 | 内容 | 参考范围 | 归属文件（端口） |
|---|---|---|---|
| A1 | 文本写入层：`createTextWriter`、`getTrailingSemicolonDeferringWriter`、缩进串缓存 | utilities.ts:6260-6470 | `Emit/EmitTextWriter.cs`、`Utilities/Utilities.Emit.cs` |
| A2 | printer 状态与工厂：字段、`createPrinter`、`createPrinterWith*` 四个记忆化默认、`printNode`/`printList`/`printFile`/`printBundle`、`writeNode`/`writeList`/`writeFile`/`writeBundle`、`beginPrint`/`endPrint`/`print`、`setSourceFile`/`setWriter`/`reset`/`getCurrentLineMap`、`emit`/`emitExpression`/`emitIdentifierName` 重载族、`beforeEmitNode`/`afterEmitNode`/`pipelineEmit`、`getPipelinePhase`/`getNextPipelinePhase`/`pipelineEmitWithNotification`/`pipelineEmitWithHint`/`pipelineEmitWithHintWorker`（巨型分派） | emitter.ts:1211-2031、1100-1190 | `Emit/Printer.cs`、`Emit/Printer.State.cs` |
| A3 | 类型与名字：`pipelineEmitWithSubstitution`、`emitHelpers`/`getSortedEmitHelpers`、字面量/标识符/限定名/实体名、类型参数/参数/装饰器、全部类型节点、绑定模式 | emitter.ts:2040-2611 | `Emit/Printer.Types.cs` |
| A4 | 表达式与语句：表达式族（含 `createEmitBinaryExpression`、`parenthesizeExpressionForNoAsi`）、语句族、声明族、模块族、注释与 token 发射辅助 | emitter.ts:2612-3876、3183-3334 | `Emit/Printer.Expressions.cs`、`Emit/Printer.Statements.cs` |
| A5 | JSX、case 子句、source-map 注释路径（本阶段按"无 generator 时的参考行为"实现，`SourceMapGenerator` 归阶段 B）、尾段辅助 | emitter.ts:3877-6327 | `Emit/Printer.Jsx.cs`、`Emit/Printer.Tail.cs` |
| A6 | 接线与验收：`Checker.Stringify.cs` 四个门控改调真 printer（`createPrinterWithRemoveComments`/`WithRemoveCommentsNeverAsciiEscape`/`WithDefaults`/`WithRemoveCommentsOmitTrailingSemicolon`、`getTrailingSemicolonDeferringWriter`），删掉 `PrinterNotSupported` 与钉"仍抛"的测试，改成钉字符串；全语料复测 | checker.ts:6194-6308 | `Checker/Checker.Stringify.cs` |

**阶段 A 验收**：`TypeDiff` 全语料（5,908）的 80 条分歧与 2,191 条崩溃整体塌陷（门控消失后应只剩真实分歧）；四个 `*ToString` 的单测钉住字符串（`TypeToString(numberType)` 之类）；build 0/0、全量测试绿。

### 17.4 阶段 B：transformers + emitter + source maps（分批）

依赖顺序（每批做完即可编译并跑对应能力）：
1. `transformers/utilities.ts`（886）+ `destructuring.ts`（626）+ `taggedTemplate.ts`（132）——公共工具；
2. `ts.ts`（2,750，含 `classThis.ts` 148、`typeSerializer.ts` 633）——TS 专属擦除（类型/接口/枚举/命名空间/参数属性…），是 `.js` 发射的主链；
3. `es2015.ts`（5,065）+ `generators.ts`（3,284）——class/箭头/解构/生成器降级；
4. `es2016/2017/2018/2019/2020/2021/esnext`（122+1,154+1,486+51+267+106+800）；
5. `classFields.ts`（3,360）+ `legacyDecorators.ts`（846）+ `esDecorators.ts`（2,533）+ `namedEvaluation.ts`（489）；
6. `jsx.ts`（937）；
7. `module/{esnextAnd2015,impliedNodeFormatDependent,module,system}.ts`（439+99+2,504+2,050）；
8. `sourcemap.ts`（823）+ printer 的 source-map 路径；
9. `emitter.ts:1-1190`（`getOutputPathsFor`/`emitFiles`/`printSourceFileOrBundle`/host）+ `Program.Emit`/`EmitBuildInfo`/`GetDeclarationDiagnostics`/`GetEmitHost`/`GetTsBuildInfoEmitOutputFilePath`/`GetOutputDeclarationFileName`/`forEachEmittedFile` 八条阻断逐条接管；
10. `declarations.ts`（1,977）+ `declarations/diagnostics.ts`（812）——声明发射（依赖 printer + typeSerializer）。

### 17.5 阶段 C：验收外环与收口

- 新建 `tools/EmitDiff`（形制照 `tools/BindDiff`/`tools/TypeDiff`：同语料、两半各写 `.js` 与诊断，逐字节比），先跑单文件选项矩阵（`target`/`module`/`jsx`/`useDefineForClassFields`…），再上全语料。
- P1b（§7）：`.errors.txt` + `.js` 与 `tests/baselines/reference/**` 逐字节比对（需要指令注释解析与 harness 复刻）。
- `EmitBuildInfo` 与增量属 M8；本阶段只把 `Program` 侧接口接通。

### 17.6 M7-0 交付：printer 落地，四个 `*ToString` 去门控

**交付物**：`Emit/` 8 个文件 **8,290 行**（`Printer.cs` 2,185、`Printer.Statements.cs` 1,340、`Printer.Types.cs` 1,686、`Printer.Expressions.cs` 880、`Printer.Comments.cs` ~700、`Printer.SourceMaps.cs` 263、`PrinterFactory.cs` 131、`Printer.State.cs` 137）＋ `Utilities/Utilities.Printer.cs` 221 ＋ `Utilities/Utilities.Emit.cs`（A1 写入层与注释/三斜线工具）。覆盖 `emitter.ts:1199-6378` 的 printer 全部（发射文件族 1-1190 留给阶段 B）。

切片与归属（四个并行 agent + 集成）：
| 片 | 内容 | 文件 |
|---|---|---|
| A1 | 文本写入层（`createTextWriter`/`getTrailingSemicolonDeferringWriter`/缩进缓存） | `Utilities.Emit.cs` |
| A2 | printer 工厂与状态、`printNode/printList/printFile/printBundle`、`writeNode/writeList/writeFile/writeBundle`、`print/reset/setWriter/setSourceFile/getCurrentLineMap`、`emit` 族、`pipelineEmit` 五相、`pipelineEmitWithHintWorker` 巨型分派、writer 包装、行终止符引擎、`emitList/emitNodeList*` | `Printer.cs`、`PrinterFactory.cs`、`Printer.State.cs` |
| A3 | 替换相与 emitHelpers、字面量/标识符/实体名、类型参数与参数、全部类型节点、绑定模式、文本读取与名字生成器（`generateName` 全族） | `Printer.Types.cs`、`Utilities.Printer.cs` |
| A4 | 表达式族（含 `createEmitBinaryExpression`、`parenthesizeExpressionForNoAsi`）、语句/声明/模块族、JSX、JSDoc 与 sourceFile/prologue 族 | `Printer.Expressions.cs`、`Printer.Statements.cs` |
| A5 | 注释管线（30 个方法）+ source-map 管线（10 个方法 + 一个具名阻断）；另补 `utilities.ts` 的注释族（`writeCommentRange`/`emitDetachedComments`/`getContainingNodeArray` 等，~250 行） | `Printer.Comments.cs`、`Printer.SourceMaps.cs`、`Utilities.Emit.cs` |
| A6 | 接线：四个 `*ToString` 改调真 printer（`PrinterFactory` 的四个默认 + `getTrailingSemicolonDeferringWriter`），删除 `PrinterNotSupported` 与文件头"printer 是 M7"的说明；`typeToString` 的缺省 writer 改为参考的 `createTextWriter("")`（其余三个仍用 `usingSingleLineStringWriter`） | `Checker/Checker.Stringify.cs` |

**验收（实测）**：build 0 错误 / 0 警告；测试 **1,053/1,053**（并入 A1 的 4 例写入层测试）；`Gen --check` 无漂移；**TypeDiff 全语料 5,908 文件：`same=5516 diverged=11 port-crash=381`**（M6 末为 3,637 / 80 / 2,191 —— `same` +1,879、`diverged` −69、崩溃 −1,810）。A2 另做了一次**与参考 printer 的差分烟测**（同一源文本分别用端口与 `lib/typescript.js` 的 printer 打印，逐字节比对）：`printFile`/`printNode(SourceFile)`/`printList`、`removeComments`+`omitTrailingSemicolon` 变体、`preserveSourceNewlines` 的开/关与注释重排版、`.tsx`、JSDoc 文件全部一致。

**M7-0 之后剩余（下一次的起点）**：`port-crash=381` = `syntacticNodeBuilder` **376**（M4f 未落地的 `expressionToTypeNode.ts`，1,330 行 —— 现在是唯一的大头）+ `visitEachChildWorker` 1（Layer 6）+ `InvalidCastException` 2 + 其它 2；`diverged=11` = 10 条同 M4f 门控造成的单侧 `=!` + **1 条真分歧**（`types/literal/templateLiteralTypes4.ts:150` 的 `node sym mismatch`）。

**三项模型/生成器待办（本次记录，不属 printer）**：① `IPrinter.PrintList/WriteList` 生成成 `where T : Node`，而它们要接受的槽是接口化的（`NodeArray<IStatement>` 等），`where T : INode` 才对（同 `EmitList` 的裁定）；② 模型里没有任何类型实现 `ISourceMapSource`，而参考的 `setSourceMapSource(sourceFile)` 直接传 `SourceFile`（现在由 `Printer.SourceFileMapSource` 适配器顶住，加接口后适配器即可删）；③ `IPrintHandlers.OnBeforeEmitNodeArray/OnAfterEmitNodeArray` 生成成 `NodeArray<object?>`（types.ts:9857 是 `NodeArray<any>`），于是端口在**装了钩子时**要复制一次数组；生成成 `NodeArray<Node>` 即可免掉复制（M8 之前无人装钩子）。

### 17.7 M7-0 收尾：printer 之后的连锁拆门 —— TypeDiff `same=5838 diverged=7 port-crash=63`

printer 落地后，语料从"崩溃"变成"能跑"，此前被门控掩盖的缺陷成批显形；本轮把它们逐条拆掉，全语料（5,908 文件）走到：

| 点 | same | diverged | port-crash |
|---|---|---|---|
| M6 末 | 3,637 | 80 | 2,191 |
| 仅 printer 落地（§17.6） | 5,516 | 11 | 381 |
| **本轮收尾后** | **5,838** | **7** | **63** |

**本轮拆掉的三批（全语料 + 逐文件证据）**

1. **4 处 JS 越界读/联合类型强转**（`ab98e5b`）：`Checker.IndexAccess.cs:972` 把数值字面量类型的 `value`（JS 里 number）硬转 string；`Checker.Check.Calls.cs:915` 把 `NamedTupleMember | ParameterDeclaration` 强转 `INamedDeclaration`；`Checker.Check.Expressions.cs:1719` 的 `node.typeArguments![i]`（JS 越界读即 `undefined`，端口抛 AOORE）；`NodeBuilder.Members.cs:164` 的 `elementFlags[i]` 同理。四者都按"JS 越界读"的语义用 `i < count` 守卫或传 null errorNode 收口。
2. **`expressionToTypeNode.ts` 全量移植（M4f 的最后一半，2,177 行）**：`Checker/SyntacticNodeBuilder.cs`(448) + `.Engine.cs`(969) + `.Kinds.cs`(760)；`Checker.NodeBuilder.cs:86` 的 `syntacticNodeBuilder` 门控换成参考自己的 `_syntacticNodeBuilder ??= SyntacticNodeBuilder.CreateSyntacticTypeNodeBuilder(_compilerOptions, SyntacticBuilderResolver)`，`CheckerNodeBuilderTests` 的"仍抛"测试改成真断言。它一次性退掉 **376 条崩溃**与 10 条单侧 `=!` 分歧。
3. **`Js.ToNumber` 改成 ECMA-262 的 StringNumericLiteral 强制转换**：旧版只特判 `0x` 前缀、其余交给 `double.TryParse`，于是 `+"0o1"`/`+"0b1"`/`+"1e21"`（以及带空白的 `" 1 "`）都被答成 NaN、`"infinity"` 反而答成 Infinity、16 位十六进制串被当成补码（`0xFFFFFFFFFFFFFFFF` → -1）。新版按规范分词（JS WhiteSpace/LineTerminator 裁剪、空串→0、三种进制前缀、十进制文法先验，再用 TryParse 取值）。它以"122 个用例与 node 的 `+s` 逐位比对"+"在 `git archive HEAD` 的干净副本里只换这一个文件做 A/B"证明：退掉 2 条分歧（`types/literal/templateLiteralTypesPatterns.ts`、`types/literal/templateLiteralTypes4.ts` 的哈希**等于参考自己的哈希**），`parser/**`(820)、`es6/binaryAndOctalIntegerLiteral/**`、`types/members/**`、`es2019/**` 零变化。

**M7-0 之后的剩余（下一步的起点）**：`port-crash=63` **全部**是同一个门控 `visitEachChildWorker`（`checker.ts:1134 → visitorPublic.ts` 的 `visitEachChild`，Layer 6 的公共 visitor 基础设施 —— 一个自包含切片即可全部退掉）；`diverged=7` = 4 条同属该门控的单侧 `=!` + 1 条诊断码错配（`interfaces/interfaceDeclarations/interfaceExtendsObjectIntersectionErrors.ts`：端口 `2416@758` 对参考 `2612@709`）+ 2 条类型 id 偏移（`genericCallToOverloadedMethodWithOverloadedArguments.ts` 332/340、`discriminatedUnionTypes2.ts` 178/179，即类型创建计数差，与本轮改动无关，已用 A/B 排除）。

**教训（写入纪律）**：M7-0 的三次测量都证明同一件事 —— 打开一道门控会**成批**显形下一层的缺陷（printer → 4 处 JS 越界读；syntacticBuilder → `visitEachChildWorker`），因此每一道门控拆除后必须重跑全语料并把新显形的项分类（真缺陷 / 下一道门控），不要按上一轮的分组继续做。

### 17.8 M7-0 收尾（二）：`visitorPublic.ts` 全量移植 —— 全语料 `same=5905 diverged=3 port-crash=0`

**`src/compiler/visitorPublic.ts`（1,808 行）→ `Utilities/Utilities.Visitor.cs`**，并删除 `Utilities/Utilities.cs` 的 `VisitEachChildWorker` 门控（第三个也是最后一个 checker 侧门控）。它一次退掉上一节剩下的**全部 63 条崩溃**与 4 条同源 `=!` 分歧。两处形状决策（都有参考行）：TS 的 `NodeVisitor`/`NodesVisitor` 是**泛型调用签名接口**（一等泛型函数），C# 里改为各带一个泛型方法的抽象类，135 张表项都由实参推断同一个类型变量（正是 TS 的推断结果），槽位名与位置（`nodesVisitor`/`tokenVisitor`/`nodeVisitor`）照旧；`TransformationContext` 槽改用 `IVisitorTransformationContext`（生成的 `ITransformationContext.Factory` 要求 `INodeFactory`，而端口没有任何类型实现它，见 §15.9 —— 与 `IEmitHelperFactoryContext` 同一条裁定）。

**去重（本次一并收口）**：`Checker/SyntacticNodeBuilder.Engine.cs` 的**文件内** `VisitNode<T>`/`VisitNodes<T>`/`ExtractSingleNode`（M7-0 移植时因 visitorPublic 未落地而临时声明）全部删除，31 处调用改走 `Utilities.VisitNode`/`Utilities.VisitNodes`；`VisitNodesWithoutCopyingPositions` 与 `NodeBuilder.Types.cs` 的 `DeepCloneOrReuseNodes`（**票面之外的第 4 个调用点**）各自落成 `NodesVisitor` 子类；`GetSynthesizedDeepClones*` 的约束由 `where T : Node` 放宽为 `where T : INode`（旧的 `(NodeArray<Node>)ns` 强转对非 Node 元素在运行期不成立）。这次替换顺带修掉两处**朝向参考**的行为差：被访问过的片段获得 `pos/end = (-1,-1)` 且只有含末元素才带 `hasTrailingComma`；访问器返回 null 的元素**被丢弃**而不是复用（参考 `if (visited) push(...)` 没有 else，`visitorPublic.ts:328-376`）。

**另一处真缺陷（本次显形并修）**：`Checker.GetApplicableIndexSymbol`（`Checker.Check.Source.cs:988`）把 `type` 强转 `UnionOrIntersectionType` 再读 `Members`，遇到类型参数直接抛 `InvalidCastException`（`privateNameAndIndexSignature.ts`、`keyofAndIndexedAccess2.ts` 的 `sym=!` 分歧就是这个）；参考的 `(type as ObjectType).members`（checker.ts:50135）是**结构读取**，对没有该槽的类型答 `undefined` 因而整支跳过 —— 改为安全转换 + null 短路。

**全语料（5,908 文件）三轮总账**：M6 末 `3637/80/2191` → printer 落地 `5516/11/381` → 本节收尾 **`5905/3/0`**（`same` +2,268、`diverged` −77、崩溃 −2,191）。build 0/0、测试 1,053/1,053。

**剩余 3 条分歧（下一轮的起点，均有现场）**：① `interfaces/interfaceDeclarations/interfaceExtendsObjectIntersectionErrors.ts` 诊断码错配（端口 `2416@758` 对参考 `2612@709` —— 两者都不只是码不同，span 也不同）；②③ 两条类型 id 偏移（`genericCallToOverloadedMethodWithOverloadedArguments.ts` 332/340、`discriminatedUnionTypes2.ts` 178/179），已用 A/B 确认与 `Js.ToNumber`/printer/visitor 都无关，属类型创建计数差。

### 17.9 M7-B 前置：发射管线外壳 + `tools/EmitDiff` + transform 公共工具（checker 零回归）

| 切片 | 内容 | 交付 |
|---|---|---|
| 批次 1（公共工具） | `transformers/utilities.ts`(886) + `destructuring.ts`(626) + `taggedTemplate.ts`(132) | `Transforms/TransformationUtilities.cs`、`Transforms/Destructuring.cs`、`Transforms/TaggedTemplate.cs`（~2,470 行）+ 43 例单测 |
| 发射管线外壳 | `transformer.ts`(696) 的驱动与 `TransformationContext/Result` + `emitter.ts:1-1190`（输出路径族、`emitFiles`、`printSourceFileOrBundle`、build info）+ `Program.Emit/EmitBuildInfo/GetEmitHost/GetDeclarationDiagnostics` | `Transforms/Transformer.cs`、`Emit/Emitter.cs`、`Emit/Emitter.Model.cs`、`Program/Program.Emit.cs`、`Utilities/Utilities.EmitPaths.cs`；**`Program/Program.EmitBoundary.cs` 删除**（每条 M6 阻断要么落地要么改名） |
| 发射差分工具 | `tools/EmitDiff` | `Program.cs`(972) + `ts-emit.cjs` + README + csproj；CLI/输出/退出码与 BindDiff/TypeDiff 同形，`port-crash` 与 `ts-error` 分离，绝不把"端口没发射"算作 same |

**唯一保留的具名阻断**：`transformer.ts:120/127/192` 的 `getTransformers`/`getScriptTransformers`/`getDeclarationTransformers` —— 它们点名每一个具体 transform 工厂，属 M7-B2…B7；在落地前它们抛具名 `NotSupportedException`（不静默返回空清单）。

**组装期发现并修的真缺陷**：`Checker._emitResolver` **从未被赋值**。参考在构造期 `var emitResolver = createResolver();`（checker.ts:1550），端口的 PORT-NOTE 把它留给"M6-5 的组装"，而那次从未落地 —— 于是任何 emit 路径的 `getEmitResolver` 都在 `Debug.CheckDefined(_emitResolver)` 上炸（新测试先撞上它）。修法：在 `Checker` 构造里按参考赋值。

**另修三处新测试的期望（修正到参考行为，不是放宽）**：① `IEmitHost` 同时继承 `IScriptReferenceHost`/`ISourceFileMayBeEmittedHost` 的同名成员，C# 里通过派生接口调用有歧义 ⇒ 按基接口限定（端口既有写法）；② `emittedFiles` 只在 `listEmittedFiles` 打开时填充（参考 emitter.ts:767/790）⇒ 测试打开该选项；③ 端口的 `FakeSystem` 只记录写入路径不保存内容 ⇒ 断言 `system.Written`（与 `FilesTests` 一致）。

**验收（实测）**：build 0 错误 0 警告、测试 **1,097/1,097**、`Gen --check` 无漂移、**TypeDiff 全语料仍 `same=5905 diverged=3 port-crash=0`（checker 零回归）**；`tools/EmitDiff` 全语料冒烟：参考半对该文件成功发射（8,964 字节），端口半如实报
`port-crash: NotSupportedException: getTransformers (transformer.ts:120) … transform batches M7-B`。

### 17.10 M7-B 批次 2：`ts.ts` + `typeSerializer.ts` + `classThis.ts`（含两处基础缺陷修复）

| 交付 | 内容 |
|---|---|
| `Transforms/TypeScriptTransform*.cs`（6 文件） | `transformers/ts.ts`（2,750 行）全量：类型擦除、声明合并/命名空间、参数属性、import-equals、`export =`、const enum 内联、替换钩子 |
| `Transforms/TypeSerializer.cs` | `transformers/typeSerializer.ts`（633）：声明发射用的运行时类型序列化器 |
| `Transforms/ClassThis.cs` | `transformers/classThis.ts`（148） |
| 测试 | `TypeScriptTransformTests`（27）、`TypeSerializerTests`（62）、`ClassThisTests`（12） |

**smoke 端到端暴露的两处基础缺陷（都已修，均影响面大于本批次）：**

1. **`NodeFactory` 从不标 `NodeFlags.Synthesized`**（`Parser/NodeFactory.cs`）。参考的 `factory` 是 `createNodeFactory(NoIndentationOnFreshPropertyAccess, syntheticFactory)`，而 `syntheticFactory`（nodeFactory.ts:7393-7405）对每个工厂产物 `makeSynthetic`；端口的 PORT-NOTE 曾以为"pos/end 默认 -1 就等于已合成"，把这一层丢了。后果：`Utilities.IsParseTreeNode` 对所有工厂节点答真 ⇒ `GetParseTreeNode` 跳过 nodeTest ⇒ `EmitResolver` 把合成节点递给 checker ⇒ `Debug.Fail`。修法：构造参数 `synthesizedNodes`（`Factory` 传 true，`FactoryUtilities.ParseNodeFactory` 传 false，即参考的两个 base factory），`CreateBaseNode` 据此置位。
2. **`Utilities.Visitor` 的 `isArray(visited)` 判定**（visitorPublic.ts:172）：端口写成 `visited is IEnumerable<Node>`，而接口化的节点数组（`NodeArray<IStatement>`）不是 `IEnumerable<Node>`（泛型不变），于是 visitor 返回"片段列表"时抛 `InvalidCastException`。新增 `IsNodeArray`（非泛型 `IEnumerable` 且非 `Node`）并在两处使用。

**验收（实测）**：build 0 错误 0 警告、测试 **1,206/1,206**、**TypeDiff 全语料仍 `same=5905 diverged=3 port-crash=0`（零回归）**。批次 2 的期望值改为用参考真值测定：`ts.transpileModule(src, options)`（6.0.3）为 24/27 例的直接 oracle，另两例用 `node lib/tsc.js` 复核（`transpileModule` 隐含 `isolatedModules`，无法建模 const enum 保留与跨文件别名解析）；其中 1 例期望写错（点号命名空间的中间变量是 `var B;` 而非 `let B;`，`ts.ts:2026`）已按参考改正，另 2 处"本链比参考小"（ES 模块转换器未装、const enum 内联用 `tsc` 测）在测试处注明而非掩盖。

### 17.11 M7-B 批次 3（一，切片自述）：`generators.ts` —— 生成器降级 + 一处跨切片 printer 缺陷

本批次的一半（`es2015.ts`，5,065 行）由并行切片交付；本节记 `generators.ts`（3,284 行）这一半。

| 交付 | 内容 |
|---|---|
| `Transforms/GeneratorsTransform.cs`（593） | 工厂 `TransformGenerators` + `OpCode`/`BlockAction`/`CodeBlockKind`/`ExceptionBlockState`/`Instruction` 五个枚举、`CodeBlock` 类族（`Exception`/`With`/`Switch`/`Loop`/`Labeled`）、`OperationArguments` 结构体、状态字段与四个访问器（`transformSourceFile` 起手） |
| `Transforms/GeneratorsTransform.Visits.cs`（766） | `generators.ts:552-1278` 的访问器：生成器函数声明/表达式/访问器、含 `yield` 的逗号/逻辑/条件/`yield*`/数组/对象/元素访问/调用/`new` |
| `Transforms/GeneratorsTransform.Emit.cs`（1,072） | `generators.ts:1280-2107`：15 种语句的 `transformAndEmit*`、`containsYield`/`countInitialNodesWithoutYield`、`onSubstituteNode`（改名后的 catch 变量）与 `cacheExpression`/`declareLocal` |
| `Transforms/GeneratorsTransform.Blocks.cs`（602） | `generators.ts:2109-2711`：标签、代码块的 `begin*`/`end*` 族、`break`/`continue` 目标查找与 `emit*` 操作族 |
| `Transforms/GeneratorsTransform.Build.cs`（531） | `generators.ts:2742-3284`：`build`/`buildStatements`、标签刷写、块进入/离开簿记与 `write*` 族 |
| 测试 | `tests/.../Transforms/GeneratorsTransformTests.cs`：39 例 |

**唯一保留的具名阻断**：`visitObjectLiteralExpression` 的 `reduceProperty` 调参考的 `createExpressionForObjectLiteralElementLike`（factory/utilities.ts:465-480），该成员未移植（`FactoryUtilities` 头部已记，阻断项是 `getAllAccessorDeclarations`）。因此含 `yield` 的对象字面量路径响亮抛 `NotSupportedException`（点名成员与参考行），并有两例测试钉住：一例断言这条失败，一例（常量）记下参考对同一输入的输出，供该成员落地后直接改断言。其余 37 例全部是参考真值。

**方法与 oracle**：全部期望值用 `ts.transpileModule(src, { target: ES5, module: CommonJS, alwaysStrict: false })`（6.0.3）测定；39 例中 38 例逐字节比对（含 `__generator` helper 全文与调用点），1 例为具名阻断。夹具严格限制在「ES5 语法 + 生成器」子集，因此 `transformGenerators` 就是参考会施加的唯一 transform（`transformES2015` 对该子集无事可做），测得的文本与参考一致。

**跨切片真缺陷（本批发现，已修，不在本切片文件内）**：`Emit/Printer.cs` 的 `WriteLines`（emitter.ts:4975-4985）把 JS 的 `slice` 写成了 `Substring`。参考 `const line = indentation ? lineText.slice(indentation) : lineText;` 中 `"".slice(8) === ""`，C# 的 `"".Substring(8)` 抛 `ArgumentOutOfRangeException`；`guessIndentation` 跳过空行而刷写循环不跳，于是**任何**「首行为空 + 各行统一缩进」的 helper 文本都会在第一个空行炸掉。`Parser/EmitHelpers.cs` 里每个多行 helper 都是这个形状，因此本切片是第一个在测试里真正申请到 helper 的切片（`__generator`）就撞上它——38 例全部死在 `Printer.WriteLines`，transform 的文本一行都没写出去。修法一行：`lineText.Length <= indentation.Value ? "" : lineText.Substring(indentation.Value)`；已由 `Emit/` 切片按同一钳制落下（该文件处的 PORT-NOTE 记之）。与 §17.10 的头两条同属「JS 越界读 → C# 抛异常」这一类。**该缺陷不修则本切片 38/39 例全红**，所以它是本批次的硬前置，不是可选改进。

**逐条偏差（均已写入代码处的 PORT-NOTE）**：① `endLoopBlock` 的 `endBlock() as SwitchBlock`（generators.ts:2342）是参考自身的笔误（上一行断言的是 `Loop`），端口用 `BreakableCodeBlock`（`Switch`/`Loop`/`Labeled` 共享 `isScript`/`breakLabel` 的基类）承载那两个槽；② 参考用平行洞数组 + 元组联合存状态（明写为了省 GC），端口保留数组形态，洞以 `-1`/`null` 哨兵表达（每个读洞处本就与「非负操作下标」比较），`OperationArguments` 由元组联合变为 4 槽 readonly struct（仍是「一个数组元素、无对象」）；③ `supportsUnlabeledBreak`/`supportsLabeledBreakOrContinue`/`supportsUnlabeledContinue` 的类型谓词在 C# 退化为 `bool` + 调用处强转；④ `visitNode(x, visitor, isStatement)` 的断言收窄在端口只保留入参类型，个别 `Node` 入参在边界强转；⑤ `containsYield` 参数放宽为 `INode?`（`IStatement` 与节点类在端口无公共基类）；⑥ `isLogicalOperator`（utilities.ts:7408）/`isBinaryLogicalOperator`（utilities.ts:7403）在 `FactoryUtilities` 已有私有一份（factory/utilities.ts:1246 的二进制版），本切片是第二份拷贝（同 `Destructuring.AssertEachNode` 的处理），报表请收一份；⑦ `Debug.failBadSyntaxKind` 与 `Debug.assertNever` 在表达式位置的返回类型缺口，按仓库既有写法用 `Debug.Fail<T>`/「void 后跟一个 return」；⑧ `renamedCatchVariableDeclarations` 与 `labelExpressions`/`labelNumbers` 的稀疏写读各配一个私有小工具（`List<T>` 无洞）。

**验收（实测）**：真树 `dotnet build src/TypeScript.Compiler` **0 错误 0 警告**；`dotnet test --filter GeneratorsTransformTests` **39/39 通过**（printer 缺陷修复后；修复前该 38 例全部死在 `Printer.WriteLines`，本批次因此也是那条缺陷的阳性对照）。全部期望值来自参考真值测定，含 `__generator` helper 全文与调用点的逐字节比对。


### 17.11 M7-B 批次 3：`es2015.ts` + `generators.ts`（含四处基础缺陷修复）

| 交付 | 内容 |
|---|---|
| `Transforms/ES2015Transform*.cs`（7 文件） | `transformers/es2015.ts`（5,065 行）：class→IIFE + `__extends`、箭头/捕获 `this`/`new.target`、解构与默认值/rest、for-of/for-in 循环转换（`_loop_1` 家族）、spread、模板字面量、`super`/`new.target` 替换 |
| `Transforms/GeneratorsTransform*.cs`（5 文件） | `transformers/generators.ts`（3,284 行）：生成器/async 状态机（`__generator`/`__awaiter` 辅助、块与标签、`try/catch/finally` 转换、参数展开辅助） |
| 测试 | `ES2015TransformTests`（30→28，见下）、`GeneratorsTransformTests`（39），全部以 `ts.transpileModule`/`tsc` 6.0.3 的真值钉定 |

**本批次顺带修掉的四处基础缺陷（都不在本切片范围内，但都是真缺陷）：**

1. **`Parser/NodeFactory.cs` 的克隆丢 `NodeFlags.Synthesized`**：参考的 `cloneNodeWorker` 由**合成基工厂**造克隆后再 `clone.flags |= node.flags & ~NodeFlags.Synthesized`（nodeFactory.ts:6395/7393-7405），端口写成 `clone.Flags = node.Flags & ~NodeFlags.Synthesized` —— 既丢了标记又覆盖了构造期标志。后果：克隆被 `IsParseTreeNode` 认作语法树节点，`EmitResolver.GetConstantValue` 的 `getParseTreeNode(nodeIn, canHaveConstantValue)` 守卫把 es2015 自己合成的 `Object.defineProperty(C.prototype, …)` 实参漏进 checker，`getAssignmentTarget` 解引用其空父指针 → **任何带访问器的类**都 NRE。修法按"本工厂是否合成"（`_synthesizedNodes`）区分 `CloneNodeWorker` 与四个专用克隆（后者经本工厂的 `CreateBaseIdentifier` 已带标记，用 `|=`）。
2. **`Checker/Checker.Inference.cs` 的 `GetEnclosingIterationStatement` 把三值回调压成两值**：参考 `(!n || nodeStartsNewLexicalEnvironment(n)) ? "quit" : isIterationStatement(n, false)`（checker.ts:31267-31269）里 `false` 是**继续**，端口写成 `!startLexical && isIterationStatement ? true : null`，于是遍历在循环体的 `Block` 处退出、对所有 Block 体的 do/while 都答 null ⇒ `BlockScopedBindingInLoop`/`LoopWithCapturedBlockScopedBinding` 从没被设过 ⇒ es2015 的循环转换永不触发（被捕获的绑定走了"重命名"路径）。
3. **`Emit/Printer.cs` 的 `WriteLines` 未按 JS 语义裁剪**：参考 `lineText.slice(indentation)` 会**钳制**（空行答空串，`guessIndentation` 跳过空行而循环不跳），端口的 `Substring` 越界抛 `ArgumentOutOfRangeException` —— 任何"前导换行 + 统一缩进"的 emit helper 文本（`Parser/EmitHelpers.cs` 里全部如此）一被真正请求就炸。
4. **`Checker/Checker.Check.Source.cs` 的 `IsSymbolOfDeclarationWithCollidingName` 读了空值**：参考末尾 `return links.isDeclarationWithCollidingName!` 是**类型层断言**（容器无局部变量时该槽保持 `undefined`，真值测试即假），端口 `.Value` 抛"Nullable object must have a value"。

**验收（实测）**：build 0 错误 0 警告、测试 **1,284/1,284**（+30 es2015 +39 generators +2 访问器真值例；另删除 1 例不可能出现在参考管线里的静态属性初始化用例 —— `transformClassFields` 无条件跑在 `transformES2015` 之前，transformer.ts:161-166 + es2015.ts:2276-2278，该用例归 M7-B5 用真链验证）；**TypeDiff 全语料仍 `same=5905 diverged=3 port-crash=0`（零回归）**。

### 17.12 M7-B 批次 4：余下全部转换器 + 转换器链组装

一次并行 8 切片把 `transformers/` 剩余部分全部落地（每片自带以 `ts.transpileModule`/`tsc` 6.0.3 真值钉定的测试）：

| 切片 | 参考 | 端口文件 | 测试 |
|---|---|---|---|
| 语言版本小尾巴 | `esnext.ts`(800)+`es2021.ts`(106)+`es2020.ts`(267) | `ESNextTransform.cs`/`ES2021Transform.cs`/`ES2020Transform.cs` | 39+11+20 |
| 异步/对象展平 | `es2017.ts`(1,154)+`es2018.ts`(1,486)+`es2016.ts`(122)+`es2019.ts`(51) | `ES2017Transform{,.Substitution,.Support}.cs`/`ES2018Transform{,.Substitution}.cs`/`ES2016Transform.cs`/`ES2019Transform.cs` | 4 文件 |
| 类字段与命名求值 | `classFields.ts`(3,360)+`namedEvaluation.ts`(489) | `ClassFieldsTransform{,.Visitor,.Fields,.Expressions,.Classes,.PrivateNames,.Substitution}.cs`+`NamedEvaluation.cs` | 19（真链 `[transformTypeScript, transformClassFields, transformES2015]`） |
| 装饰器 | `legacyDecorators.ts`(846)+`esDecorators.ts`(2,533) | `LegacyDecorators.cs`+`ESDecorators{,.ClassLike,…}.cs` | 17 |
| 模块 | `module/module.ts`(2,504)+`module/system.ts`(2,050)+`module/{esnextAnd2015,impliedNodeFormatDependent}.ts`(439+99) | `Transforms/Module/{ModuleTransform*.cs,SystemTransform*.cs,EsNextAnd2015.cs,ImpliedNodeFormatDependent.cs}`（6,079 行） | 23 |
| JSX | `jsx.ts`(937) | `JsxTransform{,.Text}.cs`（1,403 行，含 253 项实体表逐字生成） | 50 |
| 声明发射 | `declarations.ts`(1,977)+`declarations/diagnostics.ts`(812) | `Transforms/Declarations/DeclarationsTransform{,.Reporting,.Members,.Statements,.Support}.cs`+`DeclarationsDiagnostics.cs` | 见其自述 |
| sourcemap | `sourcemap.ts`(823)+printer 映射路径 | `SourceMap{,.Generator,.Mappings,.DocumentPosition}.cs`；`Emit/Printer.SourceMaps.cs` 的具名阻断换成真生成器 | 18 |

**转换器链组装（transformer.ts:78-197，M7-B/C 的最后一个具名阻断）**：`Transforms/Transformer.cs` 的三个列表构建器从"响亮阻断"变成参考自身组合 —— `GetScriptTransformers` 按 `transformer.ts:127-190` 的顺序与门控压入 19 个工厂（`transformTypeScript` → 装饰器、JSX、ESNext、ESDecorators、**classFields（无条件）**、ES2021…ES2015、generators → `getModuleTransformer` → 自定义 after），`GetDeclarationTransformers` 为 `transformDeclarations` + 自定义 `afterDeclarations`，`emitOnly` 回答共享空表；`GetModuleTransformer` 委派给 `ImpliedNodeFormatDependent.GetModuleTransformer`（模块切片的公开成员，参考 switch 的唯一家）；`TransformerListNotLanded` 删除。

**同时收口的基础缺陷与门控**：
1. **`Utilities.GetDecorators`/`GetModifiers` 只做 cast 不做 filter**（`Utilities/Utilities.Checker.cs`）：参考 `filter(node.modifiers, isDecorator)`/`isModifier`（utilitiesPublic.ts:1024-1034）；端口对共享 modifiers 槽整体 `Cast<>`，任何"装饰器 + 关键字修饰符"（`@dec static y = 2`）都抛 `InvalidCastException`。装饰器切片端到端复现，已修。
2. **`Program.GetDeclarationDiagnosticsFromTransform` 的最后具名阻断换成真调用**（`DeclarationsTransform.GetDeclarationDiagnostics`，declarations.ts:221-238）⇒ `--declaration` 的诊断路径贯通。
3. **`Utilities/Utilities.Visitor.cs` 的 `PrefixUnaryExpression` 分派把参数窄成 `JsonMinusNumericLiteral` 子类型**（`JsonMinusNumericLiteral : PrefixUnaryExpression`，模型 NodeClasses.cs:1375）：参考的 `visitEachChildOfPrefixUnaryExpression`（visitorPublic.ts:1154）与 `updatePrefixUnaryExpression`（nodeFactory.ts:3387）都取基类型；端口的窄化在分派点**强转**每个 `-x`/`+x`，即 EmitDiff 首轮全语料里 18 条崩溃的全部成因。已改基类型 + 纠引注行号。
4. **四处参考 `utilities.ts` 成员从切片私有副本归位 `Utilities`**（各切片报告点名）：`IsStringDoubleQuoted`(3917-3919)、`IsAmbientPropertyDeclaration`(2197-2199)、`IsNodeWithPossibleHoistedDeclaration`(4964-4988)、`IsEffectiveStrictModeSourceFile`(2166-2192) 进 `Utilities.Checker.cs`，`IsNonNullChain`(utilitiesPublic.ts:1437-1439) 进 `Utilities.cs`（`IsPropertyAccessChain`/`IsCallChain` 旁），私有副本删除、调用点改指。
5. **模块切片的 4 个测试失败是它自己的测试夹具缺陷**，非产品缺陷：`CreateProgram` 把 `EsModuleInterop` 默认成 `false`，而参考（以及端口 `Utilities.GetESModuleInterop`）是 `?? true`，导致 `__importStar`/`__importDefault` 未被请求（期望值本就是参考字节）⇒ 夹具参数改为 `bool? = null` 透传默认。期望值未改一字。
6. **`ProgramTests` 的阻断断言换成真行为断言**：`TheEmitSurfaceIsRealAndTheTransformerChainIsAssembled` 断言 `GetDeclarationDiagnostics` 不再抛、并逐项校验链的组成（首为 `TransformTypeScript`、`ClassFields` 先于 `ES2015`、`Generators` 紧随其后、末为 `ImpliedNodeFormatDependent.GetModuleTransformer(CommonJS)`、声明侧为 `TransformDeclarations`、`emitOnly` 与 `NoTransformers` 同实例）。

### 17.13 M7-C 首次贯通：EmitDiff 全语料逐字节一致

组装 `getTransformers` 后（§17.12），`tools/EmitDiff` 对同一份 5,908 文件语料同时跑参考 `lib/typescript.js`（6.0.3）与端口 `Program.Emit`（单文件程序、`target/module=ESNext`、`allowJs`、`newLine=LF`、`suppressOutputPathCheck`），对每个文件写出的 `.js` 取 FNV-1a 哈希逐字节比对：

| 轮次 | 结果 | 说明 |
|---|---|---|
| 首轮（未修 visitor 窄化） | `same=5890 diverged=0 port-crash=18` | 18 条崩溃**全部**是同一缺陷：`Utilities.VisitEachChildWorker` 把 `PrefixUnaryExpression` 分派强转成 `JsonMinusNumericLiteral`（§17.12 缺陷 3），任何含 `-x`/`+x` 的文件即崩 |
| 修后（当前树） | **`same=5908/5908 diverged=0 port-crash=0 ts-error=0 unpaired=0`（VERDICT clean）** | 5,908 个文件的 `.js` 与参考**逐字节相同**，零崩溃、零分歧 |

同一树的 checker 侧外环（`tools/TypeDiff`，真源 `lib/typescript.js`）：**`same=5908/5908 diverged=0 port-crash=0 ts-error=0 unpaired=0`（VERDICT clean）** —— 历史上最后三条分歧（1 条诊断码错配 + 2 条类型 id 偏移）分别由 §17.13.1 与 §17.13.2 修掉。

验收命令（可复现）：
```
dotnet run --project tools/EmitDiff -- --cases D:/Source/GitHub/TypeScript/tests/cases/conformance --out artifacts/M7-C-emit-final
dotnet run --project tools/TypeDiff -- --cases D:/Source/GitHub/TypeScript/tests/cases/conformance --out artifacts/M7-C-typeside-final
```

**仍未闭合（M7-C 尾项，已登记）**：声明发射（`.d.ts`）与 `tsc --declaration` 的逐字节比对（declarations 切片已发现一例"解构 + 参数属性 + 显式数组类型"分歧）；剩余 3 条 TypeDiff 分歧；`.errors.txt`/baseline 全量比对（P1b）。`.js.map`/`sourceMappingURL` 侧已收口，见 §17.13.3。

#### 17.13.1 第三条 TypeDiff 分歧修掉：`base.declarations?.every(…)` 的 `?.` 丢了

`getPropertiesOfType(baseType)` 的成员覆盖循环里，参考的
`base.declarations?.every(d => isPropertyAbstractOrInterface(d, baseDeclarationFlags))`（checker.ts:47593）在基符号**无声明**时短路成 `undefined`（假值）而**不**继续；端口写成 `Core.Every(baseSymbolTarget.Declarations, …)`，而 `Core.Every` 对 `null` 列表答 `true`，于是这类覆盖（元组成员、映射类型成员，如 `[string, number]` 的 `0`）一律 `continue`，其后的 `PropertyWillOverwriteBaseProperty`（2612）永不可达。

实测路径：`interfaceExtendsObjectIntersectionErrors.ts` 的 `class C4 extends Constructor<T4>() { 0: number }` —— 参考在同一节点同时报 2416 与 2612，端口只报 2416（用临时探针定位：基符号 `Flags=Property, Transient`、`Declarations=null`，`CheckFlags.Synthetic` 为假 ⇒ 走 `Every` 臂 ⇒ 落在 `continue` 上）。加 `is not null` 守卫后：**TypeDiff 全语料 `same=5906/5908 diverged=2`**（此前 5905/3）。同型审计：参考 `checker.ts` 全文只有两处 `?.every(`，另一处（51110）端口早已带 `is not null` 守卫。

#### 17.13.2 最后两条 TypeDiff 分歧修掉：`getTypeOfPropertyOrIndexSignatureOfType` 的错移植 + 一处 A″ 延迟被本语料观测到

**收尾实测：TypeDiff 全语料 `same=5908/5908 diverged=0 port-crash=0`（VERDICT clean，此前 5906/2）。**

两条都是**类型创建计数差**，用同一个临时探针定位（把 `createType` 的 `typeCount`/`TypeFlags`/调用栈逐次写文件：参考侧是 `lib/typescript.js` 的副本，在 `createType` 里插一段 `appendFileSync`；端口侧在 `Checker.CreateType` 里按 `TSC_CT_LOG` 环境变量打同构的点；两侧跑完即删/`git checkout` 还原，仓库最终无探针痕迹）。因为探针记的是**全部**创建（不只是 dump 里可达的那些），两侧的创建序列可以逐项对齐——这正是 dump 只报"id 偏移"时唯一能落地的定位手段。判定方法先把 dump 里 `(dump 序号, ref id, port id)` 按 ref id 排序：只有 deficit 随 ref id 单调时才可能是一条偏移，再拿探针序列做逐项对比。

**① `types/union/discriminatedUnionTypes2.ts`（`T#2` `id=178` 对 `179`，端口少 1 次创建）。** 探针指向 `f31` 的 `if (foo.tag === true)`：参考在 `narrowTypeByDiscriminant` 的过滤回调里调 `getTypeOfPropertyOrIndexSignatureOfType`（checker.ts:11584-11589），对 `Foo` 的 `{ [x: string]: string }` 成员取到索引签名类型后还要过 `addOptionality(propType, /*isProperty*/ true, /*isOptional*/ true)` ⇒ `getOptionalType` 造出 `string | undefined` 这个联合，那一次 `createType` 就是缺的量。端口整个函数是

```csharp
var prop = GetPropertyOfType(type, name);          // 语义等同 getTypeOfPropertyOfType
return prop is not null ? GetTypeOfSymbol(prop) : GetIndexTypeOfType(type, _stringType);
```

两处都错：索引回退既不是 `getApplicableIndexInfoForName(type, name)?.type`（按**名字**取可用索引信息，而不是"字符串索引签名"那一条），又丢了 `addOptionality`。改写成参考的三段顺序（`getTypeOfPropertyOfType` 短路 → `getApplicableIndexInfoForName` → `AddOptionality`）后，两侧创建序列 **198/198 逐项相同**（id + TypeFlags）。参考全文该函数只有 3 个调用点，端口也是同样 3 个（`Checker.Relations.Identical.cs:150`、`Checker.Inference.cs:5584`、`Checker.Check.cs:1090`），无漏网。

**② `types/typeRelationships/typeInference/genericCallToOverloadedMethodWithOverloadedArguments.ts`（`id=332` 对 `340`，端口少 8 次创建）。** 探针显示参考多出的 8 次全部来自 `signatureToString(c)`（checker.ts:81308-81317 的 `chain2`），链是 `checkTypeRelatedTo → chain2 → signatureToString → signatureToSignatureDeclaration → symbolToParameterDeclaration → getTypeOfSymbol(瞬时形参符号) → instantiateType`：**打印**候选签名时，把 `Promise<number>.then` 实例化签名里那几个瞬时形参的类型实例化出来（每次 2 个：匿名对象类型 + 联合，m3/m4/m5/m6 各贡献若干）。端口在 `Checker.CallResolution.cs:1778` 把该实参包成 `Diagnostic.DeferArg(...)`（M6-late 的 A″），于是 `chain()` 虽在 `CheckTypeRelatedTo` 里被调用、实参却无人读 ⇒ 这一步永不发生。改成在 `chain` 体内直接求值（与参考同一时点：`CheckTypeRelatedTo` 调 `chain` 时）后，两侧 **346/346 逐项相同**。

**A″ 的残留账（仍留给裁定，不在本次范围内）：** ② 只是 A″（约 200 个调用点，前文"需在 295 条分歧收完之后单独裁定"的那条）里**被本语料观测到**的那一个；机制上是"参考在调用点就算出消息实参，而打印会把瞬态符号的类型实例化出来"，所以凡实参为 `typeToString`/`symbolToString`/`signatureToString`/`typePredicateToString` 的诊断调用点，只要其链在检查期被求值，就同样能移动 `typeCount`。本语料现在对其余调用点零分歧（实测 5,908/5,908），整体去留仍应由那条裁定决定——前文已量出"放开 printer 门控会让 `diverged` 增加约 1,041"，而 A″ 使门控只在消息文本真被读到时才触发；本次只在**已量出可观测的唯一调用点**上改成参考的求值时点，其余调用点一律未动。

**验收（实测）**：`dotnet build src/TypeScript.Compiler` 0 错误 / 0 警告；`dotnet test --filter FullyQualifiedName~Checker` **108/108 通过**（整份测试由集成点复跑）；`dotnet run --project tools/TypeDiff -- --cases …/tests/cases/conformance` **`same=5908/5908 diverged=0 port-crash=0`（VERDICT clean）**。

#### 17.13.3 M7-C 尾项之 `.js.map` 侧收口：EmitDiff 开 `--source-map`，端口 source-map 路径逐字节一致

§17.12 落地的 source-map 切片（`SourceMap{,.Generator,.Mappings,.DocumentPosition}.cs` + `Emit/Printer.SourceMaps.cs`）此前**完全不被外环跑到**：EmitDiff 的冻结选项集里没有 `sourceMap`，一次 `.js.map` 都不产生，而端口侧 `Emit/Emitter.cs` 的私有 `CreateSourceMapGenerator` 仍是抛 `NotSupportedException` 的具名阻断（`emitter.ts:997-1005` 的调用点被原样留着）。

**改动（两处）**：

1. **端口侧最后一米**：`Emit/Emitter.cs` 的 `CreateSourceMapGenerator` 从"具名阻断"换成 `SourceMap.CreateSourceMapGenerator`（`src/compiler/sourcemap.ts:36-58`），并把 emitter 的 `SourceMapOptions` 视图映射到生成器唯一读的成员 `SourceMapGeneratorOptions.extendedDiagnostics`（参考里那个接口是结构类型，C# 里就是这一句）。这是 emitter 自己的入口，printer 侧（`Printer.SourceMaps.cs`）此前已就位。
2. **工具两侧一起扩**：`tools/EmitDiff/Program.cs` 的 `PortOptions` 与 `ts-emit.cjs` 的 `options` 同时新增参考 source-map 路径读的五个 `CompilerOptions` 槽——`sourceMap`/`inlineSourceMap`/`inlineSources`/`sourceRoot`/`mapRoot`，由 `--source-map`/`--inline-source-map`/`--inline-sources`/`--source-root <d>`/`--map-root <d>` 驱动（不带 flag 时这五个槽在两侧**都缺席**，所以默认运行的选项对象与之前逐字节相同、判决与历史可比）。**dump 格式一个字没改**：`begin`/`end` 本来就按"发射器写出的每个输出"重复，于是 `.js.map`（emit 先写它，`emitter.ts:1099-1121`）与 `.js`（含 `//# sourceMappingURL=` 注释，`emitter.ts:1053-1055`）各自成为一条记录，名字/顺序/长度/字节全部进同一个 FNV-1a 哈希。`report.txt` 的 `# options` 行按同样的顺序追加实际开到的槽位。

**实测（同一份 5,908 文件语料，参考 `lib/typescript.js` 6.0.3 的 `program.emit`，即 `tsc --sourceMap` 同一发射器）**：

| 运行 | 结果 |
|---|---|
| 修前（阻断在位）`--source-map` | `same=36 diverged=0 port-crash=5872 unpaired=0`（36 个仍 `same` 的是不产出 JS 的 `.d.ts` 输入，发生器根本没被请求） |
| 修后 `--source-map` | `same=5908/5908 diverged=0 port-crash=0 ts-error=0`（VERDICT clean，24 批 node；参考侧 24 批合计写出 `emitted=11,744` 个输出 = 5,872 份 `.js` + 5,872 份 `.js.map`、7,216,081 字节，端口侧逐文件哈希与 dump 长度与之相同） |
| `--source-map --inline-sources` | 同上 clean |
| `--source-map --map-root ../maps` | 同上 clean |
| `--source-map --source-root /src` | 同上 clean |
| `--inline-source-map` | 同上 clean |
| `es6/**`（1,045 文件）`--source-map --inline-sources --source-root ../src --map-root ../maps` | `same=1045/1045` clean |
| `es6/**`（1,045 文件）`--inline-source-map --inline-sources` | `same=1045/1045` clean |

除上表外，另用 16 个自造对抗夹具（非 ASCII 文件名与源文本、BOM、CRLF、U+2028/U+2029 行分隔、shebang、空文件、`.d.ts`、`.js`、`.tsx`、含空格/`#`/`+` 的文件名、60 行多段映射文件）把 `sourceRoot`/`mapRoot` 的绝对/相对/空/带空格/URL/Windows 盘符各臂、`inlineSourceMap`+`inlineSources` 的 base64 也压了一遍，全部逐字节一致；该临时语料与测量脚本只用于量期望值，未留在树里（`artifacts/` 下已删）。

**测试**：`tests/TypeScript.Compiler.Tests/Transforms/SourceMapGeneratorTests.cs` 由 18 条增至 **23 条**（新增：发射路径的写序与两段文本、`inlineSources` 的 `sourcesContent`、`sourceRoot`+`mapRoot` 的注释与源根、`inlineSourceMap` 的 `data:` URL，以及**任何语料都到不了**的 `sourceMapRange.source` 换源臂——外环逐文件发射，永远不会有节点的 source-map 范围指向另一个源，所以 `emitSourcePos` 的 `source !== sourceMapSource` 分支只能由单测驱动）。期望值全部先用参考 6.0.3 现量（`artifacts/` 下的临时 node 脚本，量完即删）。

**结论**：`Emitter.cs` 那一行阻断是这条路径上唯一的真缺陷；把它换成真生成器后，端口 source-map 路径与参考在全部可达配置下**零分歧、零崩溃**。仍未闭合：声明发射（`.d.ts`）、剩余 TypeDiff 分歧、`.errors.txt`/baseline 全量比对（P1b）。

#### 17.13.4 M7-C 尾项之 `.d.ts` 侧：声明发射逐字节收敛（7 处缺陷）

以 `node lib/tsc.js --declaration --emitDeclarationOnly` （6.0.3）为真源，对声明切片留下的那例分歧（解构导出列表 + 参数属性 + 显式数组类型）追到根，并顺带清掉 7 处真缺陷（`DeclarationsTransformTests` 4 → 11 条逐字节用例，每条都先证明"修前失败"）：

| # | 位置 | 缺陷 | 参考 |
|---|---|---|---|
| 1 | `Checker.Check.Source.cs` `IsImplementationOfOverload` | 把 `node.Body` 强转读：索引签名 `isFunctionLike` 但无 body 槽 ⇒ `InvalidCastException`（`class C { [k: string]: unknown }` 即崩） | checker.ts:50832-50833 + declarations.ts:1036 |
| 2 | `Checker.Stringify.cs` `IsEntityNameVisible` | 结果对象类型写成 `SymbolVisibilityResult`（缺 `errorModuleName`），与 `HasVisibleDeclarations` 的 `SymbolAccessibilityResult` 不是同一形状；后者已删 | declarations.ts:823-826 |
| 3 | `Checker/SyntacticNodeBuilder.Kinds.cs` `IsPrimitiveLiteralValue` | default 臂调 `Debug.AssertNever`，而参考的 `assertType<never>` 是**纯类型层**断言 ⇒ 非字面量实参会误抛；改答 `false` | utilities.ts:12051 |
| 4 | `Emit/Printer.cs` `IsEmptyBlock` | 少了"块的起止在同一行"合取项 ⇒ `declare namespace M { }` 打成一行的判定错；同时删掉 `Printer.Statements.cs` 里只按语句数判空的重复 `IsEmptyBlockLike` | emitter.ts 的空块判定 |
| 5 | `Parser/NodeFactory.cs` `CreateNumericLiteral` | `string \| number` 槽只处理了整数 ⇒ 传 double 时 `Text` 为 `null`（声明发射的 `0: number` 属性名即触发） | nodeFactory.ts（`createNumericLiteral`） |
| 6 | `Transforms/Declarations/DeclarationsTransform.Members.cs` | 迟到替换表的值（`Node \| Node[]`）被强转成 `IList<IStatement>` ⇒ 改走 `AnyReplacement`（`IsNodeArray` + 元素循环） | declarations.ts:1700 附近 |
| 7 | `Utilities/Utilities.Visitor.cs` | `IsNodeArray` 由 private 提为 internal，供声明变换复用同一判定 | — |

另有一处**夹具而非产品**：`DeclarationsTransformTests` 的选项集漏了 `Declaration = true`，而 `getEmitDeclarations` 决定 `checkExportSpecifier` 是否 `collectLinkedAliases(setVisibility: true)`，那是解构绑定元素可见性的唯一来源（checker.ts:48823-48825 + 11348/11454 + declarations.ts:756-767、1760-1761）。补上后端口与真源逐字节一致。

#### 17.13.5 成员归位收口：`getAllAccessorDeclarations` × 2 → 1，`namedEvaluation` 侧的三个 `utilities.ts` 成员入 `Utilities`

- `getAllAccessorDeclarations`（utilities.ts:6832-6885）此前在 `Utilities.Checker.cs` 的装饰器判定里有一份**局部函数**、在 `Transforms/Declarations/DeclarationsDiagnostics.cs` 里又有一份**静态局部**。二者合一为 `Utilities.GetAllAccessorDeclarations(System.Collections.IEnumerable?, Node)`（两个调用方分别递入 `IClassLikeDeclarationBase.Members` 与符号声明表 —— 同一个"访问器列表"的两种泛型面，故取非泛型序列 + 模型既有 cast），两处私有副本删除、调用点改指。它与 checker 自己的 `GetAllAccessorDeclarationsForDeclaration`（基于符号声明，checker.ts:51205 邻近）**不是**同一成员，名字不冲突。
- `isAnonymousFunctionDefinition`(utilities.ts:5485)、`isNamedEvaluation`(utilities.ts:5564)、`isEmptyStringLiteral`(utilities.ts:3604) 此前寄居 `Transforms/NamedEvaluation.cs`（因 `Utilities` 当时未落地），现归位 `Utilities.Checker.cs`；`Transforms/NamedEvaluation.cs` 只留 `namedEvaluation.ts` 自己的成员（`classHasExplicitlyAssignedName`/`classHasDeclaredOrExplicitlyAssignedName` 等，utilities.ts 的 `isAnonymousFunctionDefinition` 反向调用后者 —— 参考里就是这条跨模块调用，端口按名具名）；8 个文件的 25 处调用点改指 `Utilities.*`。

两项都是纯搬运：搬运后 **build 0/0、测试 1,520/1,520、TypeDiff `same=5908/5908` clean、EmitDiff `same=5908/5908` clean**（行为中性）。

#### 17.13.6 基线环（§17.14）发现的两处编译器侧缺陷（已提交 `9dd4e37` 并复核）

§17.14 的基线环比前三环多做了两件事：它真的去读语料自带的 `// @` 指令，并且把**合成节点**（checker/nodeBuilder 造出来、给 `.types` 下划线用的那些）也打进了比对。据此发现的两处缺陷都在编译器侧，都已被集成方提交并复核（**build 0/0、测试 1,520/1,520、TypeDiff 全语料 `same=5908/5908` clean、EmitDiff 全语料 `same=5908/5908` clean**）：

| # | 位置 | 缺陷 | 参考行 | 观测路径 |
|---|---|---|---|---|
| 1 | `Checker/NodeBuilder.Names.cs:346,352`（`getSpecifierForModuleSymbol` 的 ambient module 两臂） | JS `substring(1, name.length - 1)` 被读成**长度**（`name.Substring(1, name.Length - 1)`），于是合成 `import("m")` 字面量的 `Text` 留着收尾引号，`Symbol(C[Symbol.iterator]` 一类的名字变成 `m"`；改为 `EscapedName![1..^1]` | `checker.ts:8611,8617` | `.types`/`.symbols` 对 `declare module 'm'` 打出 `typeof import("m\"")`（`exportEqualsOfModule`、`cyclicModuleImport`） |
| 2 | `Parser/NodeFactory.cs:290-334`（`CreateTokenNode`） | token 各臂直接 `new PunctuationToken/KeywordTypeToken/ModifierToken/KeywordToken/Token`，绕过 `CreateBaseNode` 的 `NodeFlags.Synthesized` 标记；补 `if (_synthesizedNodes) node.Flags \|= NodeFlags.Synthesized;`（switch 需要显式 `Node` 目标类型） | `factory/nodeFactory.ts:7393-7405`（`createBaseTokenNode` 被 `makeSynthetic` 包着） | `Utilities.IsParseTreeNode`/`GetParseTreeNode` → `src/harness/typeWriter.ts:35-37` 的 `nodeIsFullySynthetic`：修前每条合成关键字类型的 `.types` 下划线打空格而非 `^`（`binaryIntegerLiteralES6.types`、`generatorTypeCheck64.types` 各差若干净行），修后逐字节一致 |

两处的共同判据是**基线为真源**：参考编译器能逐字节复现签入基线（§17.14.1 的 148 文件自检），所以基线里的 `^` 与 `"m"` 就是期望行为，端口打不出即是缺陷。

## §17.14 M7-C 最后一环：`tools/BaselineDiff`（官方 harness 选项模型 + 基线逐字节比对）

前三环（`tools/TypeDiff`、`tools/EmitDiff`、声明发射）都只有**一个固定选项对象**或干脆没有 program，**没有任何一环读到语料自带的 `// @` 指令**——也就是说，端口在"参考自己的测试用哪些选项、哪些文件被拆成多单元、哪些配置会分叉"这件事上一无所证。本环补上这个缺口，且它的第三方不是参考编译器而是**文件**：`tests/baselines/reference` 里的基线。

判据是三路两判：**端口 vs 基线**（本环的判决）、**参考 vs 基线**（harness 模型自检，模型错则该 case 的端口判决一文不值）、以及**多余基线**（有基线但模型从未派生出对应的 case ⇒ 配置模型漏了一个 harness 跑过的用例）。

```
dotnet run --project tools/BaselineDiff -- \
  --cases D:/Source/GitHub/TypeScript/tests/cases/conformance \
  --baselines D:/Source/GitHub/TypeScript/tests/baselines/reference \
  --node-parallel 8
```

### 17.14.1 harness 模型：全部取自参考自身，并由参考半场自检

| 环节 | 出处 |
|---|---|
| `// @` 指令、单元切分、`@link` | `src/harness/harnessIO.ts:1219-1390`（`TestCaseParser`） |
| 选项声明与取值转换 | `src/harness/harnessIO.ts:255-325`（+ harness 自有的 15 个选项）over `src/compiler/commandLineParser.ts:1184-2900` |
| 选项模型表（143 条，含 kind/`isFilePath`/`isCommandLineOnly`/`affects*`/custom-type 映射） | 由 `lib/typescript.js` 6.0.3 的 `optionDeclarations` 生成进 `tools/BaselineDiff/BaselineDiffOptions.cs`（不是手抄的第二份模型） |
| `target/module/lib/strict/newLine` 默认值 | `harnessIO.ts:333-380` + `compilerImpl.ts:265-300`（`newLine=CRLF`、`noErrorTruncation`、`skipDefaultLibCheck`、`noResolve:false`） |
| 分叉配置与文件名主干 | `src/testRunner/compilerRunner.ts:129-350` + `harnessIO.ts:1078-1175`（`<base>(key=value,…).ts`，key/value 全小写并排序） |
| 虚拟文件系统 | `vfsUtil.ts:25-30,1261-1300,1573-1580`：文档在 `/.src`，`/.ts` 挂到 `<TypeScript>/lib`（harness 挂的是 `built/local`，本 checkout 无 `built/`）、`/.lib` 挂 `tests/lib`，除非 `@useCaseSensitiveFileNames` 否则大小写敏感 |
| host | `fakesHosts.ts:244-430`（`jsDocParsingMode: ParseForTypeErrors`、`setParentNodes: true`、输出经 `writeFile` 收集） |
| `.errors.txt` / `.js`（含声明输出）/ `.types`/`.symbols` | `harnessIO.ts:494-661` / `:910-1005` / `typeWriter.ts` + `harnessIO.ts:663-860` |
| 基线头部路径 | case 文件相对**工作区根**的路径（`//// [tests/cases/conformance/…] ////`） |

**参考半场自检（实测）**：对 148 文件 / 182 case 的样本（`tests/cases/conformance` 每 40 个取 1，排序后），`ts-baseline.cjs` 与签入基线**逐字节相同** —— `files=148 cases=182 same=689 diverged=0 unmodelled=0 errors=0 extra=0`（`.js` 182/182、`.errors.txt` 182/182、`.types` 182/182、`.symbols` 182/182）；60 文件分片另测 `same=276 diverged=0`。

自检过程抓出并修掉 **8 处 harness 建模缺陷**（reference-vs-baseline 不一致是模型缺陷、不是端口缺陷）：①`.js`/`.types` 头部是工作区相对路径而非语料相对路径、也不是配置名；②诊断为空时 `doErrorBaseline` 必须传 `null`（即 `<no content>`）而不是打出 `(0 errors)` 段；③`CompilationResult` 必须把 `resolveJsonModule` 的 `.json` 产物算作 js 产物；④host 必须设 `jsDocParsingMode: ParseForTypeErrors` 与 `setParentNodes: true`；⑤虚拟文件系统的目录层必须认 `/` 与每个文档的祖父目录（否则 `import './a'` 解析失败、`.types` 打印 `any`）；⑥`readDirectory` 必须是真遍历；⑦类型/符号写入器的符号分支要 `Symbol(<symbolToString>, Decl(file,line,char)…)` 包装（五条声明上限、lib 文件打 `--`）；⑧`=== Performance Stats ===` 块是**程序自己的** checker 计数（阈值 1000/1000/1000/1000/1000/1500/45000、`toLocaleString("en-US")`），207 份 `.types` 基线带它。

### 17.14.2 实测三态表（60 文件分片，含命令）

```
dotnet run --project tools/BaselineDiff -- --cases D:/Source/GitHub/TypeScript/tests/cases/conformance \
  --baselines D:/Source/GitHub/TypeScript/tests/baselines/reference --out artifacts/bd-run4 --limit 60 --node-parallel 4
```

| 产物 | same | diverged | port-crash | ts-error | unpaired | unmodelled | skipped | extra-baseline |
|---|---|---|---|---|---|---|---|---|
| `.js` | 65 | 1 | 4 | 0 | 0 | 0 | 0 | 0 |
| `.errors.txt` | 66 | 0 | 4 | 0 | 0 | 0 | 0 | 0 |
| `.types` | 65 | 0 | 4 | 0 | 0 | 0 | 1 | 0 |
| `.symbols` | 54 | 11 | 4 | 0 | 0 | 0 | 1 | 0 |

`SUMMARY files=60 cases=70 same=250 diverged=12 port-crash=16 unmodelled=0 unpaired=0 extra-baseline=0 ref-diverged=0 ref-crash=0`（参考半场 276/276 逐字节相同、零分歧；`.symbols` 那 1 条 `skipped` 是该 case 全 `.d.ts`、`.js` 侧同理无基线）。

**分歧只 2 类**：

1. **`.symbols` 11 例（唯一类）**：合成 symbol 名的 `]` 多打一个。
   ```
   port:     >[Symbol.iterator] : Symbol(C[Symbol.iterator]], Decl(ES5SymbolProperty2.ts, 3, 20))
   baseline: >[Symbol.iterator] : Symbol(C[Symbol.iterator], Decl(ES5SymbolProperty2.ts, 3, 20))
   ```
   复现对：`tests/cases/conformance/es6/Symbols/ES5SymbolProperty{2,3,4,5,6,7}.ts`（`target=es5`/`es2015` 两种配置）。落在 checker 的 `symbolToString`（`Checker.Stringify.cs` 的合成名渲染）；本次未定位到具体行，已登记。
2. **`.js` 1 例**：发射产物**少了一条首部注释**。
   ```
   port:     "use strict"; Object.defineProperty(exports, "__esModule", { value: true });
   baseline: "use strict"; // https://github.com/microsoft/TypeScript/issues/7840
             Object.defineProperty(exports, "__esModule", { value: true });
   ```
   复现对：`tests/cases/conformance/ambient/ambientModuleDeclarationWithReservedIdentifierInDottedPath.ts`。

**4 个 case 崩溃（16 = 4×4 产物）**，四类异常各一（都是"该判 kind 却直接 cast"的形状，与 §17.13.4 的历史缺陷同族）：

| case | 异常 | 说明 |
|---|---|---|
| `es6/Symbols/ES5SymbolProperty2.ts`（`target=es5`） | `NullReferenceException` | `target=es2015` 配置不崩 |
| `async/asyncArrow/asyncArrowFunction_allowJs.ts` | `InvalidCastException: JSDocFunctionType → IFunctionLikeDeclarationBase` | JSDoc 函数类型混入函数式节点判定 |
| `async/.../asyncFunctionDeclarationParameterEvaluation.ts`（`target=es2015`） | `InvalidCastException: FunctionDeclaration → ShorthandPropertyAssignment` | |
| `async/.../asyncMethodWithSuperConflict_es6.ts` | `InvalidCastException: MethodDeclaration → ShorthandPropertyAssignment` | 与上一条同一判定 |

失败记录现已附**端口帧**（`FrameNames`，与 `tools/EmitDiff` 同形），于是两类崩溃可当场定位：

* **`ClassDeclaration/MethodDeclaration/FunctionDeclaration → ShorthandPropertyAssignment`**（`symbolDeclarationEmit1/11/…`、`disallowLineTerminatorBeforeArrow.ts` 等，本分片 15 个 crash 里的绝大多数）：
  ```
  Checker.<CalculateNodeCheckFlagWorker>g__IsExpressionNodeOrShorthandPropertyAssignmentName|364_6 (Checker.Check.Flags.cs:319)
  < …CheckSingleIdentifier|7 (Checker.Check.Flags.cs:333) < …CheckIdentifiers|4 (Checker.Check.Flags.cs:309)
  < ForEachChildTable.ForEachChildRecursively (ForEachChildTable.cs:481)
  ```
  根因单行：`var shorthandParent = (ShorthandPropertyAssignment?)identifier.Parent;` —— 参考的
  `isShorthandPropertyAssignment(node.parent) && (node.parent.objectAssignmentInitializer ?? node.parent.name) === node`（`checker.ts:51013-51017`）里前一项是**谓词**、后一项只在它成立时求值；端口把谓词写成了**无条件强转**，于是 `CalculateNodeCheckFlagWorker` 的 `ForEachChildRecursively` 走到任何非 shorthand 的父节点（本分片里是 `ClassDeclaration`）即抛。改为 `identifier.Parent as ShorthandPropertyAssignment`（`as` 真转型，`&&` 短路保证只有守卫成立时才读 `shorthandParent!`）后该族崩溃清零。**这是编译器侧、单行、且与参考逐字对应**的修复，按集成方约定留在编译器的提交里（不在本环的 `tools/BaselineDiff/**` 提交中）。
* **`ModuleTransformer.GetExports` 的空引用**（`emitArrowFunction(target=es5).ts`）：
  ```
  ModuleTransformer.GetExports (ModuleTransform.Substitution.cs:247) < ModuleTransformer.SubstituteBinaryExpression (:203)
  < ModuleTransformer.SubstituteExpression (:90) < ModuleTransformer.OnSubstituteNode (:51) < ImpliedNodeFormatDependentTransformer.OnSubstituteNode (:146)
  ```
  只在 `target=es5`（即绕过模块变换后的降级发射路径）出现，未定位到单行根因，**报告不修**。
  修完 Class 3 后同一分片（120 文件 / 150 case）**只剩它 1 个 case 崩溃**（`port-crash 60 → 4` = 1 case × 4 产物），
  分歧也只剩 Class 2 那 1 条（`diverged 44 → 1`）——即该分片已收敛到"1 崩溃 + 1 分歧"。

**成本**：端口半场单线程 186.5 s / 275 case（≈0.68 s/case，每 case 含 1–2 个 program + 声明重编译 + noCheck 重编译 + 两次类型/符号遍历），因此**全语料单机约 2.6 小时**，必须按 `--limit`/`--filter` 分片；参考半场 60 文件 20.6 s（4 批并行）。集成方另跑的 `es6/**` 120 文件分片（150 case，耗时 534 s）给出同一结论且比例一致：

| 分片 | 命令 | ARTIFACT（same/diverged/port-crash） | SUMMARY |
|---|---|---|---|
| 60 文件（本环） | `--limit 60 --node-parallel 4` | js 65/1/4、errors 66/0/4、types 65/0/4、symbols 54/11/4 | `same=250 diverged=12 port-crash=16 ref-diverged=0 ref-crash=0` |
| `es6/**` 120 文件（集成方，修前） | `--filter "es6/**" --limit 120 --node-parallel 8` | js 135/0/15、errors 135/0/15、types 134/1/15、symbols 92/43/15 | `same=496 diverged=44 port-crash=60 ref-diverged=0 ref-crash=0` |
| 同上（集成方，`0f67b46` 之后） | 同上 | symbols **92/43 → 135/0**；js 135/0/15、errors 135/0/15、types 134/1/15 不变 | `same 496 → 539`；`ref-diverged=0 ref-crash=0` |
| 同上（`0f67b46` + §17.14.2 的 `IsExpressionNodeOrShorthandPropertyAssignmentName` 修复之后，本环复测） | 同上 | js **135/0/15 → 149/0/1**、errors **→ 149/0/1**、types **→ 148/1/1**、symbols **→ 149/0/1** | `same 496 → 595`、`diverged 44 → 1`、`port-crash 60 → 4`；`ref-diverged=0 ref-crash=0 unpaired=0 extra-baseline=0` |

`Checker/NodeBuilder.Names.cs:866` 的 `symbolName.substring(1, symbolName.length - 1)`（`checker.ts:8938`）被读成长度，是**同一族第三处**：合成成员名留着收尾 `]`（探针打出 `entity=ElementAccessExpression arg=Identifier:Symbol.iterator]`），打印器再加一个 `]` ⇒ `C[Symbol.iterator]]`。改为 `symbolName[1..^1]` 后 `.symbols` 43 → 0。

**两处仍未闭合（交给集成方）**：

* **`.types` 下划线中段空洞（工具侧，Class 2）**：`symbolProperty5.ts`（`es6/Symbols/`，`target=ES6`，`var x = { [Symbol.iterator]: 0, [Symbol.toPrimitive]() {}, get [Symbol.toStringTag]() {…} }`）。复现产物路径：`artifacts/bd-integration/port/symbolProperty5.types`（driver 的 `--out` 下的 `port/<基线名>`），对照 `tests/baselines/reference/symbolProperty5.types`：
  ```
  baseline: >  : ^^^^^^…^^^^              （连续一串）
  port:     >  : ^^^^^^…^^^^        ^^^^^^…^^^^   （中段 8 格空白）
  ```
  即端口在打印一个**嵌套节点**时把下划线关掉了、而参考保持打开：`nodeIsFullySynthetic`（`nodeIsSynthesized && !getParseTreeNode`）对该节点答 false。与 `9dd4e37` 的 factory token 同族、但节点种类不同；定位需要"打印被构造链的节点 kind/text"式探针（Class 1 用的同一手法）。
* **`(T?)expr` 硬转型扫查（编译器侧，交集成方修）**：C# 里 `(T?)x` 对**引用类型**仍是硬转型，而参考的写法往往是 `is…(x) && …` 谓词。全仓按 `\(([A-Z]\w*)\?\)\s*\w+\.(Parent|Expression|Name|…)\b` 且 `T` 是 `Model/NodeClasses.cs` 里的节点类扫出 2 处（扫查模式只覆盖了这批成员名，更广的成员名会更多）：
  * `Binder/Binder.Declarations.cs:344` — `(Identifier?)node.Name`
  * `Transforms/ESDecorators.ClassLike.cs:593` — `(Identifier?)node.Name`
  （已修的同类现场：`Checker/Checker.Check.Flags.cs:316-323` 的 `(ShorthandPropertyAssignment?)identifier.Parent`，参考 `checker.ts:51013-51017`。）

### 17.14.3 本轮落地的两处端口缺陷（已由集成方提交 `9dd4e37` 并复核）

| # | 位置 | 缺陷 | 参考行 | 证据 |
|---|---|---|---|---|
| 1 | `Checker/NodeBuilder.Names.cs:346,352`（`getSpecifierForModuleSymbol` 的 ambient module 臂） | JS `substring(1, name.length - 1)` 被读成**长度**，合成 `import("m")` 字面量的 `Text` 多一个收尾引号（`m"`）；改为 `EscapedName[1..^1]` | `checker.ts:8611,8617` | `.types`/`.symbols` 对 `declare module 'm'` 打印 `typeof import("m\"")`（`exportEqualsOfModule`、`cyclicModuleImport`） |
| 2 | `Parser/NodeFactory.cs:290-334`（`CreateTokenNode`） | 各 token 臂直接 `new PunctuationToken/KeywordTypeToken/ModifierToken/KeywordToken/Token`，绕过 `CreateBaseNode` 的 `NodeFlags.Synthesized` 标记；补 `if (_synthesizedNodes) node.Flags \|= NodeFlags.Synthesized;` | `factory/nodeFactory.ts:7393-7405`（`makeSynthetic`） | 经 `Utilities.IsParseTreeNode`/`GetParseTreeNode` 传导到 `src/harness/typeWriter.ts:35-37` 的 `nodeIsFullySynthetic`：修前每一条合成关键字类型的 `.types` 下划线打空格而非 `^`（`binaryIntegerLiteralES6.types`、`generatorTypeCheck64.types`），修后逐字节一致 |

复核结果（集成方实测）：**build 0/0、测试 1,520/1,520、`tools/TypeDiff` 全语料 `same=5908/5908` clean、`tools/EmitDiff` 全语料 `same=5908/5908` clean**。

### 17.14.4 本环确立的语料事实与仍未闭合项

**语料事实（实测）**：① `tests/baselines/reference` 下**没有独立 `.d.ts` 基线** —— 只有 `.types` 14,016、`.symbols` 14,016、`.js` 13,806、`.errors.txt` 9,055（另有 227 份 `.js.map`/`.sourcemap.txt` 与少量 `.trace.json`）；`tests/baselines` 下那 912 个 `.d.ts` 全属 projects/transpile/api 三个 runner。声明输出是**装在 `.js` 基线里**的（`doJsEmitBaseline` 在 JavaScript 之后追加每个 `.d.ts`，再追加 `//// [DtsFileErrors]` 段）。② 无诊断的测试**根本没有** `.errors.txt`（harness 传 `null`，基线机制按 `<no content>` 比较），"基线缺失"就是正确答案。③ 头部路径是工作区相对路径；④ 207 份 `.types` 带 Performance Stats 块。

**仍未闭合**：

* **全语料尚未跑通**：只实测了 60 文件分片；分片命令与三态表见 §17.14.2。全语料需 ~2.6 小时单线程，应分片或并行。
* **12 条分歧（2 类）+ 4 个崩溃 case（4 类）**：见 §17.14.2 表，均已给复现对；`.symbols` 的 `]` 与"丢首部注释"各一处，四类崩溃同族（缺 kind 判定的强转）。
* **模型未覆盖（报告而非默认）**：`tsconfig.json` 单元（语料 10 个文件，端口无 `parseJsonSourceFileConfigFileContent`，M8）；`// @link:` 符号链接（语料 0）；`@paths`/`@plugins`（object 型选项，参考自己的 `optionValue` 就抛）；`@pretty: true`（1 个文件，缺 `getErrorSummaryText`/`getErrorCountForSummary`/`getFilesInErrorForSummary`）；`noCheck` 比对里的 diff（9 份 `.js` 基线打印 jsdiff 的 `createTwoFilesPatch`，该包是参考的 npm 依赖、未随仓引入 ⇒ 这些 case 记为 `unmodelled`，不会"少打一段 patch 也算 same"）。
* **挂起的辅助检查**：`--limit`/`--filter` 分片下不验"多余基线"（分片必然覆盖不到多数基线），报告会显式说明。

### 17.14.4b 扩大分片：600 文件 / 821 用例的三态表与待办队列（集成方实测）

命令：`dotnet run --project tools/BaselineDiff -- --cases <conformance> --baselines <TS>/tests/baselines/reference --js <TS>/lib/typescript.js --limit 600 --node-parallel 8 --out artifacts/bd-wide`（约 30 分钟；端口半单线程）。

| artifact | same | diverged | port-crash | 说明 |
|---|---|---|---|---|
| `.js` | 798 | 7 | 14 | 7 条分两类：5 条缺 `__classPrivateField…` 帮助调用、2 条缺 `Reflect.set(_c,` —— 均为 `target=es2015` 的降级发射 |
| `.errors.txt` | **807** | **0** | 14 | 821 用例零分歧（诊断文本、波浪线、章节头全对） |
| `.types` | 765 | 4 | 14 | 下划线长度差异（与 `17.14.8` 同族，另有未标 `Synthesized` 的工厂节点） |
| `.symbols` | 769 | 0 | 14 | 零分歧 |

崩溃 14 例、8 种形状：`BinaryExpression→…`(4)、`YieldExpression→…`(3)、`JSDocParameterTag→…`(2)、`ConditionalExpression→…`(1)、`JSDocFunctionType→…`(1)、`PropertyAccessExpression→TypeS…`(1)、`ArgumentOutOfRangeException`(1)、以及 `NotSupportedException: GetSpecifierForModuleSymbol`(1，**具名阻断**：module specifiers 尚未移植，属模块解析面)。

三类工作已派给切片代理（降级私有名发射 + 崩溃族），`.types` 下划线 4 例与具名阻断登记待办。

### 17.14.5 基线环驱动的三处编译器侧修复与本轮遗留（集成方）

本环（§17.14）暴露的缺陷中，编译器侧由集成方按环的分片复测收口；工具（`tools/BaselineDiff/**`）侧由切片代理提交 `743de84`：

| 提交 | 缺陷 | 参考 | 环上复测 |
|---|---|---|---|
| `9dd4e37` | 环境模块名的 JS `substring` 结束下标被当长度（说明符打成 `m"`）；工厂 token 未标 `Synthesized`（`.types` 少下划线） | checker.ts:8611/8617、nodeFactory.ts:7393-7405 | — |
| `0f67b46` | 计算属性名 `[Symbol.iterator]` 只剥了前括号（`symbolToString` 多一个 `]`） | checker.ts:8938 | es6/** 分片 `symbols` `same=92 diverged=43` → **`same=135 diverged=0`**；SUMMARY `same` 496 → **539** |
| `5f5df01` | `IsExpressionNodeOrShorthandPropertyAssignmentName` 把 `isShorthandPropertyAssignment(parent)` 谓词写成 `(ShorthandPropertyAssignment?)identifier.Parent`（C# 的 `(T?)x` 对引用类型是**硬转**）⇒ 类声明经节点遍历命中，15 条 `InvalidCastException` | checker.ts:50925 邻近 | es6/** 分片 `port-crash` 15 → 0（该 15 条即此一类） |

三处修复后：build 0 错误 0 警告、测试 1,520/1,520、`tools/TypeDiff` 全语料 `same=5908/5908` clean、`tools/EmitDiff` 全语料（`.js` 与 `--source-map`）`same=5908/5908` clean。

**本轮遗留（各有可复现对，登记待办）**

1. ~~**`.js` 首注释丢失**（已修，见 §17.14.6）~~：`ambientModuleDeclarationWithReservedIdentifierInDottedPath.ts`（`@module: commonjs`）—— baseline 在 `"use strict";` 与 `Object.defineProperty(exports,"__esModule",…)` 之间保留文件首注释 `// https://github.com/microsoft/TypeScript/issues/7840`，端口丢掉它。机制属 `emitSourceFile` 的 detached-comment 路径（emitter.ts:4292-4306 的 `shouldEmitDetachedComment` 与 prologue 列表的写出顺序）；复现：`--filter "ambient/ambientModuleDeclarationWithReservedIdentifierInDottedPath.ts"`。
   临时插桩（已还原）得到的定位：`EmitDetachedCommentsAndUpdateCommentsInfo` 对**声明**发射的 source file 确实找到 detached 范围（`rangePos=0 rangeEnd=233`、`DetachedCommentInfo(0, 54)`）并按参考调用 `writeComment`，但该上下文里 `ShouldWriteComment` 答 false（`onlyPrintJsDocStyle` 生效）—— 对 `.d.ts` 是正确行为；而 **`.js` 发射这一侧没有出现任何一次 `writeComment`**，即 `.js` 的 `emitSourceFile` 根本没走 `EmitBodyWithDetachedComments`（或走了却没有注释范围）。下一步应在此处按参考对比 `.js` 发射时 `statements[0]` 的 `isPrologueDirective`/`nodeIsSynthesized` 与注释范围的来源。
2. ~~**`.types` 下划线中段空洞**（已修，见 §17.14.8）~~：`symbolProperty5.ts` —— baseline 是一条连续 `^^^^`，端口中间空 8 格 ⇒ 某个嵌套节点的 `nodeIsFullySynthetic`（`src/harness/typeWriter.ts:35-37`）答 false。与 `9dd4e37` 的 token 标记同族、不同节点类；复现产物在 `artifacts/bd-integration/port/symbolProperty5.types` 第 6/8 行。
3. ~~**`GetExports` 空引用（target=es5 降级路径）**（已修，见 §17.14.7）~~：`NullReferenceException @ ModuleTransformer.GetExports (ModuleTransforms/ModuleTransform.Substitution.cs:247) < SubstituteBinaryExpression (:203) < SubstituteExpression (:90) < OnSubstituteNode (:51) < ImpliedNodeFormatDependentTransformer.OnSubstituteNode (:146)`，仅在 `@target: es5` 的降级发射上出现（分片 1 例：`emitArrowFunction(target=es5).ts`；帧见该分片的 `report.txt`）。非单行根因，需按参考逐层比对替换路径。

### 17.14.6 遗留 1 已修：转换后的语句表丢文本范围 ⇒ 首注释被吞

`updateSourceFile` 是每个重建语句表的变换（模块、jsx、ts）都会走的工厂成员，参考里**新语句表保留原语句表的文本范围**——用 release-6.0 自己的 `lib/typescript.js` 经 `after` 变换钩子实测：`ambientModuleDeclarationWithReservedIdentifierInDottedPath.ts` 变换后 `file.statements.pos === 0 && file.statements.end === 296`（文件范围 0..315），而合成出来的首语句 `pos === -1`。端口的 `CreateNodeArray` 给出 `-1/-1`，而 printer 的 `emitSourceFile` 正是把这个**数组**当作 detached-comment 的范围交给 `emitDetachedComments`（emitter.ts:4292-4306 → 5922-5950），`getLeadingCommentRanges(text, -1)` 因 scanner.ts:842 的 `pos >= 0` 守卫找不到任何注释 ⇒ 首个语句被变换掉时，文件首注释整块丢失（`.js` 输出里 `"use strict";` 之后直接是 `Object.defineProperty(exports, "__esModule", …)`）。

修法（`Parser/NodeFactory.cs` 的 `CloneSourceFileWithChanges`，带 PORT-NOTE 与实测数字）：`Utilities.SetTextRange(node.Statements, source.Statements)`。

实测：基线环同一用例 `same=3 diverged=1` → **`same=4 diverged=0`**；三条既有外环零回归（build 0/0、测试 1,520/1,520、TypeDiff `same=5908/5908` clean、EmitDiff `same=5908/5908` clean）。

### 17.14.7 遗留 3 已修：`getExports` 里的 `!` 把参考的 `?.` 变成空引用

参考 `getExports`（module.ts:2460-2480）三处读 `currentModuleInfo` 都写 **optional 链**：`currentModuleInfo?.exportedBindings[…]`（2464、2473）与 `currentModuleInfo?.exportSpecifiers.get(name)`（2479）；端口写成 `_currentModuleInfo!`（null-forgiving）⇒ 当替换钩子跑在模块变换的文件作用域之外（`@target: es5` 的降级路径会走到）时抛 `NullReferenceException`（栈：`GetExports (ModuleTransform.Substitution.cs:247) < SubstituteBinaryExpression (:203) < OnSubstituteNode (:51) < ImpliedNodeFormatDependentTransformer.OnSubstituteNode (:146)`）。

修法：三处按参考的 `?.` 语义改写（前两处判空后返回 `null`/取 `null`，第三处用 `_currentModuleInfo?.ExportSpecifiers.Get(name)`）。

实测：基线环 `emitArrowFunction.ts`（2 个用例）由 1 崩溃变为 **`same=8 diverged=0 port-crash=0`（VERDICT clean）**；三条既有外环零回归（build 0/0、测试 1,520/1,520、TypeDiff `same=5908/5908` clean、EmitDiff `same=5908/5908` clean）。

### 17.14.8 遗留 2 已修：工厂造的修饰符 token 未标 `Synthesized` ⇒ `.types` 下划线中段空洞

harness 的下划线规则是逐节点的 `nodeIsFullySynthetic(node) = nodeIsSynthesized(node) && !getParseTreeNode(node)`（`src/harness/typeWriter.ts:35-37`），而 `getParseTreeNode` 的第一步是**只看标志位**的 `isParseTreeNode(node) = (node.flags & NodeFlags.Synthesized) === 0`。端口的 `NodeFactory.CreateModifier` 直接 `new ModifierToken(kind)`，既没有走 `CreateBaseNode` 的标记，也没有 `9dd4e37` 给 `CreateTokenNode` 补的那一步 —— 于是 `readonly` 修饰符 token 虽然 `pos = -1`（`nodeIsSynthesized` 为真），却因标志位缺失被当成语法树节点，`nodeIsFullySynthetic` 答 false，打印器在它前后切换下划线 ⇒ `symbolProperty5.ts` 的 `.types` 第 6 行正好在 `readonly` 那 **8 个字符**上留出空洞（实测插桩：`kind=ReadonlyKeyword flags=None pos=-1 syn=True parseTree=node:ReadonlyKeyword fullySynthetic=False`）。

修法：`CreateModifier` 与三处 `?? new PunctuationToken(...)` 默认 token 改走新的 `MarkSynthesized<T>`（照 `makeSynthetic`，factory/nodeFactory.ts:7393-7405；解析器自己的工厂 `_synthesizedNodes == false` 不标），并带 PORT-NOTE 记录实测。

实测：es6/** 分片（120 文件 / 150 用例）`ARTIFACT types 148/1/1` → **`150/0/0`**，四类 artifact 全 150/150 ⇒ 该分片 **`cases=150 same=600 diverged=0 port-crash=0`（VERDICT clean）**；三条既有外环零回归（build 0/0、测试 1,520/1,520、TypeDiff `same=5908/5908` clean、EmitDiff `same=5908/5908` clean）。

### 17.14.9 基线环驱动收口（二）：7 条 `.js` 分歧 = 一处 `multiLine`，11/14 崩溃已修

分片代理（`DownlevelPrivateNameEmit` + 其子片）交付、集成方验证并提交：

**7 条 `.js` 分歧同一根因**（不是"少文本"而是**版式**）：`NodeFactory.CreateAssignmentTargetWrapper`（nodeFactory.ts:6725-6745）里参考写 `createBlock([createExpressionStatement(expression)])`（**不传** `multiLine` ⇒ `undefined`），端口写成 `CreateBlock([...], true)`；`shouldEmitBlockFunctionBodyOnSingleLine`（emitter.ts:3449-3482）在 `if (body.multiLine) return false` 处提前返回 ⇒ 私有名解构目标被包出来的 `{ set value(_b) { … } }` 打成三行而非内联。改为 `null` 后：7 个 fixture 的过滤器（`privateNameFieldDestructuredBinding*`、`privateNameMethodAssignment*`、`privateWriteOnlyAccessorRead*`、`privateNameStaticFieldDestructuredBinding*`、`privateNameStaticMethodAssignment*`、`thisAndSuperInStaticMembers*`）全部 `VERDICT clean`。新增单测 `PrivateNameDestructuringTarget_EmitsTheSetterWrapperInline`（期望值以 `ts.transpileModule` 在 `{target:ES2015,module:ESNext,alwaysStrict:false,strictNullChecks:true}` 下实测）——原单测环不覆盖该构造，正是它漏掉这条。

**14 例崩溃中 11 例已修**（同族：参考用谓词/unchecked `as`，端口写成 C# 硬转）：
`Binder.DeclarationBinders.cs`（JSDoc 参数标签的 `name` 走 `JSDocPropertyLikeTag`/`ParameterDeclaration` 分支而非 `INamedDeclaration` 强转）、`Binder.Declarations.cs`（`(SourceFile)_container!` → `_container is SourceFile`，binder.ts:2443 是 unchecked `as`）、`Checker.Queries.cs`（`NodeTests.IsTypeAliasDeclaration` → `Utilities.IsTypeAlias`，checker.ts:13509 的谓词更宽）、`Checker.TypeSupport.cs`、`Checker.Check.Source.cs`、`Utilities/Utilities.Visitor.cs`（`VisitNode` 的"先检查后 pun"与 `AddDefaultValueAssignmentsIfNeeded` 的 `result.Add(updated)`）。剩余 3 例登记：`Checker/SyntacticBuilderResolver.cs:67-69`（JSDocParameterTag 臂的 4 成员联合，需加宽而非改转）、`jsdoc/callbackTagNamespace.ts`（能跑但 errors/types 有分歧）、`privateNamesUnique-2.ts`（module specifiers **具名阻断**）。

验收（集成方实测，冻结树）：build 0 错误 0 警告、测试 **1,521/1,521**、`tools/TypeDiff` 全语料 `same=5908/5908` clean、`tools/EmitDiff` 全语料 `same=5908/5908` clean；代理侧各 fixture 过滤器 clean（命令与数字见其报告）。

#### 17.14.9.1 `requiresAddingImplicitUndefined` 的多余硬转（JSDoc 参数标签）

`Checker/SyntacticBuilderResolver.cs` 的 `SyntaxKind.JSDocParameterTag` 臂把 `declaration` 硬转成 `ParameterDeclaration` 再交给 checker；参考（checker.ts:6331）**直接传联合**，而 checker 自己的 `requiresAddingImplicitUndefined` 参数本来就是 `ParameterDeclaration | JSDocParameterTag`（checker.ts:50852，端口成员早就取 `Node`），其内部 `IsRequiredInitializedParameter` 也已按 `IsJSDocParameterTag(parameter) ||` 先行短路。于是这处转换既多余又必崩（`JSDocPropertyLikeTag` 不是 `ParameterDeclaration`）：`jsdoc/callbackTagNestedParameter.ts`、`callbackTagVariadicType.ts`。删掉转换后 `**/callbackTag*` 过滤器（7 用例）`port-crash` 归零；仅剩 `jsdoc/callbackTagNamespace.ts` 的 errors/types 分歧（点号 `@callback NS.Nested.Inner` 的命名空间解析未落地，已登记）。

#### 17.14.9.2 600 文件分片复测（全部修复之后）

同一命令（`--limit 600 --node-parallel 8`）：

| artifact | 修复前 | 修复后 |
|---|---|---|
| `.js` | 798 / 7 / 14 | **818 / 0 / 1** |
| `.errors.txt` | 807 / 0 / 14 | **820 / 0 / 1** |
| `.types` | 765 / 4 / 14 | 775 / 4 / 1 |
| `.symbols` | 769 / 0 / 14 | **779 / 0 / 1** |
| `SUMMARY` | same=3139 div=11 crash=56 | **same=3192 div=4 crash=4** |

剩余 4 条分歧全是 `privateNameNestedClass{Accessors,Field,Method}Shadowing`.ts 与 `privateNamesInNestedClasses-1.ts 的 `.types` 下划线**中段缺口**（示例：`(x: Derived) => void` 里参数类型名 `Derived` 的 7 个字符没有下划线），仍属 `nodeIsFullySynthetic` 家族但**不是**工厂未标标志位：插桩显示打印到那里的 `Identifier("Derived")` 是 `flags=Synthesized pos=188`、`original` 指向**语法树节点**的那一个（`getParseTreeNode` 因此非空 ⇒ 非 fully synthetic），而 fully synthetic 的 `pos=-1` 同名节点在同一文件里也确实存在。即：节点构建器在"重用语法树节点 vs 重建合成节点"上的取舍与参考不同 —— 属节点构建器的重用面，需要单独切片，本条只记录证据（`artifacts/bd-wide2/port/privateNameNestedClassFieldShadowing.types` 第 43 行）。剩余 1 条崩溃是已登记的 module specifiers 具名阻断（`privateNamesUnique-2.ts`）。

### 17.14.10 裁定：`Utilities.VisitNodes`/`VisitArrayWorker` 的按元素检查式转换保持不动

`Utilities/Utilities.Visitor.cs:354/361` 的 `updated.Add((TNode)(object)visitedNode)` 与 `VisitNode<TNode>` 的 pun 是同一形状（参考在 `visitorPublic.ts:328-376` 里对"访问器答了更宽的元素类型"是**未检查**的），但两者性质不同：`VisitNode` 的 pun 有**实证**（某个真实用例的访问器确实答了更宽的节点，见 §17.14.9 的 slices 说明），而按元素转换至今**没有任何用例**触发。端口对 `Unsafe`/pun 的既有规则是"参考的未检查行为必须可观测才引入 pun"（§15.10 缺陷 7 的四个具名 punning 辅助都带这条论证），因此本条**保持现状**：不加 pun、不改参考语义、不写"可能更宽"的猜测式放宽。若将来某个用例证明参考会静默接受更宽元素，再按同一条规则引入具名 pun 并附读路径论证。

### 17.14.11 新测区域 `jsdoc/**`：JSDoc/JS 声明面是当前最大缺口（实测）

命令：`--filter "jsdoc/**" --node-parallel 8`（341 文件 / 397 用例；`port pass 325s`）：

| artifact | same | diverged | port-crash | unmodelled |
|---|---|---|---|---|
| `.js` | 235 | **45** | 116 | 1 |
| `.errors.txt` | 261 | 19 | 116 | 1 |
| `.types` | 243 | 36 | 116 | 1 |
| `.symbols` | 272 | 7 | 116 | 1 |
| `SUMMARY` | same=1011 | **diverged=107** | **port-crash=464（116 用例，29%）** | 4 |

崩溃按消息聚类（`artifacts/bd-jsdocarea/port-errors.txt`）：

| n | 形状 | 性质 |
|---|---|---|
| **74** | `NotSupportedException: GetSpecifierForModuleSymbol`（checker.ts:8638 / `moduleSpecifiers.ts`） | **单一缺失特性**：模块说明符解析（此前登记的"具名阻断"，实测占本区崩溃的 64%） |
| 11 | `JSDocEnumTag → …` | 引用谓词被写成 C# 硬转 |
| 10 | `FunctionDeclaration → …`（`jsDeclarations*`） | 同上 |
| 7 | `JSDocImportTag → …` | 同上 |
| 2+2+2+2 | `VariableDeclaration → …` / `PropertyAccessExpression → TypeS…` / `Failed to compare two elements in the array`（排序比较器在混合节点上抛）/ `JSDocParameterTag → …` | 同上 + 一处比较器 |

107 条分歧按首差片段聚类：JSDoc/JS 发射（`namespace St…`、`import type …`、注释保留）+ `.types` 下划线 + `errors`（如 `satisfies` 标签）。

**结论**：之前"剩余 5 项"的说法只覆盖了前 600 文件分片；真正的大缺口在 **JSDoc/JS 面**（本区 29% 用例仍失败），其中 **74/116 由模块说明符解析未移植一项造成**。两个切片已派出：`ModuleSpecifiersPort`（`moduleSpecifiers.ts` 1,470 行 + checker.ts:8638 调用点）与 `JSDocCastFamily`（≈35 例硬转 + 107 条分歧的分类）。

### 17.15 项目模式（单 program）计时：`tools/ProjectBench`，以及与 tsgo / JS 实现的对比

**动机**：三条差分环都是**每文件新建 program**（每用例重解析 lib），量的是 harness 模型而不是编译器；tsgo 与 JS 实现都是"一个 program 编整个项目"。为此新增 `tools/ProjectBench`（`--files <tsconfig.json>`，读真实文件系统、加载真实 lib、按阶段计时：createProgram=parse+bind+图 / syntactic / semantic / emit），使端口第一次有与 `tsc -p` 同形的数字。

**同一工作负载**（conformance 全语料 5,908 个根文件 + 93 个 lib 声明文件，`noEmit`、`skipLibCheck`、`target/module=esnext`、`allowJs`）：

| | tsgo 7.0.2（全局 `tsc`） | tsc.js 6.0.3 | tsc-cs（项目模式，单线程） |
|---|---|---|---|
| 总计 | **1.87 s**（`--extendedDiagnostics`） | **1.42 s**（同） | 单目录分片求和 **64.0 s**（扣掉 52 次重复 lib 载入 ≈23 s ⇒ **净 ~41 s**）；**整语料一次跑不完**（见下） |
| parse | 0.127 s | 0.54 s | 分片求和约 0.6–0.9 s/区（844 文件的 `types` 区 parse+bind 0.94 s） |
| bind/check | 其余 ~1.7 s（并行） | bind 0.32 s，check ≈0.78 s | `semantic` 是热点：`types` 区 **17.6 s / 21,584 条诊断**，`classes` 0.90 s / 3,942 条，`parser` 0.93 s / 2,894 条 |
| 内存 | 386 MB / 8.6M allocs | 210 MB | —（未测） |
| 每语料文件 | **0.25 ms** | **0.19 ms** | **7–11 ms**（净） |

⇒ **单核量级差距 ~5–10×（parse+bind），type-relation 密集处 ~50–100×（`types` 区 20.8 ms/文件 对 JS 实现全语料 check 0.78 s/5,908 ≈ 0.13 ms/文件）**；再加端口单线程，整项目差距约 **30–50×**。用户判断成立：差距不是"模型"能解释完的，**瓶颈在 checker（类型关系/实例化/链接查询），不在解析**。

**项目模式还暴露两个单文件模型看不见的健壮性缺陷**（都阻断"整语料一次编完"）：

1. **`ArgumentOutOfRangeException`**：前缀 2,862 文件正常，加入第 2,863 个（`functions/strictBindCallApply1.ts`）即抛（该文件单独跑正常 ⇒ 跨文件交互）。二分脚本：`artifacts/perf/`（`all.txt` + `shard.json`，`ProjectBench --files shard.json`）。
2. **`Stack overflow`**：5,908 文件全量时无限递归，栈为 `GetDeclaredTypeOfEnumMember → GetTypeOfEnumMember → GetTypeOfSymbol → GetNarrowedTypeOfSymbol → CheckIdentifier → CheckExpressionWorker → CheckExpression → CheckComputedPropertyName → …`（重复 247 层）—— 参考在同类输入下不递归（checker.ts:13589-13598 的 `getDeclaredTypeOfEnumMember` 靠 enum 侧 `links.declaredType` 先置位断链），端口缺同一道断链。

**性能工作的方向（依用户裁定：优先参考 Go 实现，其次 TS）**：①先修上述两处崩溃使整语料可跑；②用 profiler（`dotnet-trace`/采样）定位 `Checker.Relations.*` 的热点，对照 tsgo 的 checker 结构（符号链接用字段而非字典、类型 interning 用 id→数组、无闭包访问器、按文件并行）逐项对照；③并行化（tsgo 的主要优势之一，端口当前单线程）；④减少分配（`NodeArray`/闭包/字典）。

### 17.16 `moduleSpecifiers.ts` 落地：JSDoc 面最大的单一崩溃源清零（切片交付，集成方复核）

`src/TypeScript.Compiler/ModuleSpecifiers.cs` **2,570 行 / 67 处 `ported from:`**（`moduleSpecifiers.ts` 全量：缓存、`node_modules` 包名、`exports`/`imports`、`rootDirs`、`typesVersions`、结尾偏好、prefix/suffix、大小写与 realpath 等），并替换两处具名阻断：

1. `Checker/NodeBuilder.Names.cs` 的 `GetModuleSpecifiers` 局部桩 → 真正的 `ModuleSpecifiers.GetModuleSpecifiers(...)`（含参考里被丢掉的 `links.SpecifierCache` 回写）；
2. **同族的第二处**：`sortByBestName` 的 `countPathComponents` 局部桩 → `ModuleSpecifiers.CountPathComponents` —— 这正是此前"排序比较器在混合节点上抛 `Failed to compare two elements`"的来源（它其实是该阻断被包了一层）。

支撑改动：`Program/ModuleNameResolver.NodeModules.cs` 新增 `IModuleSpecifierResolutionHost` 版 `ForEachAncestorDirectoryStoppingAtGlobalCache`（原 `IModuleResolutionHost` 版改为转发），`Program/Utilities.ModuleResolution.cs` 新增三个 host 重载并抽出 `GetPathsBasePathWorker`（参考的 host 参数是 TS 联合/Pick，端口的两种宿主角色是各自独立的 nominal 接口）。

**实测（`tools/BaselineDiff`，`--filter "jsdoc/**"`，341 文件 / 397 用例）**：

| | 前 | 后 |
|---|---|---|
| 崩溃记录 / 崩溃用例 | 464 / 116 | **6 / 6**（`GetSpecifierForModuleSymbol` 74 例 → **0**） |
| 端口 vs 参考一致行（1,588 行） | 1,011 | **1,401** |

单例复核（各跑一次，`VERDICT clean exit=0`）：`callbackCrossModule.ts`、`privateNamesUnique-2.ts`、`node/nodeModules*Specifier*.ts`（16/16，含只有本切片能合成的 specifier 基线，如 `nodeModulesExportsSpecifierGenerationDirectory(module=node16)` 的 `import("inner/other.js").Thing`）、`moduleResolution/allowImportingTsExtension*.ts`。

**该区剩余**：JSDoc 硬转族 5 例（`NamespaceImport → …`、`JSDocImportTag → Import…`，由 `JSDocCastFamily` 切片处理中）、1 例 unmodelled（需要 tsconfig 读入，M8）、以及 121 个用例的发射/类型/JSDoc-注释类分歧。

集成方在同一棵树上复核（含各切片在飞改动）：build 0/0、测试 **1,521/1,521**、`tools/TypeDiff` 全语料 **clean 5908/5908**、`tools/EmitDiff` 全语料 **clean 5908/5908**（端口半场 23.2 s，此前 38–46 s —— 已有切片带来约 40% 提速，详见 §17.17 的性能诊断）。

### 17.17 三个切片的收尾：JSDoc 硬转族清零、项目模式两处崩溃、checker 首轮性能修复

**A. JSDoc/JS 声明面硬转族（`JSDocCastFamily`）** —— 11 处修复，全部是"参考用谓词/unchecked `as`，端口写成 C# 硬转"：

| 文件 | 修复 |
|---|---|
| `Checker.Check.Symbols.cs` | `CheckJSDocTypeAliasTag` 按三臂联合（`JSDocTypedefTag`/`JSDocCallbackTag`/`JSDocEnumTag`）走 kind 分支，替代硬转 `JSDocCallbackTag`（checker.ts:44223-44234 + 调用点 49163-49166） |
| `Utilities.Checker.cs` | `GetEffectiveImplementsTypeNodes` 拓宽到 `Node`（非 JS 走 `HeritageClausesOf`）；新增 `GetImportContainerModuleSpecifier`/`GetImportContainerAttributes`（`ImportDeclaration \| JSDocImportTag` 容器读取） |
| `Checker.BaseTypes.cs` / `NodeBuilder.Serialize.cs` / `Checker.SymbolSupport.cs` | `getImplementsTypes`、`SerializeAsClass` 的 implements 读取、`ImportClause`/`NamespaceImport`/`ImportSpecifier` 诸臂改按面读取（`NamespaceImport` 臂照参考恒为 undefined） |
| `SyntacticBuilderResolver.cs` / `Checker.Check.Statements.cs` / `Checker.CallResolution.cs` / `Checker.TypeSupport.cs` / `Checker.Inference.cs` / `Checker.Check.Classes.cs` | 五处联合成员的软转/越界语义（`NodeIsMissing(functionLike?.Body)`、`SignatureDeclaration \| JSDocSignature` 的参形读取、`pop()` 空表 no-op、`arguments[predicate.parameterIndex]` 越界答案等） |

实测（`--filter "jsdoc/**"`，341 文件/397 用例，同命令同基线）：**`port-crash` 464 → 0（116 用例 → 0）**；`same` 1,011 → **1,414**；`diverged` 107 → 166（107 条原样保留，**59 条是原先崩溃的用例现在可比后新露出的**）。该区现有分歧全部是发射/类型/JSDoc-注释类文本差异，无崩溃。

**B. 项目模式（`tools/ProjectBench`）两处跨文件崩溃（`ProjectModeCrashes`）** —— 都不是"缺守卫"，而是 JS 语义在 C# 里的等价物被写成了会抛的形式：

1. `Checker.Members.cs` 的 `GetExpandedParameters` → `associatedNames[i]`：参考读的是可能越界的数组元素，JS 答 `undefined`（falsy ⇒ 回退 `getParameterNameAtPosition`），C# 的 `List[i]` 抛 ⇒ 前缀 2,863 文件（`functions/strictBindCallApply1.ts` 与 `async/es6/asyncWithVarShadowing_es6.ts` 组成的最小对，后者 `fn19([x])` 造的 `[any]` 元组目标与 lib 的 `bind<...[...A, ...B]...>` 归一化后复用，标签表为空）**`ArgumentOutOfRangeException`**。
2. `Checker.Queries.cs` 的 `GetDeclaredTypeOfEnum` 成员遍历守卫：`HasBindableName(member)` → `!Utilities.HasDynamicName(member)` ⇒ 前缀 5,718 起 **Stack overflow**（`GetDeclaredTypeOfEnum → HasBindableName → HasLateBindableName → IsLateBindableName → CheckComputedPropertyName → … → GetDeclaredTypeOfEnumMember → GetDeclaredTypeOfEnum`，重复 247 层）。

**C. checker 首轮性能修复（`CheckerPerfDiagnosis`，按"优先参考 Go 实现"裁定）** —— `dotnet-trace` 采样（Speedscope 回放校验：`StartsWith` 34.05% / `EndsWith` 29.65% 独占，两条互证）：

| 修复 | 依据 | 实测 |
|---|---|---|
| `Checker.Relations.Identical.cs` 的 `InferFromLiteralPartsToTemplateLiteral`：`string.StartsWith/EndsWith(string)` → `Core.StartsWith/EndsWith`（序数比较） | **既是性能修复也是忠实性修复**：JS 的 `String.prototype.startsWith` 与 core.ts 的 helper 都是序数，tsgo 用 `strings.HasPrefix/HasSuffix`（relater.go:2426）；C# 的重载走 CompareInfo/ICU（可折叠软连字符等） | `types` 分片（845 根/938 文件）CPU **15.0–15.8 s → 6.5 s（-57%）**，semantic **14.6–17.0 s → 5.7 s（-61%）**，诊断数不变（21,584）；ICU 独占从 63.7% 降至 0.1%；负对照（1,200 文件前缀）不变 ⇒ 收益集中在模板字面量类型密集文件 |
| `Utilities.Parser.cs` 的 `FormatStringFromArgs`：`[GeneratedRegex]` 替换 → 单次左到右扫描（输出逐字节相同，含越界/无匹配臂，带 PORT-NOTE） | 降低每次诊断消息的分配（Regex 路径在 GC 样本中可见） | **A/B 未能分辨**（机器噪声 ±5%，效应 ~6% 在解析度边缘）——保留理由：每次消息的工作量严格更少且两条外环干净；属"形状偏离两个参考但可论证"的范畴 |

**收尾验证（集成方，冻结树）**：build 0 错误 0 警告、测试 **1,521/1,521**、`tools/TypeDiff` 全语料 **clean 5908/5908**、`tools/EmitDiff` 全语料 **clean 5908/5908**（端口半场 21.9 s，此前 38–46 s）。

**仍未闭合（已登记，未在本轮修）**：整语料单 program 跑通还差一串同族缺陷——`CreateMarkerType`（`Checker.Relations.Identical.cs:360-362`）对"无类型参数/联合类型声明"的泛型仍会抛（`[.. null]` 的 NRE，以及 `UnionType → IGenericType` 的硬转；参考的 `as`/`!` 在运行期都是"不检查"），且改成"传 null"会继续顶到 `GetTypeAliasInstantiation`（`Checker.TypeReferences.cs:352` 的 `GetValueOrDefault(null, …)`）。这一串要达到"整语料一次编完"需按 tsgo 的 `getVariances`/`createMarkerType` 形态逐处对照（属下一轮工作，不在本收尾内）。

---

## 18. 并发：数据结构向 Go 看齐（M8 前置，进行中）

### 18.1 依据与目标

`AGENTS.md`（本会话更新两条）：① **TSGO CLI 就是全局 `tsc`**（实测 `tsc --version` = **7.0.2**，`%APPDATA%\npm\tsc`）——用于同工作负载直接对拍，无需先构建 Go 端；② **.NET 支持多线程 ⇒ 必须考虑多线程与线程安全的数据结构来加速编译器，并争取超过 Go 端**。

实测基线（工作负载同 §17.15：conformance 5,908 根文件 + 93 lib，`noEmit`/`skipLibCheck`/`target=module=esnext`/`allowJs`）：

| | tsgo 7.0.2（`tsc --extendedDiagnostics`） | tsc.js 6.0.3（同） | tsc-cs（项目模式，单线程） |
|---|---|---|---|
| 总计 | **1.87 s** | **1.42 s** | 分片求和 64.0 s（扣重复 lib 载入净 ≈41 s） |
| 内存 | 386 MB | 210 MB | 未测 |

改造前端口**没有任何并行原语**：全程序集只有一个 `lock`（`Core/Debug.Model.cs:189`），`Interlocked`/`Task`/`Parallel`/`Concurrent*`/`Thread` 命中数 **0**。也就是说本节的每一项都是净新增，而不是把已有的并发改好。

### 18.2 审计（一）：Go 端怎么并行 —— 形态参考

来源 `D:/Source/GitHub/TypeScript-Go/tsc/internal/**`（AGENTS.md 指定的静态类型参考）：

| 机制 | Go 位置 | 形状 |
|---|---|---|
| 工作分组 | `core/workgroup.go:11-76` | `WorkGroup`；并行实现 **每工作项一个 goroutine（无界）**，单线程实现=互斥保护的闭包切片 |
| checker 池 | `compiler/checkerpool.go:26-34,308-320` | N 个 checker（**硬编码 4**）+ **每 checker 一把 `sync.Mutex`**；`--checkers`/`--singleThreaded` 可改 |
| 文件→checker | `checkerpool.go:417-419,480-486` | **静态分区**（FENNEL 均衡图分割，`checkerpool.go:164-229`）：一个文件终身归一个 checker ⇒ checker 内的 `map` 无需同步 |
| 链接存储 | `core/linkstore.go:7-88` | `LinkStore{entries map[K]*V; Arena[V]}`；**`PagedLinkStore{pageList []*[256]V; pageMap map[uint64]*[256]V}`** |
| id 分配 | `ast/utilities.go:16-40` | `atomic.Uint64` + `CompareAndSwap`（原注释："worst case, we burn a few ids"） |
| 诊断 | `ast/diagnostic.go:255-286` | 每 checker 一个按文件路径键的 collection，`Add` 加锁 |
| emit | `checker/emitresolver.go:49-69` 等约 20 处 | 每次 resolver 调用**重取** checker 的粗锁 |

**Go 端自己记录的短板**（= C# 可以超过它的地方）：checker 数硬编码 4（`checkerpool.go:308`）；静态分区导致长尾（`checkerpool.go:55-62` 自述 MUI docs 四个 checker 收工时间 **0.6 / 2.8 / 15.3 / 19.0 s**）；每个 checker 重复 merge 全部全局符号（`checker.go:1307-1396`）⇒ N× 重复工作；一把粗锁把 emit 也串起来（`emitresolver.go:49`）；无界 goroutine（`workgroup.go:34-41`，`4 builders × 4 checkers × files`）；无 shard 内并行。

### 18.3 审计（二）：端口的共享可变槽，以及本轮已落地的改法

并行阶段的前提是"任意两个 worker 不共享可变状态"。审计逐条列出端口里违反该前提的槽，本轮先消掉与"可重入"直接相关的部分（每条都在代码处留 PORT-NOTE；形状参考 Go）：

| 槽 | 改法 | 依据 / 证据 |
|---|---|---|
| 5 个 id 计数器（node/symbol/flow/merge/auto-generate） | `Interlocked.Increment`；**槽位写入改 `CompareExchange`**（`Node.Id`/`Symbol.Id`/`Symbol.MergeId`/`FlowNode.Id` 变为 `Volatile` 属性 + `internal ref int …Slot`） | `tsc/internal/ast/utilities.go:21-40`；`Node.Id` 由 `int?` 改成 `int`（`0` = 未分配；参考首个 id 是 1，utilities.ts:1364）|
| `Tracing.Current` | `AsyncLocal<TracingEnabled?>`；`StartTracing` **每次 trace 新建实例**；legend/`traceCount` 进程级并加锁 | §15.13 记录的"测试程序集必须串行"的直接成因 |
| 4 个 memoized `Printer`（`Emit/PrinterFactory.cs`） | `[ThreadStatic]`，每 worker 一份 | printer 是有状态对象（`writeNode`：存 writer→打印→`reset()`→还原）。并行首轮实测的直接证据：`TransformationUtilitiesTests` 打出 `"static {a \n    = x.a, x"`（期望 `"a = x.a, x"`），`ClassThisTests` 在 `Printer.WriteBase`（`Emit/Printer.cs:1575`）空引用——两个测试的打印互相踩 |
| `Utilities.s_stringWriter`、`NodeFactory.rawTextScanner` | `[ThreadStatic]` | 同上：各是一个可变的写缓冲/扫描游标 |
| `NodeFactory.Factory` 的 `_parenthesizer`/`_converters` | 构造期 eager（`readonly` 字段） | `??=` 写在共享实例上；惰性检查还在热路径 |
| `Performance` 的 `marks`/`counts`/`durations` | `ConcurrentDictionary` + `AddOrUpdate`（计数与时长是读-改-写） | 并行实测直接抛 `Dictionary` 撕裂（`Performance.Mark` 与 `ForEachMark` 两处栈） |
| `getNamedArgRegEx` 缓存、Unicode property values 缓存 | `ConcurrentDictionary` | 首次使用的写竞争 |
| Unicode 属性表 ×4、`nodeCoreModules`、`scriptTargetFeatures` | `Lazy<T>`（保留惰性：表大；但只发布一份） | 三个"单值惰性"站点原来是 `??=` |
| `Core.MemoizeOne`（`moduleSpecifiers` 的正则缓存是唯一使用者） | 内部换 `ConcurrentDictionary`，**键形状照参考不变** | core.ts:1907-1918 的 `${typeof arg}:${arg}` |
| UI comparer + locale（`Core.Comparison`） | 一把锁；比较器经 `Volatile` 发布 | `SetUILocale` 与比较并发 |
| `typeScriptVersion`（`ModuleNameResolver`） | eager（版本号是常量） | 惰性在这里只有代价 |
| `ParenthesizerRules` 的两个 operator→规则 memo | `ConcurrentDictionary` + `GetOrAdd`（且不再惰性建容器） | **由工具侧并行实测抓出**（见 18.5）：3 个文件在端口半场崩溃，栈在 `GetParenthesizeLeftSideOfBinaryForOperator`。`ParenthesizerRules` 自 18.3 起是每个 factory 一份、而 factory 是进程级单例，两份 memo 原本是普通 `Dictionary` |

**验收（本轮 oracle 就是测试程序集）**：删掉 `tests/…/AssemblyInfo.cs` 的 `DisableTestParallelization = true`（§15.13 的临时措施），让 xUnit 恢复并行 collection —— 于是"进程级可变槽"变成可观测项。收敛过程（每轮都是并行跑整个程序集 1,521 个用例）：

| 阶段 | 连续几跑的失败数 |
|---|---|
| 首轮 | `2 / 0 / 56`（Printer 单例：两个测试的打印互相踩） |
| 修 `PrinterFactory` 后 | `1 / 0 / 0 / 0`（`Performance.Mark` 的字典撕裂） |
| 修 `Performance` 后 | `0 / 0 / 0 / **36** / 0 / 0` ⇒ 其后 **20 跑连续全绿** |

**这说明并行测试本身就是并发审计器：改到位的槽立刻从 flake 变成绿灯。**

### 18.4 下一步：数据结构向 Go 看齐（按此顺序）

**状态（2026-09-27）**：第 1 步（`PagedLinkStore`）已落地（§18.4c，实测中性）；第 2 步的 `WorkGroup` 以 `Parallelism.For` 落地并接在 bind 上（§18.4b）；第 3 步的 checker 分片**实测否决并回退**（§18.4d）；第 4 步（诊断每文件缓冲）经复核**已无对象**——端口本来就是每文件收集 + 一次排序去重（§18.4e）；第 5、6 步的结论见 §18.4e。

1. **链接存储 → `PagedLinkStore`**。现状 `Checker.cs` 的 `_nodeLinks`/`_symbolLinks` 是 `List<T?>`，"id 超过表长就 `Add(null)` 补齐"（`Checker.cs:461-473,483-489`）——这是参考的稀疏数组在 C# 里最贵的等价物。Go 的 `PagedLinkStore`（`core/linkstore.go:44-88`）以 256 元素一页、页表按 id 定位：无补齐、无大列表重分配、**每分片各写各的**。这一步同时解决内存与并行两件事。
2. **`WorkGroup`**：端口版 = 有界 `Parallel.ForEach` + partitioner（Go 的"每项一 goroutine"不能照抄，.NET 会淹在调度里）。
3. **并行 parse/bind**（`Program.Host.cs:107` 的 `Parser.CreateSourceFile`、binder 的每文件入口互不相干）→ 并行 **emit**（`Program.Emit.cs:495` 每文件）→ 最后 **checker 分片**（`Checker.CheckSourceFile`，`Checker.Check.Source.cs:72`）。
4. **诊断**：现在是全局 `DiagnosticCollection`（`Utilities/Utilities.Diagnostics.cs:374-419`，读-改-写 + 插入排序）；改为每文件缓冲，在 `Program.GetDiagnosticsHelper`（`Program.DiagnosticsAggregation.cs:96-107`）串行合并——既去掉锁，也保住排序确定性（外环逐字节比对要求确定性）。
5. **内存与惰性**：`[ThreadStatic]` 的表是"每 worker 一份"（Unicode 表、printer），worker 数一上来必须复核驻留集；`Lazy<T>`/`??=` 的每次读也不是零成本——热路径上能 eager 就 eager（本轮已改 3 处），能共享就共享（并发容器取代每线程副本）。
6. **验收方式固定**：每一步都用 `tools/ProjectBench`（同工作负载）与全局 `tsc` 7.0.2 对拍，并记录峰值工作集；正确性仍由 §7 的两条外环（`TypeDiff`/`EmitDiff` 全语料 5,908 逐字节）与 1,521 个测试兜底。

### 18.4b 编译器侧已落地的第一个阶段：bind（含固定成本实测）

`Core/Parallelism.cs`（**非参考成员**，见其 PORT-NOTE）：`Degree` 从 `TSC_CS_PARALLEL` 读一次（默认 `ProcessorCount`，`1` = 全串行，Go 端同样用环境变量暴露它自己的开关 `TS_TEST_PROGRAM_SINGLE_THREADED`），`For(count, body)` 是有界 `Parallel.For`。第一个接上它的阶段是 checker 初始化里的绑定循环（`Checker/Checker.Initialize.cs:503-512`）——每个文件一个 `Binder` 实例、只写自己文件的 AST，紧随其后的 merge 循环保持串行（它写共享的 `_globals`/`_diagnostics`，且顺序有意义）。Go 的对应物是 `BindSourceFiles`（`program.go:578`）。

**验收**：`tools/TypeDiff --parallel 1`（外层串行、内层默认）跑全语料 5,908 文件 ⇒ `VERDICT clean same=5908/5908`，且 `port-hashes.txt` 与完全串行运行 **`cmp` 逐字节相同**；1,521 个测试并行 collection 连续 3 跑全绿。

**实测（`tools/ProjectBench`，`artifacts/perf/cpd-types.json` = 845 根/938 文件）**：`TSC_CS_PARALLEL=1` → semantic 4.377 s（21,584 条诊断）；默认 24 → 4.364 s（同 21,584）。**即 bind 在这份工作负载里不是瓶颈**（诊断数一致、时间无差），它的价值在于形状与"大项目/多文件时不再串行"。

**下一步的设计约束（同批实测）**——单文件 program（`artifacts/perf/one.json`）的 `semantic` = **0.331 s**，845 文件分片 = **4.348 s**，两者之差就是"检查 844 个文件"的成本 ≈ 4.0 s（≈4.7 ms/文件），而每个 program 的固定成本（lib 解析 + checker 初始化/全局合并）≈ **0.25–0.33 s**。检查器分片 K 份的墙钟 ≈ `固定成本 + 4.0/K`，所以分片**是**划算的（固定成本并行付出，只决定下界），这也是 Go 端"每 checker 重复 merge 全局符号"那条短板的量化形式：K 越大，重复的初始化越多，必须实测选 K（Go 硬编码 4）。

### 18.4c 链接表 → `PagedLinkStore`（Go 形状；实测中性，作为分片前置保留）

`Core/PagedLinkStore.cs`（`where T : class, new()`，256 元素一页，`id >> 8` 寻址；形状照 `tsc/internal/core/linkstore.go:38-88`）替换了 checker 的两张链接表 `_symbolLinks`/`_nodeLinks`——它们原来是"id 超过表长就 `Add(null)` 补齐"的 `List<T?>`（`Checker.cs:449-453`、访问器 `:462-492`、读取点 `Checker.Check.Flags.cs:174`）。语义逐条对齐：`GetOrAdd` = 参考的 `links[id] ??= new Links()`；`TryGet` = 读一个洞（**不分配 id、不建页**，正是 `getNodeCheckFlags` 要的 `node.id || 0` 然后越界读）；`id & 0xFF` 定页内偏移。

**实测 A/B（同一会话，845 根/938 文件，各跑两轮）**：

| | semantic | 峰值工作集 |
|---|---|---|
| `List<T?>` 补齐（改前） | 4.371 / 4.417 s | 341 MiB |
| `PagedLinkStore`（改后） | 4.337 / 4.372 s | 339 / 341 MiB |

⇒ **在这份负载上时间与内存都无实质差异**（诊断数 21,584 不变）。保留的理由不是"它更快"，而是两条结构性的：①它是分片的前置（每个 checker 一张表，页式表的增量成本才低到值得每分片一份——Go 为同一目的这么写）；②它去掉 `List` 的倍增拷贝（大 program 上一次扩容要复制整张表）。**没有实测收益的部分不写成收益。**

本步验收：1,521/1,521 测试；`tools/TypeDiff` 与 `tools/EmitDiff` 全语料各 `clean 5908/5908`，且两个工具的 `port-hashes.txt` 与改前基线 **`cmp` 逐字节相同**（11.64 s / 10.06 s 端口半场）。

### 18.4d checker 池：实测否决（已实现、已测量、已回退）

按 §18.4 的顺序做到第 1 步之后，自然的一步是照 Go 的 `checkerpool.go` 做 checker 池（K 个 checker、文件→checker 固定关联、每分片自己的链接表与诊断集合）。**已实现并测量**，结论是**回退**——不是"还没调好"，而是与移植的正确性判据（逐字节一致）不相容。记录如下，以免后人重复。

实现要点（当时的形态）：`Program.CheckerPool.cs`；K 个 checker（`CheckerCount` 由 `TSC_CS_CHECKERS` 或核数/文件数/上限决定）；文件按**连续分块**关联（Go 用加权图分割）；绑定从 checker 初始化里**上提到 `Program`**（K 个 checker 并发创建会让 binder 的 `if (!file.locals)` 变成 check-then-act 竞争，Go 也把 `BindSourceFiles` 放在池创建之前）；整程序扫描分两阶段——先按分片检查（一个分片只被一个 worker 触碰），再按分片组装；每文件答案 = 自己分片的条目 **∪ 其他分片的条目**（比 Go 更强：Go 只读 owning checker，会丢掉"检查 B 时在 A 上记下的诊断"）。

**验收手段先建**：`tools/ProjectBench --diagnostics <file>` 把项目模式的全部诊断按 `category / code / file / start / length / message`（消息用编译器自己的 `FlattenDiagnosticMessageText`，与 BaselineDiff 写 `.errors.txt` 同一条路径）落盘并排序。判据是**池大小不变性**：不同 `TSC_CS_CHECKERS` 的两次运行必须产出**逐字节相同**的 dump。三条既有外环都看不见这件事（它们的语料全是"一文件一 program"，池大小恒为 1）。

**实测（`artifacts/perf/cpd-types.json`，845 根/938 文件）**：

| K | semantic | 峰值工作集 | 诊断数 |
|---|---|---|---|
| 1（参考路径） | 4.32 s | 346 MiB | **21,584** |
| 2 | 5.12 s | 360 MiB | 21,602 |
| 4 | 4.62 s | 379 MiB | 21,602 |
| 8 | 4.89 s | 412 MiB | 21,603 |
| 16 | 5.59 s | 508 MiB | 21,603 |

1. **不快**：分片没有收益，反而更慢。原因是 §18.4b 量出来的固定成本——每个 checker 重新 merge 全局作用域、重建内建类型，0.25–0.33 s/个，K=4 就是约 1.2 s 的并行固定开销，而可并行的部分（4.0 s）除以 4 之后剩下的收益被吃掉了；内存则随 K 线性涨（346→508 MiB）。
2. **不忠实，且不止是诊断**：K>1 比 K=1 多 18–19 条诊断，全部是 `Duplicate identifier`/`Cannot redeclare block-scoped variable`（`lib.dom.d.ts` 的 `Node`、`lib.es2015.proxy.d.ts` 的 `Proxy`、`genericTypeReferenceWithoutTypeArgument.d.ts` 整组）——这正是"晚到的跨文件归属"：参考实现的单 checker 在组装文件 i 时只看得到"检查到 i 为止"的集合，晚到的贡献被丢弃；池化后它们全部出现。
3. **决定性的一条：类型文本本身变了**。同一份语料下 K>1 的 dump 里出现
   - `Variable 'c' must be of type 'any', but here has type 'number[] | string[]'` ↔ `'string[] | number[]'`
   - `not assignable to parameter of type '(string & {}) | "-moz-initial"'` ↔ `'"-moz-initial" | (string & {})'`
   - `{ a: 2; b: 1 | 4; }` ↔ `{ a: 2; b: 4 | 3; }`
   即**联合类型的成员顺序**变了。联合/属性顺序取决于**类型创建的全局先后**，而 TS 的 checker 恰恰是"一个全局、顺序敏感的类型库"；分片破坏的就是这个不变量。要修好只能让所有分片共享一个并发类型库并复现全局创建顺序——那等于放弃分片本身。

**结论**：回退整个池（`Program.CheckerPool.cs` 已删，`Program`/`Checker`/`DiagnosticCollection` 的调用点已还原）。保留三样副产品：`tools/ProjectBench --diagnostics`（项目模式诊断基线，M8 用得上）、§18.6 的测试集合、`Core/Debug.Model.cs` 的 `Lock` 改写。

**教训（写进纪律）**：并行化一个**顺序敏感的全局**（checker 的类型库、符号合并顺序）不是"加锁/分片"能解决的；Go 端选择分片，是接受它的代价（它自己的 checker 数硬编码 4，且文档记录长尾），而端口的判据是逐字节，代价不可接受。**先建 oracle 再动手**这一步救回了大量时间：`--diagnostics` 在回退前就给出了"类型文本都变了"的证据。

### 18.4e 各阶段占比：并行化的落点在哪

同一负载（845 根/938 文件，`--emit`，单线程参考路径）：`createProgram` 0.90 s（parse+bind+图）、`semantic` 4.18 s、`emit` 0.43 s、总计 5.5 s。

- **检查器占 76%**，但它的状态不可分片（18.4d）⇒ 只剩**单线程热路径优化**这一条路。已有先例且收益巨大：§17.17 的 ICU → 序数比较修复在 `types` 分片上把 semantic 从 15 s 压到 5.7 s（-57%）。**结论：编译器提速的主线是 profiler 驱动的单线程优化，不是并行。**
- **发射占 8%**：并行发射的上限约 0.35 s（844 文件），而代价是它与 checker 共享状态（Go 的做法是每次 resolver 调用重取粗锁，`emitresolver.go:49`）。按现测**不值**，登记为"等出现发射占比高的大项目再评估"。
- **诊断聚合本来就已经是"每文件收集 + 一次排序去重"**（`GetDiagnosticsHelper` → `_files.SelectMany(getDiagnostics)` → `SortAndDeduplicateDiagnostics`），§18.4 里设想的"每文件缓冲 + 串行合并"在池回退后没有对象——它已经就是那个形状。
- **绑定**（已在 `929f3b9` 并行）：0.9 s 里的 bind 部分，实测对总时长无可测影响（§18.4b），价值在形状与大项目。

### 18.4f 整语料单 program：`createMarkerType` 串的实测与残留

§17.17 登记的"整语料一次编不完"在 `CreateMarkerType`（`Checker.Relations.Identical.cs`）上复现。定位用的是**前缀二分**（`artifacts/perf/all.txt` 是语料顺序表，按前 N 个文件生成 tsconfig，逐次二分）——决定性证据是"第一次崩溃的前缀 = 5,069 个文件"，触发文件 `types/conditional/inferTypes1.ts`（它单独跑不崩：崩溃依赖前面 5,068 个文件建立的全局作用域）。

**已修（忠实性修复，非加守卫）**：`CreateMarkerType` 把两个 `typeParameters` 槽用 `[.. x!]` 展开，等价于"对 null 做集合展开"⇒ NRE。参考实现是把槽原样交给 `instantiateTypes`，而它的可选重载对 `undefined` 回答 `undefined`，所以两个槽都可能是 `undefined` 且一路传下去（`getTypeAliasInstantiation`/`createTypeReference` 都收受可选实参）。修法：新增 `InstantiateTypeParameters(List<ITypeParameter>?, mapper)`（`List<T>` 是不变的，所以先加宽到 `IType` 再实例化——顺序与参考一致；`InstantiateList<ITypeParameter>` 会把映射结果硬转回去，不成立），null 原样传下去。实测：该 NRE 消失（崩溃点按参考的语义**下移一层**），`types` 分片仍 21,584 诊断 / 4.37 s，1,521 测试与两条外环不变。

**下一个崩溃（未修，已定位到根因）**：`GetTypeAliasInstantiation` 的 `links.Instantiations!.GetValueOrDefault(id)`（`Checker.TypeReferences.cs:352`）收到 null。

按"**先问参考**"的顺序取证（每一步都留了可复现命令）：

1. **参考实现能编完这个输入**：`node D:/Source/GitHub/TypeScript/lib/tsc.js -p <前缀>` 正常结束（输出上万条全局冲突错误，但不崩）⇒ 这是端口的偏离，不是输入的必然。
2. **插桩读端口的状态**（`CreateMarkerType` 探针）：触发符号 `C` = `BlockScopedVariable|TypeAlias|Transient|Assignment|Module|ExportSupportsDefaultModifier`，**226 个声明**（语料里 226 个不相关文件各自声明 `class C`/`interface C`/`type C`，作为全局脚本合并成一个符号），而它的声明类型是 `InstantiationExpressionType` 且 7 个类型参数。
3. **向参考要同一符号的答案**（`node artifacts/probe/ts-symbol.cjs`）：参考的 `C` 有 **11 个 flag**（多出 `Function|Class|Interface|ValueModule|NamespaceModule`）。差别关键：`getDeclaredTypeOfSymbol` 先看 `Class|Interface`，参考据此走**类/接口**分支（声明类型没有类型参数 ⇒ `getVariances` 的循环为空 ⇒ 根本不创建 marker 类型）；端口只看到 `TypeAlias`，走**别名**分支（声明类型是实例化表达式 ⇒ 创建 marker 类型 ⇒ `links.Instantiations` 为 null ⇒ 崩）。
4. **那是不是合并丢了 flag？** 用同一套"端口 dump vs 参考 dump"对比 `_globals` 的全部合并符号：**2,110 / 2,112 个名称的 flag 完全一致**，只有两个不同——`F` 少 128（`ConstEnum`）、`fs` 少 1024（`NamespaceModule`）。`C` 的全局条目**与参考一致** ⇒ 合并本身基本忠实，崩溃处的 `symbol` 是**合并过程中的中间 clone（`Transient`）**，它带着当时的 flag 快照，而参考在那个位置解析到的是最终的合并符号。⇒ 真正的残留是"类型上的 `symbol` 槽解析到中间 clone 而非最终合并符号"（`getMergedSymbol`/`CloneSymbol` 的解析面），只在"上千个相互冲突的全局脚本同处一个 program"时显形。

**登记（按优先级）**：

1. **跨文件合并的 oracle 缺失**：三条差分环的语料全是"一文件一 program"，`BindDiff` 的 dump 对 key 排序，所以**没有任何环能看见跨文件合并**——本次的 `F`/`fs` 两个 flag 差异就是这样漏掉的。应该把上面第 4 步的工具化（端口 `_globals` vs `lib/typescript.js` 的 `getSymbolsInScope`，按名称比 flag）做成常驻检查（候选：`tools/ProjectBench --merged-symbols`）。重建探针的配方写在本节的步骤与 `artifacts/probe/*.cjs`（gitignored，可重建）。
2. **`F`/`fs` 的 flag 差异**（各差一个 bit）：合并路径上的具体缺陷，值得单独修（一旦 oracle 就位，这类问题会一网打尽）。
3. **整语料单 program 跑通**：在 1/2 落地后再谈；它的价值只是"与 tsgo/tsc 的端到端数字对拍"，而**支持的项目模式测量是逐目录分片**（§17.15 的数字就是那样测的），分片本身是彼此不冲突的连贯 program。

### 18.4g 单线程热路径：采样证据、一处实测收益、以及测量纪律

**工作负载的选择**：`types` 分片（845 文件）是模板字面量密集的**类别**分片，不代表全语料。所以先对**混合前缀**（`artifacts/perf/all.txt` 前 2,000 个文件，覆盖多个目录）采样：`TSC_CS_PARALLEL=1 dotnet-trace collect --profile cpu-sampling` → `dotnet-trace report <trace> topN`。单线程是必须的（并行默认核数时线程池的空等在 topN 里占 90%，把结论稀释掉）。

**实测（独占时间占比）**：

| 混合前缀 2,000 文件 | | `types` 分片 845 文件 | |
|---|---|---|---|
| `Thread.<PollGC>`（**GC**） | **44.2%** | `InferFromLiteralPartsToTemplateLiteral` | 11.2%（含 15.6%）|
| 文件 I/O（`CreateFile*`/`ReadFile`/`GetFileAttributesEx`/`CloseHandle`） | ≈4.6% | `InferTypesFromTemplateLiteralType` | 9.5% |
| `IcuCaseFolding.Convert` | 1.7% | `IsTypeMatchedByTemplateLiteralType` | 6.1% |
| `InitHelpers.CallClassConstructor` | 1.7% | `Core.Some` | 3.1%（含 41%）|
| `RuntimeHelpers.GetHashCode` | 0.9% | `Thread.<PollGC>` | 32% |
| `Array.Copy`（含 23.5%）/`StringBuilder.ExpandByABlock` | 0.3% / 0.5% | `Core.StartsWith` | 1.9% |

两条结论：①**最大的单项是 GC（32–44%），即分配**，而不是某个热函数；②`types` 分片的模板字面量族是**分片特有**的，全语料优化应先看混合前缀。⇒ 单线程热路径的主线是**减少分配**（§17.15 的方向④），需要 allocation-site 数据（采样剖析只给 GC 占比，不给分配点）。

**本轮落地的一处（有实测收益）**：`IcuCaseFolding.Convert` 的 **ASCII 快路径**（`Core/IcuCaseFolding.cs`）。ICU 的 root locale 完整映射与 .NET 的 invariant 简单映射在**纯 ASCII** 上可证等价（ASCII 没有展开映射、也没有 locale 相关的字母对），而调用方（大小写不敏感比较、路径/说明符比较、标识符）绝大多数是 ASCII，于是这类输入一次扫描即可回答，不再进 ICU（省两次 P/Invoke + `char[]` + 一次 string）。ASCII 之外仍走完整路径。

**测量纪律（本节最重要的教训）**：这台机器上**顺序 A/B 完全不可用**——同一个二进制在同一会话里可以从 4.18 s 漂到 6.30 s（约 ±20%，且呈单调漂移），我因此先得出过"ICP 前后无差异"的误判。改法是**成对交错**：把两个版本各构一份到独立目录，A/B/A/B… 逐对跑，比较成对差值。改用该协议后：

| 版本 | semantic（`types` 分片，3 对交错的中位数） |
|---|---|
| 改前 | 4.437 s |
| ASCII 快路径 | **4.233 s（-4.6%，3/3 对更好）** |

**试过并否决（有数字）**：把 ICU 的目标缓冲改成**非托管内存**（`NativeMemory.Alloc`）并把"测量 + 转换"两次调用压成常态一次（估容量 3n+1，溢出再重试）——4 对交错的中位数 **4.195 s vs 4.168 s**，没有收益、4 对里 3 对更差。原因：该路径只在**非 ASCII** 输入时进入（ASCII 已被上面的快路径接管），而对小的短命缓冲，运行时的 bump 分配 + gen0 回收比 `malloc`/`free` 更便宜；非托管内存要划算，前提是**跨调用复用**缓冲（线程本地暂存），而同一份测量说明这条冷路径还不值得那份复杂度（若将来非 ASCII 输入变成热路径，这条就是现成的结论与方向）。

### 18.4h 分配的来源：先有噪声无关的度量，再逐项去掉

**度量**：`tools/ProjectBench` 现在同时报**每阶段分配的字节数**（`GC.GetTotalAllocatedBytes(precise: true)` 的阶段差值），与墙钟并列。这是本机唯一可靠的 A/B 信号：墙钟在两个版本之间可以漂 ±20%（§18.4g），而同一二进制两次运行的分配字节数**逐字节相同**。混合前缀 2,000 文件的基线：

| 阶段 | 墙钟 | 分配 |
|---|---|---|
| createProgram（parse + 绑定 + 图） | 0.90 s | **277.5 MiB** |
| semantic（含惰性绑定 + 检查） | 1.56 s | **330.8 MiB** |
| 合计 | 2.46 s | **608.3 MiB**（≈304 KiB/文件）|

**结构性来源 1（已修，有实测）**：C# 里**对一个方法组或捕获 lambda 求值就会分配**一个委托/闭包，而 TS 里把模块级函数当回调传是零分配。binder 的每一节点都要走下钻：

```csharp
// 改前：每个节点两次分配（`Bind` 的方法组 + 捕获 this 的 lambda）
BindEachChild(node) => ForEachChild(node, Bind, nodes => BindEach(nodes));
```

改成在 Binder 实例上缓存两个委托（每文件一次，而不是每节点一次）：**semantic 330.8 → 317.7 MiB（−13.1 MiB）**，`createProgram` 不变（绑定发生在 checker 初始化，属 semantic 阶段，所以这 13 MiB 记在 semantic 上——这也说明"分配记在哪个阶段"要按实际调用点读，不能按函数名猜）。验收：1,521 测试、TypeDiff/EmitDiff 全语料各 clean 5908/5908。

**结构性来源 2（已测量，判定为不重要，已回退）**：`Parser.LookAhead<T>(Func<T>)`（`Parser.cs:576` → `SpeculationHelper`）确有 34 处调用点传方法组，且每个列表元素/表达式都会求值，看起来与 binder 同族。**实测把它否掉了**：把其中最热的 8 个谓词（`IsTypeMemberStart`、`IsClassMemberStart`、`NextTokenIsStartOfExpression`、`NextTokenIsOpenParenOrLessThanOrDot`、`NextTokenIsIdentifierOrKeywordOnSameLine`、`NextTokenIsBindingIdentifierOrStartOfDestructuring`、`NextTokenIsEqualsOrSemicolonOrColonToken`）缓存成每实例委托后，解析阶段 **277.5 → 277.3 MiB（−0.2 MiB，占 0.03%）**——即这些站点的调用量很小，与 binder 的"每节点"完全不是一个量级。改动的 8 个字段为此保留不划算，**已回退**（源码回到 277.5 / 317.7 / 595.2 MiB）。

这条的教训与上一条并列写在纪律里：**"同族的分配模式"不等于"同量级的分配量"，每一处都要用字节数验一次**。剩下 595 MiB 的主体（解析 277.5 + 检查 317.7）是**节点对象 / `NodeArray` 包装 / `List` 增长 / 字符串**，要分开需要 allocation-site 数据；本机现有工具给不出（`dotnet-trace` 的 speedscope 转换只留 CPU 采样帧，不带分配事件负载；PerfView / dotnet-gcdump 未安装）。在拿到该数据前只做"能由代码结构直接判定、且字节数能印证"的那几类。

**已排除的候选（读代码即可判定，不必再查）**：checker 的 `CheckSourceElementWorker` 是**按 kind 显式下降**的（每个分支直接调该 kind 的 checker），没有"每个节点一次 `ForEachChild(node, CheckSourceElement)`"那种通用下钻——所以 binder 的 `BindEachChild` 是**唯一**的"每节点回调"站点，语义阶段的余下分配不在这一族里。

#### 附：runtime async 的三次更正与最终结论（含被人指出的对照组缺陷）

**先更正我自己前面三处错误结论**（都被实测推翻，前一版文字已由本节替代）：

1. "这台工具链上 runtime async 打不开"——**错**：`<Features>runtime-async=on</Features>` 写进 **csproj** 即可启用（`net11.0` + `LangVersion=preview`）。判据是 IL：启用后 `AsyncStateMachineAttribute` 消失。先用 `-p:Features=…` 之所以"毫无变化"，是 **MSBuild 增量编译只看输入/输出文件、不看属性变化**——那次根本没重编译。
2. "async 只是多一层结构成本"——**过头**（见下表的 A/B 行）。
3. **最关键的一处**："`Task<int>` 那 72 B/次是 Task 对象本身、去不掉；即便零分配，async 调用协议也比委托贵 2.5–4 倍"——**错在对照组**：我那支探针的所有被测形状都是用**非 async 消费者**（`GetAwaiter().GetResult()`）驱动的，而**非 async 消费者会强制把 `Task` 实体化**。换成 async 消费者（两端都在运行时协议上）后，结论完全反过来。

**对照实验（我的探针，同一被调方、只改消费者；20M 次；每次 `-t:Rebuild`）**：

| 形状 | `runtime-async` 关 | 开 |
|---|---|---|
| A 直接调用 | 36.9 ms / 0 B（1.85 ns） | 37.0 ms / 0 B |
| B 缓存委托（端口现状） | 55.0 ms / 0 B（2.75 ns） | 57.0 ms / 0 B |
| E2b 同一被调方 + **非 async 消费者** | 252.9 ms / **72 B/次** | 239.6 ms / **72 B/次** |
| E2b 同一被调方 + **async 消费者**（async→async） | 208.7 ms / **72 B/次** | **7.2 ms / 0 B（0.36 ns/次）** |

**旁证：`hez2010` 的基准**（`artifacts/asyncbench/`，原样下载运行；100M 次；两列同为 `-t:Rebuild`）

| 用例 | 关 | 开 |
|---|---|---|
| 同步基线 | 0.41 ns / 0 B | 0.38 ns / 0 B |
| **async 方法、无挂起** | 6.83 ns / **72 B** | **0.41 ns / 0 B** |
| await 已完成 Task | 3.95 ns / 72 B | 0.35 ns / 0 B |
| await 已完成 ValueTask | 0.82 ns / 0 B | 0.35 ns / 0 B |
| `Task.Yield` 挂起 | 29.58 ns / 0 B | 34.91 ns / 0 B |
| ThreadPool 延续 | 92.38 ns / 152 B | 105.60 ns / 152 B |
| TaskCompletionSource 延续 | 93.10 ns / 160 B | 114.11 ns / 160 B |
| async 状态机链（含一次真挂起） | 285.40 ns / **324 B** | 84.97 ns / **168 B** |

**由此得到的正确结论**：

- **runtime async 打开后，两端都是 async 的"同步完成"调用链是 0 分配、且与同步调用同速**（我的探针 0.36 ns/次 vs 直接调用 1.85 ns/次；他的基准 0.41 vs 0.38 ns）。`Task` 并未"实体化"——运行时的协议让同步完成的返回值直接穿过，不必造 Task。**这是我先前判错的那一点。**
- 非 async 消费者（`GetAwaiter().GetResult()`，或任何只认 `Task` 的 API）**会**强制实体化：两列都是 72 B/次。所以"是否零分配"取决于**两端是否都在运行时协议上**，与返回值大小无关（我先前"≤8 就走缓存"的结论只解释了 *非 async 消费者* 的那一列的假象：`Task.FromResult` 对 ≤8 有缓存）。
- 真挂起仍是唯一有代价的路径（`Task.Yield` 29.6→34.9 ns；本 RC 下挂起分支的分配反而更多）。
- **因此"把回调改成 async"的性能理由不复存在**：改与不改的差别不在每次调用的成本，而在**结构**——`async` 会顺递归下降的调用图病毒式上溯到 parser/checker 每层签名、把这些 API 的调用点全部变成 `await`，而宿主（`ISystem`/`ICompilerHost`）是照 `sys.ts`/`program.ts` 的**同步**形状定的（改 async 等于偏离参考契约，M8 的每个调用点都要跟着改）；且这些站点**现在已经是 0 B / 2.7 ns**，没有可换取的收益。**结论：不改；但如果将来这些路径真出现挂起点（M8 的 I/O / watch），runtime async 是零成本的选择——本仓已打开该开关。**
- **本仓已启用**：`Directory.Build.props` 的 `<Features>$(Features);runtime-async=on</Features>`（核实过 `src/tsc` 的 csc 命令行出现 `/features:runtime-async=on`；`src`/`tools` 当前没有 C# async 代码，故今天不改变任何代码生成）。若某构造被拒（`ERR_UnsupportedFeatureInRuntimeAsync`），用 `[RuntimeAsyncMethodGeneration(false)]` 单独退出。
- **方法论教训（写进纪律）**：perf 对照实验要**逐个变量受控**——同一被调方、只改消费者；这次我一次改了三样（消费者类型、返回类型、是否异步），得出的是错的因果。

### 18.4i 分配的定位与修复：先给候选函数装探针，再动手

**方法**：给候选函数加"**子树分配字节 + 调用次数**"探针（`GC.GetAllocatedBytesForCurrentThread()` 的进入/退出差值，`TSC_CS_PROBE=1` 时才启用），先量后改。三个候选被量掉、一个命中：

| 候选 | 实测 | 结论 |
|---|---|---|
| void-walk 回调族（binder 的每节点下钻等） | **139,251 次调用 / 167,857 次访问**（types 分片） | 去间接化上限 = 访问数 × 3.3 ns ≈ **0.6 ms（0.02%）** ⇒ **不做**（原计划的结构体访问者/生成器重写代价远大于此） |
| `GetTypeListId` 的字符串键（21 处缓存键） | 53,511 次调用 / 359,381 字符 ≈ **0.4 MB** | 否（不是分配来源）。顺带记下：Go 端把它换成了整数 hash，若将来此路径变热可照做 |
| `Core.Some/Core.Map` 的捕获闭包 | 命中（见下） | 修 |
| **`InferTypesFromTemplateLiteralType`** | **27,786,283 次调用 × 128 B/次 = 3,392 MiB** | **命中：占 types 分片 semantic 分配（4,857 MiB）的 70%** |

**三处修复**（都是"参考的写法在 C# 里每次分配"，输出不变）：

1. **字符串字面量分支**：参考写 `inferFromLiteralPartsToTemplateLiteral([value], [], target)` —— 两个一次性 List（各 ~32 B，共 64 B/次）。改为单部件特化 `InferFromSingleLiteralPart`：把参考自己那条"单部件"路径的列表内联掉，guard 失败路径（27.8M 次里的绝大多数）**0 分配**。
2. **模板字面量分支**：参考的 `map(source.types, (s, i) => …)` 回调捕获 `target` ⇒ 每次一个闭包。改为循环 `InferFromMatchingTemplateParts`。
3. **联合过滤**：`some(templates, template => isTypeMatchedByTemplateLiteralOrStringMapping(t, template))` 在"每个字符串字面量 × 每个模板"上分配一个闭包。改为循环 `MatchesAnyTemplate`。

**等价性这样验的（重要）**：特化上线前先写了一个**双跑对比探针**——在同一个调用点上同时跑"特化版"和"通用版"，逐次比较返回值；它当场抓到一处真实差异：参考的 `addMatch` 会同时推进 source **position**（`sourcePosition = p`），我在特化里只推进了 index，于是相邻空分隔符（`` `${A}${B}${C}` `` 这种没有字面分隔符的模板）会产生**两个假诊断**（`'1.1.1'` 被判为不可赋给 `` `${number}.${number}.${number}` ``）。补上 `pos = p` 后双跑零差异，types 分片诊断数回到 21,584。**这条写进纪律：特化/改写"逐字对应参考"的函数，先做双跑对比，再谈性能。**

**实测（Release，探针已移除）**：

| | types 分片（845 文件） | 混合前缀 p2000（2,000 文件） |
|---|---|---|
| semantic 分配 | 4,857 → **1,461 MiB（−70%）** | 326.7 → 322.9 MiB（−1%） |
| semantic 时间 | 3.29–3.36 → **2.90–2.97 s（−11%）** | 1.405 s（≈持平） |
| 诊断数 | 21,584（不变） | 12,928（不变） |
| 峰值工作集 | 343 MiB（不变） | — |

混合前缀几乎不变正合预期：该路径只对**模板字面量密集**的负载热（p2000 上同类调用只有 33 次）。验收：1,521 测试、`tools/TypeDiff` 与 `tools/EmitDiff` 全语料各 clean 5908/5908。

**结论**：用户先前提议的"去间接化"被实测否掉（上限 0.02%），而同一批测量把真正的钱指了出来——**"每次调用的分配"（模板字面量类型推断族）**，改三处即拿到 −70% 分配 / −11% 时间。方法上的收获：**给候选函数装"字节 + 次数"探针**是这台机器上唯一可靠且便宜的定位手段（CPU 采样给不出分配点，而分配字节是逐位确定的）。

### 18.4j 分配画像（逐函数探针）与两处 `OverflowException` 崩溃修复

#### 探针方法

给候选函数包一层"进入/退出 `GC.GetAllocatedBytesForCurrentThread()` 差值 + 调用计数"，`TSC_CS_PROBE=1` 时启用，`ProjectBench` 结束时打印（**子树**口径：函数自身加上其间被调用的所有东西）。探针是临时的，量完即摘；下列数字都是 Release、混合前缀 `artifacts/bisect/p2000.json`（2,000 文件 + 93 个 lib/d.ts，共 2,093 次解析）。

| 段 | 字节 | 调用数 | 每次 |
|---|---|---|---|
| `createProgram` | 277.5 MiB (0.82–0.90 s) | — | 109–133 KB/文件 |
| ├ parse（`CreateSourceFile`） | **218.5 MiB** | 2,093 | 109 KB |
| │ ├ **scanner（`Scan`）** | **71.9 MiB** | 382,229 | **197 B/token** |
| │ ├ 节点对象（`CreateBaseNode`） | 30.2 MiB | 270,148 | 117 B |
| │ ├ JSDoc 子树（`ParseJSDocComment`） | 21.6 MiB | 7,675 | 2.9 KB |
| │ ├ 数组对象（`CreateNodeArray`） | 5.3 MiB | 124,640 | 44 B |
| │ └ 其余（解析器自有：列表/闭包/诊断/字符串） | ~89 MiB | — | — |
| ├ bind（`BindSourceFile`） | 50.2 MiB | 2,093 | 25 KB |
| └ 文件图（读文件/路径） | 8.9 MiB | — | ≈ 源文本本身 |
| semantic | 322.8 MiB (1.41 s) | — | — |

按文件看（同一次运行，`PROBEFILE` 口径）：

| 文件 | 解析分配 | 磁盘大小 | 比值 |
|---|---|---|---|
| `lib.dom.d.ts` | **67.7 MiB** | ~1 MB | ~34 B/源字节 |
| `binaryIntegerLiteral.ts` | **31.2 MiB** | **9,077 B** | **≈ 3,600 B/源字节** |
| `binaryIntegerLiteralES6.ts` | 31.2 MiB | 9,071 B | 同上 |
| `lib.es5.d.ts` | 7.5 MiB | ~250 KB | ~30 B/源字节 |

⇒ 两个 9 KB 的"数字字面量酷刑"测试文件吃掉整个解析分配的 **28%**，`lib.dom.d.ts` 一个文件吃掉 **31%**。这两个方向（大 lib 的单线程解析、数字字面量路径的每次字面量开销）比"节点对象"这条线大一个数量级——探针把注意力从猜想的节点/数组搬到了这两处。

**结论（用于排序后续优化）**：① 解析侧 218.5 MiB 里最大的一块是 **scanner 71.9 MiB / 197 B 每 token**（其中 `Js.NumberToString` 只占 0.3 MiB ⇒ 不是数字转换，是 token 文本/转义/模板路径）；② **lib 与大文件是单价最高的输入**，且 `createProgram` 单线程 + 5.4% CPU 花在文件 I/O 上 ⇒ 根文件"读+解析"是唯一还剩下的大结构杠杆；③ 检查侧 322.8 MiB 尚未归因。

#### 二次型字符串拼接：`ScanBinaryOrOctalDigits`（`Scan` 71.9 → 8.3 MiB）

接着把 `Scan` 的 71.9 MiB 拆子项，结果是**反直觉的**：所有 token 文本路径加起来只有 6.3 MiB——标识符/关键字文本 5.8（154,867 次 × 39 B）、字符串 0.2、数字 0.1、模板 0.1、正则/JSX/转义/注释指令各 0.0。于是给探针加了**每次调用分配量的分桶直方图**，真相立刻出来：

```
PROBEBUCKET scanner (Scan) 0B=213,685  <64=138,906/4.8MiB  <256=26,380/2.4MiB
                           <1K=3,222/1.0MiB  <8K=19/0.0MiB  >=8K=17/63.8MiB
```

**382,229 次调用里 17 次**（0.0045%）带走了 **63.8 MiB（89%）**——不是"每个 token 摊多少"，而是"少数调用各分配约 3.75 MB"。

根因是 **JS↔C# 的字符串拼接语义差异**：参考写的是

```ts
        while (true) { ...; value += text[pos]; pos++; }        // scanner.ts scanBinaryOrOctalDigits
```

JS 的字符串是 rope/cons-string，逐字符 `+=` 是摊销 O(1)；C# 的 `string += char` **每次都整体复制** ⇒ O(n²)。语料里的 `binaryIntegerLiteral.ts`（磁盘 9,077 B）含 ~1,900 位的二进制字面量：逐字符累积 = 1,900 次复制 × 平均 ~950 字符 ≈ **3.6 MB / 个**，与 17 次 × 3.75 MiB 精确吻合。修法是同一串数字、按游程 append 到 `StringBuilder`（无分隔符时直接切片源文本，零分配）：

```csharp
        var start = _pos;
        StringBuilder? value = null;            // 只在遇到 '_' 时创建
        ... 遇到 '_': value ??= new StringBuilder(); value.Append(_text, start, _pos - start); _pos++; start = _pos;
        ... 结束:   if (value is null) return _text[start.._pos];
                    value.Append(_text, start, _pos - start); return value.ToString();
```

**输出逐字符不变**（同一序列的数字）。同一轮把 `ScanNumberFragment` 的同类写法（每个分隔符游程一次 `result += _text[start.._pos]`，在游程数上二次）也改成同样的累积器——p2000 没有这种字面量（0.1 MiB），但全语料里有 `1_2_3_…` 这类超长带分隔符字面量，属于同一风险面。

**实测（Release，探针已移除）**：

| | 改前 | 改后 |
|---|---|---|
| `Scan` 子树分配 | 71.9 MiB（382,229 次，平均 197 B/次） | **8.3 MiB（平均 22 B/次）** |
| `createProgram` 分配 | 277.4 MiB | **213.6 MiB（−23%）** |
| 整轮分配（p2000） | 600.4 MiB | **536.5 MiB（−11%）** |
| 语义分配 / 诊断数 | 322.8 MiB / 12,928 | 322.9 MiB / 12,928（不变） |
| `createProgram` 时间 | 0.79–0.90 s | 0.78–0.85 s（噪声内，不计） |

验收：1,521 测试、`TypeDiff` 与 `EmitDiff` 全语料各 clean 5908/5908（EmitDiff 特别关键——数字字面量的 `Text` 会原样发射）。

**两条纪律**：① **JS 里便宜的 `+=` 在 C# 里是 O(n²)**：凡移植"逐字符/逐片段拼字符串"的代码，一律换成缓冲区，否则只在超长输入上爆炸；② **按 B/次看平均会漏掉这类问题**（197 B/次看似"每 token 都有开销"），必须看**每次调用分配量的分布**——17 次占 89% 只有直方图能看出来。这条线到此为止：扫描器剩下的 8.3 MiB 里 5.8 是标识符文本（AST 必须物化），其余是必要开销，**扫描器不再是杠杆**；下一步是结构性项（根文件并行读+解析）与检查侧归因。

#### 检查侧的分配画像（已收敛到调用解析簇）

同一套探针切 semantic 阶段的 322.9 MiB（p2000）：

| 段 | 字节 | 调用数 | 每次 |
|---|---|---|---|
| 每文件检查（`CheckSourceFileWithEagerDiagnostics`） | **243.0 MiB** | 1,997 | **127 KB/文件** |
| 每节点检查（`CheckSourceElement`，含嵌套 ⇒ 会重复计） | 607.2 MiB | 50,674 | 12.6 KB |
| 实例化（`InstantiateType`） | 14.4 MiB | 22,304 | 677 B |
| 检查器诊断（`Error`） | 12.6 MiB | 6,068 | **2,174 B/条** |
| 类型节点求值（`GetTypeFromTypeNode`） | 10.5 MiB | 22,279 | 491 B |
| 联合创建（`GetUnionType`） | 6.8 MiB | 13,657 | 523 B |
| 惰性诊断注册（`AddLazyDiagnostic`） | — | 76,684 | 闭包 ≈ 5–7 MiB（估计） |

分桶给出的形状与解析侧**相反**：检查侧不是"少数巨块"，而是**均匀摊在昂贵节点上**——`CheckSourceElement` 的 50,674 次里有 **15,124 次零分配**，而 **10,433 次（≥8K）独占 548 MiB（每次约 52 KB）**。也就是说钱在**少数声明节点的类型解析**里（`GetTypeOfSymbol`/`ResolveStructuredTypeMembers`/`GetResolvedSignature`/实例化缓存那一层），而不是逐节点固定开销；`AddLazyDiagnostic` 的 76,684 个闭包按估计只有 5–7 MiB，**不值得**为此改动注册形状。

再往一层（按 `SyntaxKind` 记 `CheckSourceElement` 的账**读不出分划**：`Block`/`VariableStatement`/`ClassDeclaration` 这些容器把子树全算进去了，属包含关系而非划分）。非嵌套入口才是有用信号，且钱集中在**调用解析簇**：

| 入口 | 字节 | 调用数 | 每次 |
|---|---|---|---|
| `GetResolvedSignature` | 63.0 MiB | 3,820 | 17.3 KB |
| `GetTypeOfSymbol` | 62.9 MiB | 86,513 | 761 B |
| `ResolveStructuredTypeMembers` | 57.0 MiB | 162,457 | 368 B |
| ├ `ChooseOverload` | 43.4 MiB | 2,502 | 18.2 KB |
| ├ `GetSignatureApplicabilityError` | 26.2 MiB | 2,850 | 9.7 KB |
| └ `InferTypeArguments` | 19.4 MiB | **628** | **32.3 KB** |

（这些入口互相包含，不能相加。）结论：检查侧的**单价最高**操作是"解析一次调用"——`InferTypeArguments` 每次 32 KB ⇒ 大量小对象（推断上下文/mapper/候选列表）而不是某个坏写法；下一步要么按"每个推断上下文的对象数"再切一层，要么转去做结构性的并行解析（时间收益更大）。

#### `struct`/CRTP 评估：按类型记账后的结论（不做）

问题：能否把一些小类型改成 `struct`（必要时用 CRTP `IIntf<TSelf>` 避免装箱）来省分配？答案由**按类型的分配量**决定，实测（p2000，整轮 536 MiB）：

| 类型（现为 `class`） | 分配 | 调用数 | 每次 | struct 可行性 |
|---|---|---|---|---|
| `TextSpan`（`GetErrorSpanForNode` 口径） | 1.5 MiB | 62,455 | 25 B | 可，但收益 0.3% |
| `CommentRange` | 1.2 MiB | 12,168 | ~100 B | 可，但它与 `Node` 同为 `IReadonlyTextRange` 成员 |
| `FlowNode` | 1.1 MiB | 29,143 | ~38 B | **不可**：实体语义 + 11 个接口 + 可变流程图节点 |
| `InferenceContext` / `InferenceInfo` | 1.0 / 0.1 MiB | 650 / 957 | — | 存放于 `List<IInferenceInfo>`（接口）⇒ 转 struct 即装箱 |
| `Diagnostic`（`CreateDiagnosticForNode`） | 8.7 MiB | 59,725 | 153 B | 有身份、被缓存与复制 ⇒ 不宜 |
| `TypeMapper` | 多态判别记录（`Kind` + 5 种变体） | — | — | **CRTP 不适用**：调用点选的是运行时 kind，无静态类型 |

合计 ≈ **13 MiB / 536 MiB（2.4%）**，而代价是把 Layer 0–1 的 `ITextSpan`/`ICommentRange`/`ITypeMapper` 改写成 `IXxx<TSelf>` 并对全仓调用点做泛型化。**决定：不做。** 判据（写进纪律）：CRTP/struct 只在"**调用点静态已知类型 + 非实体语义 + 高次数**"三条同时成立时才划算；本移植里恰好是**多样态判别记录**（`TypeMapper`、`InferenceInfo`、`IReadonlyTextRange`），转 struct 只会把"堆对象"换成"装箱对象"，分配量不变甚至更多。真正剩下的分配大头（解析侧节点/字符串/JSDoc 物化 213 MiB、检查侧调用解析簇 43 MiB、bind 50 MiB）都是**必须物化的对象图**，不在值类型这一侧。

#### 两处崩溃：`Convert.ToInt32/64` 的窄化（已修，与参考逐字对拍）

同一轮探针在**语法错误的输入**上抓到两个 `OverflowException`（参考只报诊断）：

| 输入 | 参考 | 端口（修前） | 端口（修后） |
|---|---|---|---|
| `var a = 0777777777777777777777;`（22 位 legacy octal） | TS1121 + 发射 `9223372036854776000` | **`OverflowException ... UInt32`**（`Convert.ToInt32(_tokenValue, 8)`） | TS1121 `'0o1000000000000000000000'` ✓ 逐字一致 |
| `var s = "\u{FFFFFFFFFFFFFFFFFFFF}";`（20 位十六进制） | TS1198 | **`OverflowException ... UInt64`**（`Convert.ToInt64(escapedValueString, 16)`） | TS1198 ✓ |

修法都是**回到参考的 double 语义**：`Scanner.ScanNumber` 的 legacy-octal 分支改 `Js.ParseInt(digits, 8)` + `Js.NumberToString` + 新增的 `Js.NumberToStringRadix(value, 8)`（`(+tokenValue).toString(8)` 的端口实现：整数值精确，≤2^53 走 `ulong` 循环、以上走 `BigInteger` 的 `mantissa << shift` 精确分解）；`ScanExtendedUnicodeEscape`/`PeekExtendedUnicodeEscape` 改 `Js.ParseInt(text, 16)`（double，永不溢出）。验证：8–44 位 legacy octal 扫描 37 个用例，与参考 CLI 的**消息多重集合逐字相同 37/37**（含 >2^53 double 的精确八进制位）；`\u{...}` 用例的语法/语义诊断与参考 API 相同。验收：1,521 测试、`TypeDiff` 与 `EmitDiff` 全语料各 clean 5908/5908。

#### 口径：端口诊断 = 参考 **API**，不等于参考 **CLI** 的打印集

探针期间发现的一个**必须记住的对拍口径**（不是端口缺陷）：参考的 `tsc` CLI 在 `esc.ts` 上打印 6 条，而参考自己的 `program.getSemanticDiagnostics()` 是 7 条——多出的一条 `TS7031` 与一条 `TS1005` **同位置**（offset 107）。端口（`ProjectBench --diagnostics`）与参考 **API** 逐条一致（含 `'(Missing)'` 那个名字），与参考 **CLI** 差那一条。参考 CLI 侧的抑制/去重是它自己的报告策略（`hasParseDiagnostics` 只对 `grammarErrorOn*` 族生效，端口这四族守卫都已忠实移植 ✓）。⇒ 全语料诊断对拍应对准**参考 API**，或等端口自己的 CLI（M8）就绪后做 CLI↔CLI；直接拿 `tsc` 的 stdout 当 oracle 会在这类同位置诊断上产生假分歧。

### 18.4k 宿主侧并行"读+解析"（`createProgram` −31%，整轮 −12%）

**为什么在宿主侧而不是编译器核心**：从文件名产出 `SourceFile` 是 `getSourceFile` 的契约，只有宿主知道怎么兑现（宿主可能从内存作答、甚至不解析），所以程序**不能**背着宿主自己解析。程序能做的只是"告诉宿主我接下来要问哪些文件、用什么选项"——这就是新加的可选钩子 `Program.IParallelSourcePrefetchHost`（实现它的只有端口自己的 `CompilerHost`，第三方宿主零影响，且 `TSC_CS_PARALLEL=1` 时整个预取被跳过 ⇒ 天然 A/B 开关）：

```csharp
    internal interface IParallelSourcePrefetchHost
    {
        void PrefetchSourceFiles(IReadOnlyList<SourceFileRequest> requests, int degree);
    }
    internal readonly record struct SourceFileRequest(string FileName, CreateSourceFileOptions Options);
```

程序在走图前用**走图同一个成员**（`GetCreateSourceFileOptions`）按根文件顺序算出请求；宿主用一个"每文件读+解析"的 `Parallelism.For` 填缓存，`getSourceFile` 命中即返回。**提问顺序不变**⇒文件图、文件列表、诊断顺序、发射输出都不变，只有墙钟动。

**第一个坑（真被发现）**：走图时算出的选项会被**走图进度影响**（模块解析/package.json 缓存是边走边填的），把根文件选项提前一次算完并信任，会改变部分文件的解析结果——**EmitDiff 当场报出 5,908 个用例里 123 个发射不同的 JavaScript**（`TSC_CS_PARALLEL=1` 关掉预取后同一环 clean ⇒ 因果确认）。修法：宿主把预取时的选项与走图时的选项**逐字段比较**，不相等就丢弃预取、让正常路径重解析（`SameParseOptions`）。

**第二个坑（自己抓的）**：`SameParseOptions` 最初用 `ReferenceEquals` 比 `SetExternalModuleIndicator`，而 `getSetExternalModuleIndicator` **每次调用新建一个 lambda**（参考里也有同样的 `TODO: Should this callback be cached?`，utilities.ts:8981）⇒ 比较恒假，**每个预取文件都被拒绝**（实测 `reused=0 rejected=2000`）。改为比较"非空性"并写明理由：同一个 program 内该回调是那份 options 的纯函数，两个请求读的是同一个 program 的 options。修好后 `reused=2000 rejected=0`。

**实测（Release，探针已移除）**：

| | 串行（`TSC_CS_PARALLEL=1`） | 并行预取 | |
|---|---|---|---|
| p2000 `createProgram` | 0.812 s | **0.556 s** | −31% |
| p2000 整轮 | 2.335 s | **2.055 s** | −12% |
| types 分片 `createProgram` | 0.730 s | **0.521 s** | −29% |
| types 分片整轮 | 3.725 s | **3.488 s** | −6% |
| p2000 峰值工作集 | 343 MiB | **263 MiB** | −23% |

解析段自身：串行 0.52 s → 预取墙钟 **0.146 s**（2,093 文件、24 核，近线性）。

**验收**：1,521 测试；`TypeDiff` 与 `EmitDiff` 全语料各 clean 5908/5908（环走的就是带预取的宿主，且命中率高）；**p2000 的完整诊断列表在"串行 vs 复用 2000 文件"两条路径下逐字节相同**（13,211 行，`cmp` 无差异）。

**剩下的瓶颈**：`createProgram` 现在是 0.52 s，其中 **文件图遍历（含 bind）0.315 s**、预取 0.146 s。图遍历里主要是**模块解析**（每文件导出/import 解析、`fileExists`/`readFile`、package.json 查找），它**顺序敏感**（缓存边走边填）且目前串行——那是下一个大杠杆，也是比"再省几 MiB 分配"更值钱的方向。

### 18.4l 全语料崩溃收敛之一：`GetTypeAliasInstantiation` —— 与 TS 6.0 **共有**的缺陷

**现场**（`artifacts/bisect/p5069.json`，5,069 文件对抗性前缀）：`ArgumentNullException` in
`Checker.GetTypeAliasInstantiation`（`links.Instantiations!` 为 null）← `CreateMarkerType` ← `GetVariancesWorker`
← `GetVariances` ← `StructuredTypeRelatedToWorker`。

**根因（逐层取证）**：

1. 崩溃符号是 `'C'`（id 5803）：**226 个声明**、`flags = BlockScopedVariable, ExportSupportsDefaultModifier,
   Module, TypeAlias, Transient, Assignment` ⇒ 是 **JS 类/赋值合并**（`MergeJSSymbols`，`Assignment` 位）产生的
   **transient 克隆**。
2. 它的 `DeclaredType` 已被盖上：`GetDeclaredTypeOfClassOrInterface` 那条"双写"（
   `originalLinks.DeclaredType = links.DeclaredType = createObjectType(...)`，`MergeJSSymbols` 会换符号、`links`
   随之指向合并后的克隆）先跑了 ⇒ 别名分支 `GetDeclaredTypeOfTypeAlias` 的 `if (links.DeclaredType is null)`
   守卫**永久跳过** ⇒ `links.TypeParameters` / `links.Instantiations` **永不初始化**。
3. `GetVariances → CreateMarkerType`（`symbol.flags & SymbolFlags.TypeAlias` 为真）⇒ `GetTypeAliasInstantiation`
   解引用空字典。参考的实现里这一处是 `links.instantiations!.get(id)`，**同样假设它已存在**。

**关键取证：这个缺陷参考自己也有。** 用参考 **API** 在同一个前缀上跑 `program.getSemanticDiagnostics()`：

```
TypeError: Cannot read properties of undefined (reading 'get')
    at recursiveTypeRelatedTo … isRelatedTo … compareSignaturesRelated
```

即 `links.instantiations.get(id)` 的 `undefined.get` —— **与端口同一个缺陷、同一条调用链**。参考 **CLI**（以及
tsgo 的 CLI）之所以"干净地"只报 2,589/2,606 条语法诊断，正是 §18.4j 记下的那道闸门（文件有语法错误时不计算
语义诊断）；**语义诊断一旦真的算，参考自己就崩**。⇒ 端口此前那次崩溃**原本是忠实的**。

**决定与修法**：全语料跑通是本项目的目标之一，而参考在这条输入上**给不出语义结果**（没有可忠实的对象），
因此端口在这一处**不再复刻参考的缺陷**，而是按参考在别处（`getSignatureInstantiation`）用过的惰性初始化写法
补上它依赖的不变式：

```csharp
        if (links.TypeParameters is null) links.TypeParameters = GetLocalTypeParametersOfClassOrInterfaceOrTypeAlias(symbol);
        links.Instantiations ??= new Dictionary<string, IType>(StringComparer.Ordinal);
```

**验收**：前缀从"崩溃"变为跑通——**语法诊断 2,589 条与参考 CLI 逐数相同**（tsgo 亦 2,606 行、`TS1005` 计数
1429 = 端口 1429）；1,521 测试；`TypeDiff` 与 `EmitDiff` 全语料各 clean 5908/5908（修复只影响参考会崩的那类输入，
所有参考能算的用例逐字节不变）。

**全语料对拍的口径结论**：语义诊断在 5,000+ 文件的对抗性输入上**没有 oracle**——tsc 6.0 的 API 崩、tsc/tsgo 的
CLI 都抑制语义诊断。可用的 oracle 只有：① 语法诊断（端口 vs tsc/tsgo CLI ✓ 已对齐）；② 用例级 `.types`/发射
（两外环 5,908/5,908 ✓）；③ 端口自身的串行/并行双跑逐字节一致。语义侧的"对拍"因此只能靠②，而不是整语料数字。

**下一个崩溃（同一族，已修）**：`CheckClassLikeDeclaration` 的 `(IObjectType)GetTypeOfSymbol(symbol)` 抛
`InvalidCastException`（该合并符号的值类型解析成了 `IntrinsicType`）。这里参考的写法是
`getTypeOfSymbol(symbol) as ObjectType` —— **`as` 是擦除的**，参考把那个非对象值一路带进静态侧的读取、从它上面
读到 `undefined` 也不会失败；端口的 `(IObjectType)` 是运行时强转，于是崩。修法是把值保持为 `IType`
（`staticTypeValue`）：三处取 `IType` 的调用传原值，唯一需要对象面的 `CheckMembersForOverrideModifier` 在值不是
对象类型时跳过（文档化为偏差——TS 6.0 的 API 在这之前就崩了，此处没有可忠实的行为）。

#### 里程碑：全语料单 program 跑通

`artifacts/perf/tsconfig.json`（5,908 根、6,001 源文件、129 个 .d.ts，2,000+ 文件互相冲突的全局脚本）：

| | 数值 |
|---|---|
| 语法诊断 | **2,661** —— 与 `tsc` 6.0 CLI 的 2,661 **逐数相同**（tsgo 2,678） |
| 语义诊断 | 67,030 |
| createProgram / semantic / total | 1.03 s / 9.25 s / **10.29 s** |
| 分配 | 5.87 GiB（parse+bind+check 全阶段） |

验收：1,521 测试；`TypeDiff` 与 `EmitDiff` 全语料各 **clean 5908/5908**（含这两处收敛修复，说明参考能算的所有
用例逐字节未变）。

### 18.4m 两个方向的收敛：模块解析不是杠杆、检查侧分配是"必要物化"，真正的赢是 GC 模式

#### 方向①：图遍历里的"模块解析"——实测否掉

先前按分配画像推断图遍历（p2000 0.315 s）是"模块解析"的钱。用端口**自带**的 `Performance` 分段
（`ProjectBench --perf-measures`，与参考 CLI 的 `--extendedDiagnostics` 同源）实测：

```
# perf Program        483.0 ms   ← createProgram 全部
# perf Parse          322.7 ms   （并行阶段，同名 mark 会互相竞争 ⇒ 该值仅供量级参考）
# perf Bind           111.5 ms   （已在 checker 初始化里并行）
# perf I/O Read        14.7 ms
# perf ResolveModule   24.1 ms   ← 只占 createProgram 的 5%
```

宿主调用侧同样排除：`FileExists` 1,856 次 / 9 ms；`ReadFile` 6,314 次 — 但它的 2.49 s 是**并行下墙钟求和的
假象**（24 线程互相排队，串行实测 53 µs/次）。⇒ **模块解析不是杠杆**；图遍历剩下的 ~0.29 s 是每文件的
import 收集与入册（`CollectExternalModuleReferences`/`AddFileToFilesByName` 一族），量级不大。

#### 方向②：检查侧 322.8 MiB 的构成——分桶给出"必要物化"的结论

| 入口 | 分配 | 调用 | **零分配占比** | 大块 | 性质 |
|---|---|---|---|---|---|
| `GetTypeOfSymbol` | 62.9 MiB | 86,513 | **73%** | 791 次 ≥8K = 36.3 MiB（≈46 KB/次） | 成员表，**常驻** |
| `ResolveStructuredTypeMembers` | 57.0 MiB | 162,457 | **93%** | 1,229 次 ≥8K = 40 MiB | 同上（命中即零分配） |
| `InferTypes` | 14.3 MiB | 1,037 | 0 | 70 次 ≥8K = 13.4 MiB | 推断暂存 + 触发的大类型物化 |
| `InstantiateSignature` | 2.5 MiB | 2,545 | 0 | 均匀 <1K | 小对象 |

⇒ 检查侧的字节**以常驻的成员表/签名表为主**，不是可回收垃圾堆。顺着"可回收"这条线只找到两处，且都属
"JS 里便宜、C# 里贵"的既定模式：

1. **推断的 `visited` 键**：参考写 `source.id + "," + target.id`（JS 里同样分配，但 C# 多两次 int→string）
   ⇒ 改成 `Dictionary<(int, int), int>`，键等价（id 唯一、`"1,23" ≠ "12,3"`）。
2. **`GetTypeListId` 的二次型拼接**：参考在循环里 `result += …`；C# 里每个 `+=` 都复制 ⇒ 多游程的联合键会造
   出几十 KB 垃圾。改为缓冲，且**延迟分配**：单游程（常见形状）仍走纯字符串，出现第二个游程才转
   `StringBuilder`（第一版直接用缓冲反而让 p2000 变差 —— 单游程键多了 `StringBuilder` + 其 `char[]`）。

**配对 A/B（同构建，只差这两处）**：types 分片语义分配 **1460.7 → 1452.6 MiB（−8.1，−0.55%）**，p2000
322.8 → 323.1 MiB（+0.1%，噪声内）；诊断数逐位不变。⇒ 保留（严格更少垃圾、语义等价、带 PORT-NOTE），
但**记为微收益**：检查侧没有"砍一大刀"的空间。

#### 真正的赢：可执行程序的 Server GC

GC 占混合负载 CPU 的 45%，而分配以常驻为主 ⇒ 先换 GC 模式（零代码、零语义风险）。新增
`Directory.Build.targets`，只对 `OutputType=Exe` 的项目设 `ServerGarbageCollection=true`（库消费者不受影响；
放在 targets 是因为 props 在项目属性之前导入，那时 `OutputType` 还没定）：

| workload | workstation | Server GC | 峰值工作集 |
|---|---|---|---|
| p2000 semantic | 1.424–1.445 s | **1.199–1.218 s（−15%）** | 264 → 283–307 MiB |
| types 分片 semantic | 2.81–2.98 s | **2.62–2.73 s（−5…9%）** | 340 → 447 MiB |
| 全语料 total | 9.96 s | **9.10–9.72 s（−5…9%）** | 953 → 1,137 MiB |

分配字节与诊断数**逐位不变**（`System.GC.Server=true` 出现在可执行程序的 runtimeconfig.json 里，验收时无
环境变量）。验收：1,521 测试；`TypeDiff`/`EmitDiff` 全语料各 clean 5908/5908；全语料语法诊断仍 2,661。

**纪律（新增）**：GC 占比高时，先改 GC 模式（零风险、立即见效），再谈砍分配；砍分配要按"**常驻 vs 可回收**"
区分——`InferTypes` 那类"每次 190 KB、70 次占 89%"的直方图尖峰看着可疑，实为触发的大类型物化；而真正可回收的
是 `+=` 字符串、闭包、`params object?[]` 这类每次调用的固定垃圾。

#### Server GC 之后：新画像与性能线的收尾

工具新增一行固化诊断 `# gc server=…`（测量必须与 GC 模式一起读；p2000/全语料均确认 `server=True`）。

**采样（Server GC 下，p2000，`TSC_CS_PARALLEL=1`）**：`Thread.PollGC` 仍占 45%，但这不是假象——单文件样本里它
只占 7.4%、其余是 `LowLevelThreadBlocker.TimedWait`（等待）⇒ 45% 是**真实的 GC 协调循环**，即"分配量仍在驱动
画像"。其余热点被摊平：文件 I/O 四入口（`CreateFilePrivate`/`GetFileAttributesExPrivate`/`ReadFile`/`CloseHandle`）
合计 ~6.8%、`GetHashCode` 1.5%、`CallClassConstructor` 1.2%、`ForEachChild` 含 8%、其余 <1%。

**③ 图遍历余下时间的归因（原假设被否）**：`CollectExternalModuleReferences` 实测 **0.009 s / 2,093 次**（可忽略），
`ProcessRootFile` 的 0.708 s 是**嵌套求和**的假象。createProgram 已降到 **0.447 s**（Server GC + 并行预取；本会话
起点 0.78 s）。剩下的是每文件 `GetCreateSourceFileOptions`（package.json / implied-format 查询、填缓存、顺序敏感）
⇒ 并行化的守卫会像预取那样频繁拒绝复用，**不做**。

**② 诊断 `params object?[]` 装箱：量了，放弃**。`Checker.Error` 6,068 次 / 12.6 MiB（子树，含惰性诊断冲刷）+
`CreateDiagnosticForNode` 59,725 次 / 8.7 MiB ≈ **21 MiB（p2000 的 3.9%）**；其中 `params` 数组在**调用点**构造，
按 ~70k 次 × ~60 B 估 ≈ 4 MiB（**0.8%**）⇒ 为不到 1% 去改 ~100 处调用点的签名，不划算（记录为"已量，放弃"）。

**工具侧唯一还值得的一处**：`DiskSystem.ReadFile` 原本 `File.Exists(full) ? File.ReadAllText(full) : null` ⇒ 每个
文件两次系统调用（画像里文件系统入口 ~6.8%）；改为单次 `ReadAllText` + 捕获"未找到/无权限"并返回 null（与
`File.Exists` 的答案一致）。

**剩下最大的结构性杠杆：emit 并行化**。types 分片实测 `emit` **0.539 s / 580 MiB 分配 = 整轮 3.67 s 的 15%**
（比早先记的 0.43 s 大）。并行化需要先解决：共享 `EmitResolver`/`NodeBuilder` 的惰性缓存（需按锁或按文件隔离）、
helper 的发射、以及 `host.writeFile` 的**调用序**（参考按文件序写；并发写需要 opt-in host + 逐文件缓冲后按序落盘）。

**本会话累计（p2000）**：total **2.24 → 1.69–1.72 s（−23%）**、整轮分配 600 → 536 MiB、峰值 343 → 283–307 MiB；
两条环的端口段：`TypeDiff` 10.6 → **6.72 s**、`EmitDiff` 9.35 → **4.53 s**（Server GC 对"每用例一个 program、
分配密集"的工具收益最大）。验收：1,521 测试；两环各 clean 5908/5908；全语料语法诊断仍 2,661。

### 18.4n 第三处"JS 里便宜、C# 里贵"的拼接：发射器的输出累积（emit 分配 −57%）

`EmitTextWriter` 的 `_output` 在参考里是 `string` + `+=`（utilities.ts 的 `writeText`/`rawWrite`/`writeLine`），
端口照搬 ⇒ **每写一段就整串复制**。一个 ~8 KB 的发射文件分 ~200 次写入 ⇒ 前缀复制累计约 687 KB/文件 ⇒
types 分片的 emit 阶段 844 个文件分配 **580 MiB**（比解析的 109 KB/文件还大 6 倍）。改用 `StringBuilder`
（`Clear()` 换成 `Clear()`，容量还能被 `PrinterFactory` 的 memoized printer 跨文件复用）：

| | 改前 | 改后 |
|---|---|---|
| types 分片 emit 分配 | 580.5 MiB | **250.4 MiB（−57%）** |
| types 分片 emit 时间 | 0.539 s | **0.431 s（−20%）** |
| types 分片 semantic 分配 | 1451.8 MiB | **1414.6 MiB（−37 MiB，同一个 writer 也在给类型转字符串/诊断格式化用）** |
| `TypeDiff` 端口段 | 6.72 s | **3.60 s** |
| `EmitDiff` 端口段 | 4.53 s | **2.21 s** |
| 全语料整轮分配 | 5,869 MiB | **5,614 MiB** |

**验收**：1,521 测试；`EmitDiff` 全语料 **clean 5908/5908**（这一处改的是**输出累积**，逐字节相等是最强判据 ✓）；
`TypeDiff` 亦 clean 5908/5908；全语料语法诊断 2,661、语义 67,030 **均不变**；p2000 total 1.676 s。

**这是同一模式的第三处**（扫描器的二进制数字累积、检查器的类型 id 列表、发射器的输出）。⇒ 写进移植纪律：**凡是
参考里"在循环/高频路径上用 `+=` 拼字符串"的地方，一律先怀疑二次型**；`grep` 一遍 `+=` 就能列清单（`EmitTextWriter`
是 grep 时漏掉的那种，因为它藏在 `Utilities/` 而不是 `Emit/`）。

**余量**：emit 现在 250 MiB / 844 文件 = **297 KB/文件**，而一个文件的输出只有 ~8 KB ⇒ 仍有约 290 KB/文件
的可疑分配。已按顺序排除（都有实测）：

| 假设 | 实测 | 结论 |
|---|---|---|
| 注释文本切片 | `removeComments: true` 后 emit 仍是 **249.9 MiB**（vs 250.4） | ✗ 与注释无关 |
| 标点文本现造字符串 | `ScannerUtilities.TokenToString` 是**静态查表** | ✗ 零分配 |
| `WriteTokenText(token, Action<string>)` 的实例方法组委托 | 21,562 次调用 / **1.6 MiB** | ✗ 只占 0.6% |
| `Printer.WriteFile`（打印管线本体） | **214.5 MiB / 844 次 = 254 KB/文件** | ← 余量在这里，未收敛 |

⇒ 下一个可拆点：在 `Printer.WriteFile` 内按 `Print`/`GetLiteralTextOfNode`/`GetLinesBetweenNodes`/注释发射分
一层探针（每文件约 3k 节点、~85 B/节点，量级像"逐节点一个小对象"）。

**第二轮（叶子层）实测**：

| 叶子 | 分配 | 调用 | 结论 |
|---|---|---|---|
| `EmitTrailingCommentsOfPosition` | 7.7 MiB | 58,399 | 小 |
| `EmitLeadingCommentsOfPosition` | 3.4 MiB | 27,487 | 小 |
| `GetLinesBetweenNodes` | 3.2 MiB | 23,094 | 小 |
| `GetLiteralTextOfNode` | 0.4 MiB | 7,964 | 小 |
| **`GetPipelinePhase` 的实例方法组委托** | **10.8 MiB** | （每节点一次链式解析） | **已修** |
| 核心逐节点打印（余量） | **≈225 MiB** | — | **未收敛** |

第五行是真修掉的一处：`GetPipelinePhase` 里五个 `return PipelineEmitWithX;` 都是**实例方法组转换**，每次调用都
新建一个委托（参考里返回的是模块级函数引用，免费）。改为**每个 printer 缓存五个委托**（`PrinterFactory` 的
memoized printer 只建一次）：emit 分配 250.4 → **239.6 MiB**，`EmitDiff` 仍 **clean 5908/5908**。

**第三轮：按节点种类的"独占"划分**（`exclusive = total − Σchildren`，用 thread-static 子和栈；这是唯一能把递归打印读成分划的办法）：

| 独占项 | 分配 | 次数 | 每次 | 判定 |
|---|---|---|---|---|
| **`GenerateNameIfNeeded`**（唯一名生成） | **74.9 MiB** | 23,046 | **3,406 B** | 见下 |
| **`BinaryExpression`** | **49.9 MiB** | 4,189 | 12,494 B | 待再切（trampoline/`WorkArea`/父规范化） |
| **`Identifier`** | **20.9 MiB** | 56,915 | 384 B | `GetTextOfNode` 的 Substring（**与参考同**）+ `EmitList` |
| `ElementAccessExpression` | 6.2 MiB | 573 | 11,384 B | 表达式侧的通用成本 |
| `VariableDeclaration`/`DeclarationList` | 5.0 / 5.0 MiB | ~5,72x | ~918 B | |
| 其余 20+ 种 | 各 ≤4.8 MiB | | | |

前三项 = **146 MiB / 236 MiB（62%）**，且每一项都有明确代码指针：

1. **唯一名生成**：`GenerateNameIfNeeded → GenerateName → MakeName → MakeUniqueName`。端口与参考**逐行一致**（含
   `autoGeneratedIdToGeneratedName` 缓存 ✓ 实测缓存生效），成本来自 `MakeUniqueName` 的**候选名扫描**：每轮
   `baseName + i` 造串 + `IsUniqueName`（文件标识符表/保留名/`_generatedNames` 三处哈希查找）。对抗语料里
   同基名成千 ⇒ 平均每次要扫 ~50 个候选 ⇒ 固有 O(N²)（参考同样如此）。**可做的忠实加速**：候选名用
   `ReadOnlySpan<char>` 交替查表（`HashSet<string>.GetAlternateLookup<ReadOnlySpan<char>>`，.NET 11 可用）把
   中间候选的垃圾消掉——但 `_handlers.HasGlobalName` 这个宿主回调只收 `string`，所以最终候选仍需物化。
2. `BinaryExpression`：`CreateEmitBinaryExpression` 的 trampoline（`WorkArea` 七个栈，链根分配一次）+ 每算子的
   父规范化规则；需要再切 `OnEnter/OnLeft/OnOperator/OnRight/OnExit` 五处才能定论。
3. `Identifier`：`GetTextOfNode(node, false)` 每次一个 Substring（**参考也这样**，必要物化）+ `EmitList`（已修）。

**第四轮：再往下切 `BinaryExpression`（49.9 MiB）** ⇒ 全部落在操作数发射助手上：

| 独占项 | 分配 | 次数 | 每次 |
|---|---|---|---|
| **`GetPipelinePhase`**（阶段解析，含**发射替换回调** `_handlers.SubstituteNode`） | **42.3 MiB** | 286,613 | **154 B** |
| `MaybeEmitExpression`（操作数发射助手；上面那项是它的子集） | 44.3 MiB | 8,768 | 5,301 B |
| `OnOperatorBinaryExpression` | 3.8 MiB | 4,384 | 905 B |
| `OnEnterBinaryExpression` | 3.4 MiB | 4,384 | 817 B |
| `OnExitBinaryExpression` | 1.4 MiB | 4,384 | 343 B |

⇒ `BinaryExpression` 的 49.9 MiB 里 **42.3 是 `GetPipelinePhase`**；该类内部已排除的项：按算子的父规范化规则（**已有
`ConcurrentDictionary` 缓存**）、五阶段委托（已缓存）、`ShouldEmitComments/SourceMaps`（便宜）、`AsParenthesizerRule`
的闭包（~64 B）。**剩下的唯一嫌疑是发射替换回调**（`_handlers.SubstituteNode`，由 emitter 用 resolver 装配）——
它是下一轮的探针目标（286,613 次 × 154 B ≈ 一次替换计算）。

**第五轮：`GetPipelinePhase` 归零，42.3 MiB 落在替换回调上**（先把探针**校准**：给"绝不可能分配"的
`Utilities.GetEmitFlags` 装同一套探针 ⇒ 615,595 次调用 **0 B/次** ⇒ 仪器无每次开销偏差，此前的读数可信）。

```
PROBEXCL transform SubstituteNode (TransformationResult)   42.3 MiB / 102,858 次 = 430 B/次
PROBEXCL pipeline phase lookup (GetPipelinePhase)            0.0 MiB / 286,613 次 =   0 B/次
PROBEXCL CALIBRATION getEmitFlags                            0.0 MiB / 615,595 次 =   0 B/次
PROBEXCL predicate ShouldEmitComments / ShouldEmitSourceMaps  0.0 MiB（各 ~183k 次）
PROBEXCL handler SubstituteNode (EmitPrintHandlers)          0.0 MiB（转发）
```

替换回调的身份（探针打印）：

```
PROBESUBST callback=ImpliedNodeFormatDependentTransformer.OnSubstituteNode
  enabledKinds=Identifier, PropertyAccessExpression, ElementAccessExpression, CallExpression,
               TaggedTemplateExpression, BinaryExpression, ShorthandPropertyAssignment, SourceFile
```

⇒ 是**模块格式（ESM/CJS）变换的替换**：它对 102,858 个模块引用**逐个创建重写节点**（`require("x").y` 一类）。
参考跑的是同一个变换 ⇒ **固有成本**，非移植缺陷。

**最终结论（回答"剩下的分配在哪"）**：emit 236 MiB =

| 项 | 分配 | 性质 |
|---|---|---|
| 唯一名生成的候选扫描 | 74.9 MiB | 固有（参考同算法，对抗语料上 O(N²)） |
| 模块格式替换回调（逐引用建节点） | 42.3 MiB | 固有（参考同一变换） |
| `Identifier` 的 `GetTextOfNode` Substring | 20.9 MiB | 固有（参考同） |
| 其余 20+ 种节点 | 各 ≤6 MiB | 分散，无单点 | 

**三项全部是"参考也这么干"的成本**——移植侧真正的缺陷是本轮之前修掉的三处（输出累积的二次型 −57%、两批
"实例方法组委托"合计 −14.6 MiB），emit 分配已从 **580 MiB 降到 236 MiB**。

### 18.5 工具侧并行：`tools/TypeDiff` 与 `tools/EmitDiff`

两个工具的端口半场原来是**按文件串行**跑（每个文件各自建 program），而这正是最适合并行的形状：文件之间零共享。新增 `--parallel <n>`（默认 `Environment.ProcessorCount`，`1` = 串行，用于 A/B），实现是**有界的 `Parallel.For` + 每文件结果槽**，最后按文件序落盘 —— 因此**索引产物与串行运行逐字节相同**（`port-hashes.txt` / `port-crashes.txt` 已实测 `cmp` 一致；`report.txt` 只差 node 侧自报的 `maxRSS` 与端口峰值这两行噪声）。

同会话 A/B（全语料 5,908 文件，本机 24 逻辑核；`port pass` 即端口半场耗时，峰值工作集取自报告的自报行）：

| 工具 | 串行 | 并行（24） | 加速 | 峰值工作集（串行 → 并行） |
|---|---|---|---|---|
| `tools/EmitDiff` | 21.10 / 21.18 / 21.37 s（三跑） | 11.87 / 11.99 s | **≈1.8×** | 128 MiB → 1155–1221 MiB |
| `tools/TypeDiff` | 18.67 s | 9.15 s | **≈2.0×** | 142 MiB → 1490 MiB |

两条结论：

1. **并行化立刻换来一个真缺陷**：首次并行 `EmitDiff` 有 3 个文件崩溃（`es2019/globalThisAmbientModules.ts`、`expressions/superCalls/superCalls.ts`、`node/nodeModulesConditionalPackageExports.ts`），栈都在 `ParenthesizerRules.GetParenthesizeLeftSideOfBinaryForOperator`（`Dictionary` 撕裂）——即 §18.3 的表里那一行。修好后全语料 `clean 5908/5908`，串行侧零回归。
2. **加速是次线性的（1.8–2.0×，不是 24×），热点是进程级 id 计数器**：`Utilities.GetNodeId`/`GetSymbolId` 的 `Interlocked` 在任何时刻都打在同一个缓存行上，worker 越多争用越重。Go 端同样是全局原子计数器（`ast/utilities.go:16-40`），所以这里也是"可以超过 Go 端"的位置之一：按 worker 成块预留 id（一次 `Interlocked.Add` 取 1,024 个）、或把 id 存储改成每分片 dense（与 18.4 的 `PagedLinkStore` 同一件事）。这条已登记，先用 profiler 确认争用占比再动。

### 18.6 待办（登记）

- **并行测试仍有低概率 flake**（**已修，见下**）：修完 `Performance` 的那一轮里出现过一次 `失败 36`（其后 20 跑全绿），未捕获到测试名。候选按审计排序：`Program/Program.ProgramDiagnostics.cs:312,382-388`（compute-once + 后写即断言）、`Program/Program.DiagnosticsAggregation.cs:203-204`（惰性 `AdditionalSyntacticDiagnostics`）、`Diagnostic.DeferArg` 在打印期回入 checker（`Checker/Checker.CallResolution.cs:1797-1800`）、`Core/SysWatch.cs` 的轮询静态、`Core/Debug.cs` 的 `CurrentLogLevel`/`IsDebugging`/`LoggingHost`、`Core/SysCore.cs:27`。

  **修法**：把"会改写**进程级**状态"的测试类交给 xUnit 自己的机制串行化——`ProcessWideStateCollection`（`DisableParallelization = true`）+ 五个 `[Collection]` 标注：`PerformanceTests`（`Performance.Enable/Disable` 是全局开关）、`CoreCollectionsTests`（`Debug.LoggingHost`/`CurrentLogLevel`/断言级别）、`SysCoreTests`（`SysCore.SetSysLog`）、`CoreComparisonTests`（`Core.SetUILocale`）、`TracingTests`（legend 与 trace 状态）。判定标准写在那个文件里：**类属于该集合，当且仅当它改写进程级槽**；只读编译产物、或者只做单线程语义断言的类不进（否则集合会把并行收益吞回去）。

  **验收**：修复后 **52 次**整程序集运行全绿（40 次 `--no-build` + 12 次带构建，1,521/1,521）。此前该失败是 1/60 量级，且**只在带构建的那一轮出现过**（首跑 JIT 让时序窗口打开），所以两种模式都跑了。
- `TracingTests` 共用 `TraceDir`，并断言 legend 的**相邻两条**是它自己的两次 trace（`TracingTests.cs:155-175`）——该断言要求"没有别的测试插进来"，已由 18.6 的进程级状态集合满足（整类串行）；legend 本身是进程级且加锁的，不需要按测试分目录。
- **性能主线（按 18.4d/18.4e 的实测重排）**：①先修 `CreateMarkerType` 串（让整语料单 program 可跑）；②**检查器的单线程热路径**——它占 76% 墙钟且不可分片，先例收益是 §17.17 的 -57%；③并行只在解析/绑定/发射之外还有余量时才谈（现测发射仅 8%，见 18.4e）。

### 18.7 对齐 tsgo（TSC 7.0）后的性能实测：每 case 成本分解，与两次被否掉的 lib 预取（2026-09-27）

**对齐目标的变更（本会话裁定）**：行为与验收的参考由 `release-6.0`（tsc 6.0.3）改为 **tsgo 7.0**；差分的第三方由 `lib/typescript.js` 6.0.3 改为**全局 `tsc` 7.0.2**（原生二进制，比 node 快一个量级，这也是旧环"跑不动/跑得慢"的直接原因）。§9.x 中"TSGO 只可作形状参考、不一致处以 6.0.3 为准"的边界随之下调为历史记录。

**单文件 program（1 个 4 行文件 + 93 个默认 lib）的成本分解**（本机中位，Release，全新进程；`tools/ProjectBench` 与 CLI 两处测得的一致）：

| | 端口 | tsgo |
|---|---|---|
| 全量（含检查 lib） | **1224 ms** | 257 ms |
| `--skipLibCheck`（解析+绑定，不检查） | **588 ms** | 117 ms |
| `--lib es5 --skipLibCheck`（只剩 1 个 lib 文件） | **211 ms** | — |
| `--noLib`（纯 CLR 启动 + JIT） | **203 ms** | — |
| tsgo 自报分相 | — | Parse **0.020 s** / Bind **0.007 s** / Check **0.137 s** |

⇒ 端口那 1224 ms = 203（CLR 启动+JIT）+ **≈385（解析+绑定 93 个 lib）** + **≈636（检查 93 个 lib）**；文件 I/O 只占 **~15 ms**（ProjectBench 在 2,093 个文件上实测的 `# perf I/O Read`）⇒ **把 libs 内嵌成程序集资源只值 ~1%**：贵的是解析（tsgo 27 ms，**14×**）与检查（tsgo 137 ms，**4.6×**）。

**实测一（否）：逐层预取 lib 引用。** 在 `processLibReferenceDirectives` 里把一层的 `/// <reference lib="…" />` 解析结果一次性交给已有的宿主预取钩子（`IParallelSourcePrefetchHost`，§18.4k）。插桩显示默认库要走 **46 轮**、其中多数只有 1 个文件（lib 参考图**又深又窄**：每个 `lib.esXXXX.d.ts` 引用的下一层同时被更下一层引用），往返开销抵掉并行收益 —— 分配 137 → **146.3 MiB**、墙钟无变化 ⇒ **回退**。

**实测二（否）：一次性 lib 闭包预取。** 先用**文本扫描**（整行 `///`-注释里的 `lib=`，`[GeneratedRegex]`）把整棵闭包读出来，再作为一个批次交给宿主。`createProgram` 确实降了（p2000：0.733 → **0.590 s，−19%**），但**分配 +105 MiB（213.4 → 318.6 MiB，+49%）**：预取时算出的 `CreateSourceFileOptions` 与走查稍后算出的**不一致**（`GetCreateSourceFileOptions` 读的是模块解析缓存，走查过程中会被填），宿主的 `SameParseOptions` 因此拒收整批并**重新解析**（+105 MiB ≈ 一次 lib 解析的全部成本，实测吻合）。按本仓的口径（分配字节是唯一无噪声的指标，§18.4g），这不是收益 ⇒ **回退**（代码回到改动前，仅保留本节记录）。

⇒ **结论**：lib 的解析必须**每进程只付一次**，而不是每 program 一次。三条路按收益/风险排序：①**宿主级（进程级）已解析 lib `SourceFile` 缓存**——对差分环与同一进程内的重复运行是数量级收益（CLI 单次运行不变），且端口已有 `ChangeCompilerHostLikeToUseCache` 的形状可循；②**预编译/内嵌的 lib 二进制 AST**（省掉那 385 ms 解析，顺带解决"部署时找不到 `lib/`"，即 §13.2 遗留）；③**并行解析 lib**（本节证明逐层与一次性两种接法都不成立，除非同时消除选项漂移）。检查侧的 636 ms（4.6×）则是 §18.4e 已登记的主线：它占 76% 墙钟、不可分片，按 tsgo 的 `checkerpool` 形状（4 分片 + 按文件归属）与分配削减来做。

**补记（同日，lib 并行的三次实测——全部否决，代码已回退）**：

| 形态 | 结果（`ProjectBench`，one.json = 1 文件 + 93 lib；中位） | 判定 |
|---|---|---|
| 逐层预取（`processLibReferenceDirectives` 一层一批） | 插桩：默认库要 **46 轮**、多数 1 文件；分配 137 → 146.3 MiB，墙钟不变 | 否（往返 > 并行收益） |
| 一次性闭包预取（全文扫描闭包 → 一批） | `createProgram` **−19%**，但分配 **+105 MiB**（选项漂移 → `SameParseOptions` 拒收 → 整库重解析） | 否（分配是唯一无噪声指标） |
| 闭包预取 + lib 选项记忆化 + 只扫前导 trivia | `createProgram` 0.365→**0.316 s（−11%）**、total 0.623→**0.578（−7%）**，分配仍 **+13.6 MiB**（≈12 个文件被重解析，键/选项均已归一仍存在） | 否（收益换分配，且 p2000 上 A/B 完全无差：ON 1.653 / OFF 1.644 s 中位） |

⇒ lib 这条线到此为止：**并行解析走不通**（默认库的闭包又深又窄、且最大的 `lib.dom.d.ts` 本来就在关键路径上），能动的只剩 ②（预编译/内嵌 lib AST）与"把解析本身的分配降下来"。

**p2000（2,000 文件 + 93 lib，单线程）的采样画像**：`Thread.PollGC` **40.9%**、文件 I/O 五入口合计 **≈8%**（CreateFilePrivate 3.4 / CloseHandle 1.7 / ReadFile 1.2 / GetFileAttributesEx 0.4 / GetFileType 0.3 + PathHelper 1.0）、`CallClassConstructor` 1.7%、`RuntimeHelpers.GetHashCode` 1.1%、`Array.Copy`（含）18.5%；**除 GC 外没有任何函数独占超过 1%**（checker 侧最大的 `CheckExpressionWorker` 0.57% 独占 / 24% 含）。⇒ 这一阶段的对手是**累计分配**（GC 40–50%）与**每文件多次系统调用**，不是某个热点函数；要按 tsgo 的形状改的是数据结构（字符串键的字典、委托式遍历、逐节点对象），而不是单点微优化。

### 18.8 新验收环：`tsgo` CLI 逐用例差分（端口第一次有了自己的 CLI）

- **`src/tsc`（M8 的第一块）**：`Program.cs`（命令行解析 + `emitFilesAndReportErrors` 的诊断装配 watch.ts:564-613 + `formatDiagnostics` 的纯文本报告）、`DiskSystem.cs`（真实文件系统的 `ISystem`）、`TscCompilerOptions.cs`（进程自己的选项对象）。`tsconfig.json` 读取、`-p`、`--build`、watch 仍未落地（见文件内 PORT-NOTE 的清单）。
- **差分器**：`artifacts/whalegirl-status/tsgo-case-diff.cjs` —— 对每个用例：按 `// @filename:` 切分单元、把 `// @name: value` 映射成**两边都一样的**命令行、在同目录下跑 `tsc`(tsgo) 与端口 CLI、逐行比对诊断。口径：只比诊断、恒 `--noEmit`（CLI 渲染不了 baseline 文本，那是 `tools/BaselineDiff` 的活）；不认识的指令记为 `unmodelled`，绝不猜。
- **harness 已知坑**（写下来避免重踩）：npm 的 `tsc` 是 `.cmd`，`spawnSync` 必须经 shell（`cmd /c`）；传给两个 CLI 的路径必须**绝对**（否则临时目录下解析失败，宿主自己的报错文本会被当成"端口输出"从而把所有用例判成 diverged）；`port.status === null` 是 harness 失败，不是判决。
- **首跑（`es6/Symbols`，95 用例，本机 113 s）**：`same=57 diverged=2 port-crash=36` ⇒ **36 个崩溃待诊**（复现：`node artifacts/whalegirl-status/tsgo-case-diff.cjs --cases D:/Source/GitHub/TypeScript-Go/tsc/testdata/tests/cases/conformance --port src/tsc/bin/Release/net11.0/tsc.dll --lib-dir D:/Source/GitHub/TypeScript-Go/tsc/internal/bundled/libs --filter "es6/Symbols/" --parallel 6`）。
  那 36 个"崩溃"随后查明是**我自己的 CLI 缺陷**：布尔选项没有吞掉紧随的 `true`/`false`，于是 `--declaration true` 把 `true` 当成一个**输入文件**，编译器于是报"文件不存在"——而那条路径又踩到下面那个真 bug。修好后同一分片是 `same=85 diverged=10 port-crash=0`（89.5%）。
- **CLI 一致性环**：`artifacts/whalegirl-status/tsc-cli-conformance.cjs` —— 同一批命令行分别喂给 tsgo 与端口 CLI，逐项比对 stdout 与退出码（25 例覆盖：无参数、help/version、未知选项、缺值、坏枚举值、布尔的各种写法、缺输入、`--listFiles`/`--explainFiles`、`-p`、`--noEmit` 等）。**首测 17/25 不一致 → 现在 8/25**，剩下的全是两类：help/version 文本（端口是简化版帮助）与 `--listFiles`/`--explainFiles` 的文件清单输出（未实现）。
- **对齐时按 tsgo 实测（不是照抄 6.0）的几处**：`ExitStatus`（0/1/2 与"输出是否生成"的区分）；命令行错误的**码与文案**走端口自己的诊断表（TS5023 未知选项、TS6044 缺参数、TS6046 带**值清单**的坏枚举值、TS5058 工程路径不存在）；值清单与**顺序**取自 `tsgo/internal/tsoptions/enummaps.go`；`-p/--p/-project/--project` 都一样收（tsgo 只剥一到两个前导 `-` 再查表，实测如此），但报错用长名；`--flag=value` 只对取值的选项成立（布尔写成 `--strict=false` 是 TS5023）；**`--target es5` → TS5108（tsgo 7.0 移除了 ES5）**。
- **这一轮从 CLI 侧抓到并修掉的两个编译器真缺陷**（都会被上一条环持续盯住）：①`Program` 给根文件造的 include-reason 用了 `ReferencedFile` 而不是 `RootFile`，于是"文件不存在"走到 `FileIncludeReasonToDiagnostics` 的 `unexpected file include reason kind 0` 直接抛异常（tsgo 报 TS6053 + "Root file specified for compilation"）；②`--lib es5` 把**库名**当**文件名**直接 `PathForLibFile`，于是去找 `lib/es5`，报"找不到全局类型 'Array'"（tsgo 的 `fileloader.go:205-211` 先过 `GetLibFileName`）。两者修好后：`missing.ts` 与 `--lib es5` 与 tsgo 逐字一致，两条外环仍 clean 5908/5908，测试 1521/1521。

### 18.9 验收基线切换到 tsgo 的 `testdata/baselines`（用户裁定方案 B）

**动机**：要拿到 tsgo 级别的性能就必须做 checker 分片，而分片会改变输出字节（联合类型成员顺序、额外诊断，§18.4d 实测）。这在"与 6.0 基线逐字节一致"的口径下不可接受；tsgo 自己就是分片的，它的基线与之自洽 ⇒ 口径换成 tsgo 的基线。

**调用方式**（`tools/BaselineDiff`，四件事缺一不可）：

```
--cases     <TSGO>/tsc/testdata/tests/cases/conformance     # 或 /compiler
--baselines <TSGO>/tsc/testdata/baselines/reference/conformance
--workspace <TSGO>/tsc/testdata        # 决定 `//// [tests/cases/…]` 头部路径
--lib       <TSGO>/tsc/internal/bundled/libs   --test-lib <TSGO>/tsc/testdata/tests/lib
--types-underlines false               # 见下
```

**`--types-underlines false` 的由来（重要）**：tsgo 的 `.types` 基线**不含下划线标注行**，原因是它的产生器还没实现——`internal/testutil/tsbaseline/type_symbol_baseline.go` 读 `result.underline` 并在非空时打印该行，但产生侧写着 `// !!! TODO: port underline printer, memoize` 与 `// underline: underline, // !!! TODO: underline`（第 403/413 行）。**这是 tsgo 的缺口，不是格式约定**，所以端口不跟着砍：默认仍输出 TS 的真实行为，只在比对 tsgo 基线时用该开关抑制该行。

**实测（`es6/Symbols`，95 用例 / 380 次 artifact 比对）**：

| 配置 | js | errors | types | symbols | 合计 |
|---|---|---|---|---|---|
| 默认（带下划线） | 94/95 | 92/95 | **1/95** | 93/95 | same 280 / div 100 |
| `--types-underlines false` | 94/95 | 92/95 | **90/95** | 93/95 | **same 369 / div 11** |

同一分片上**参考半场（tsc 6.0.3）与 tsgo 基线仍有 100 处不一致** ⇒ 端口现在比 tsc 6.0.3 本身更贴近 tsgo；剩余 11 条（js 1 / errors 3 / types 5 / symbols 2）才是真正待查的差异清单。

### 18.10 评审："按 tsc-6 的形态写 ⇒ 对静态类型不友好"的位置（以 tsgo 为准）

**tsgo 的节点表示（`tsc/internal/ast/ast.go` + `core/arena.go`，实读）**：每个 kind 一个 **arena**
（`core.Arena[T]` = 一块会增长的 `[]T`，`New()` 做 bump 分配并返回 `*T`），节点载荷是 struct 并**内嵌 Node 基座**
（`newNode` 里 `n := data.AsNode()`，即 `Identifier` 内部就含那个 `Node`）⇒ **每个节点一次 bump 分配、无额外间接层、GC 只看见大块数组**：

```go
type Node struct { Kind Kind; Flags NodeFlags; Loc core.TextRange; id atomic.Uint64; Parent *Node; data nodeData }
type Identifier struct { PrimaryExpressionBase; FlowNodeBase; Text string }
func (f *NodeFactory) NewIdentifier(text string) *Node { data := f.identifierArena.New(); … }
```

**能不能在 C# 也这么干？** 不能整体照搬：C# 的 struct 不能参与继承，端口那套 350 个类 +
散落全树的 `Node`/`Identifier?` 引用槽（Model/Parser/Binder/Checker/Emit ≈ 15 万行）必须改成
"kind + arena 下标 / ref"的访问形态才是同一个东西——那是重写，而且会丢掉现在来自类层级的字段类型安全
（`D001`）。**结论：不动节点模型**；可搬的只有下面三条（都不需要动模型）。

**按实测影响排序的三条可搬形态**：

| # | 端口现状（tsc-6 形态） | tsgo 形态 | 影响（p2000 实测） |
|---|---|---|---|
| 1 | `NodeArray<T> : List<T>`（`Model/NodeArray.cs:31`）—— 每个子节点列表 = 包装对象 + 后备数组**两个**堆对象 | `NodeArray` 是**值形态**：`{ Nodes []*Node; HasTrailingComma bool; … }`，切片头内联在结构里，只有后备数组一次分配 | 解析 140 MiB 里相当一部分（百万级节点、几十万列表） |
| 2 | 关系缓存的键是**每次检查现造的字符串**：`$"{source.Id},{target.Id}{postFix}"`（`Checker.Relations.Identical.cs:490`）与泛型分支的 `result += "-" + t.Id` 循环（同文件 470 附近） | **`CacheHashKey` = xxh3 128 位**，由 `keyBuilder` 在 **192 字节的栈内缓冲**上算出来（`checker.go:17695-17707`、`getRelationKey` `:17947`）——**零分配** | `Checker.IsTypeRelatedTo` 7.8 MiB/42,303 次；identical/strict 关系同族 |
| 3 | 热路径上的 LINQ/委托（`Checker` 31 处 `.Select/.Where/.Any/.ToList`；`Core.Some` 等） | Go 全是显式循环 | 弥散但真实（每次调用一个闭包/枚举器） |

**明确判定为"不是问题"的（避免照着"我觉得 C# 该怎样"去改）**：

- **符号表用字符串键是 tsgo 自己的形态**（`type SymbolTable map[string]*Symbol`，`ast/symbol.go:45`）。端口的
  `SymbolTable : OrderedDictionary<string, ISymbol>(Ordinal)` 与之同形；用有序字典换来的确定性顺序是逐字节
  输出需要的，**保留**。
- **模型里的可空值类型槽只有约 20 个**（`int?`/`bool?`，多为 JSDoc/正则相关），量级可忽略。
- **诊断的 `params object?[]` 装箱**：已量（21 MiB / 3.9%），§18.4m 裁定不做。

**第 1 条的落地形态**（最接近 tsgo 且不需要改模型）：`NodeArray<T>` 不再继承 `List<T>`，而是"只持有 `T[]` +
`HasTrailingComma`/`TransformFlags`"的引用类型（一个包装 + 一个数组，与今天相同）——**要真正省掉包装，得把它
变成 struct 槽位**，那就触及 `INodeArray<T>` 的所有使用者。⇒ 先做**第 2 条**（收益清晰、改动局部、可用探针
与两条环直接验收），第 1 条随后单独评估。
