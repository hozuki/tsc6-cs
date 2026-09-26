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

- `tests/cases/conformance/` — 5,908 个 `.ts` 用例
- `tests/baselines/reference/` — 53,026 个 baseline 文件（`.js` / `.types` / `.symbols` / `.errors.txt` / `.symbols.txt` 等）
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
| `tracing.ts` | 322 | `Core/` | ETW/trace 埋点，可后置 |

### Layer 1 — 数据模型（全局共享的类型与标志）

| 源文件 | 行数 | 说明 |
|---|---|---|
| `types.ts` | 9,535 | **全部** `SyntaxKind`、`Node` 及其派生接口、`Symbol`、`Type`、`Signature`、各种 `Flags` 位标志、`NodeArray`、`Diagnostic` 等 |

`types.ts` 是整个移植的**地基与最大翻译风险点**：它用 TS 的类型系统（条件类型、映射类型、字面量联合）表达"运行时约束"，C# 需要换成显式运行时结构（见 §4）。

### Layer 2 — 词法

`scanner.ts`（3,823）— `Scanner`（`createScanner`）、字符分类表、`textToKeywordObj` 关键字表。

### Layer 3 — 语法

| 源文件 | 行数 | 说明 |
|---|---|---|
| `parser.ts` | 9,775 | `Parser`（`createSourceFile`）：递归下降 + 错误恢复 + JSDoc 解析 |
| `factory/nodeFactory.ts` | 6,848 | `NodeFactory`：唯一的 AST 构造入口（缓存、`NodeArray` 复用、parent 处理） |
| `factory/utilities.ts` | 1,611 | factory 辅助（`update*` 例程的共享逻辑） |
| `factory/emitNode.ts` | 337 | 发射期附加信息槽 |
| `factory/nodeTests.ts` | 952 | 节点判定谓词（取代 TS 的 `isXxx` 条件类型） |
| `factory/nodeChildren.ts` | 54 | 子节点访问表 |
| `factory/nodeConverters.ts` / `parenthesizerRules.ts` / `baseNodeFactory.ts` | 179/644/52 | 转换器与括号规则 |
| `factory/emitHelpers.ts` | 1,377 | 生成 `__awaiter`/`__spreadArray` 等 helper |
| `expressionToTypeNode.ts` | 1,281 | 表达式 → 类型节点（用于声明生成与快速修复） |

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
    └─ gen\                       # 从 TS 源码生成/校验枚举与诊断表的脚本
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

- **P1（语法层验收）**：先只比对 `.errors.txt`（语法诊断）与 `.js`，不含类型检查。可在 Layer 3 完成后启动。
- **P2（语义层验收）**：加 `.types` / `.symbols`，依赖 checker。

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
| **M2b** | `sys.ts` + `performance.ts`（Layer 0 中依赖 Layer 1/5 的部分） | 文件系统抽象与计时埋点单测 |
| **M3** | Layer 2：`Scanner` 全量 | 单测：token 流、trivia、错误恢复、关键字表 |
| **M4** | Layer 3：`Parser` + `NodeFactory` | 能对 conformance 用例产出 AST；语法诊断 baseline 对齐 |
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

### 10.2 语义映射（有意为之的等价改写）

- **JS truthiness**：新增 `Js.IsTruthy`。TS 代码里 `if (v)`、`v ? … : …` 大量依赖 truthy，C# 只有 `bool`；凡是原样依赖 truthy 的位置都调用它，而不是偷懒写成 `!= null`。
- **稳定排序**：TS 用 `Array.prototype.sort`（ES2019 起稳定），C# 的 `List.Sort`/`Array.Sort` 是内省排序、**不稳定**。`Core.StableSort` 用原始索引做 tiebreak 来恢复稳定性，所有排序入口（`toSorted`/`sortAndDeduplicate`/`deduplicate`）都经过它。
- **字符串序**：JS 的 `<`/`>` 按 UTF-16 码元比较；C# 的 `string` 没有比较运算符，统一走 `string.CompareOrdinal`。
- **字符串索引越界**：JS 的 `str.charCodeAt(i)` 越界返回 `NaN`，与任何字符码比较都为 `false`；C# 的 `s[i]` 越界**抛异常**。`Paths.CharCodeAt` 把越界读映射为 `-1`（落在 0..0xFFFF 之外，比较结果与 `NaN` 一致）；`path.ts` 的根长度解析（`//`、`c:`、`file:///`）依赖这一点，直接翻译会在 `"/"` 这类输入上崩溃。
- **大小写折叠**：JS 的 `toUpperCase` 是 full case mapping（`"ß"` → `"SS"`），.NET 的 `ToUpperInvariant` 不是。因此 `equateStringsCaseInsensitive("ss", "ß")` 在 TS 中为 `true`、在移植版中为 `false`。影响面仅限含 ß 一类特殊字符的标识符/文件名比较。**已知差异，暂不补偿。**
- **`StartsWith`**：TS 写 `str.lastIndexOf(prefix, 0) === 0`。.NET 的 `LastIndexOf(value, 0, Ordinal)` 在 `value` 长于 `startIndex + 1` 时返回 `-1`（实测 `"./foo".LastIndexOf("./", 0, Ordinal) == -1`），语义不同，因此改写为 `str.StartsWith(prefix, Ordinal)` 并补上 TS `slice` 的长度收敛行为。
- **"返回同一实例"的优化**：`filter`/`compact` 等在 TS 中可能返回入参实例本身。C# 因输入为 `IReadOnlyList<T>`、输出为 `List<T>` 而无法共享实例，这些函数改为返回新列表（`sameMap`/`concatenate`/`compact` 则通过返回 `IReadOnlyList<T>` 保留该契约）。
- **断言分级**：TS 通过把 `Debug` 上的断言函数替换成 `noop` 并缓存来实现分级；C# 不能在运行时替换自身方法，改为每个受分级控制的断言在入口处判断 `ShouldAssert`。调用点契约不变。

### 10.3 有意复刻的上游行为（看起来像 bug，但必须保持一致）

- `relativeComplement` 的内层循环一旦耗尽 `arrayA` 就结束本轮 `offsetB`，所以"大于 `arrayA` 全部剩余元素"的 `arrayB` 元素不会进入结果（例如 `relativeComplement([1,3,5], [1,2,3,4,5,6])` 得到 `[2,4]` 而非 `[2,4,6]`）。上游调用方保证 `arrayA` 覆盖 `arrayB` 的最大值，因此该分支不会被触发；移植版保留该语义。
- `insertSorted` 在"插入位置的前一个元素已相等"时返回 `false` 且不修改数组（只在与后继相等时才替换）。

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
| `sys.ts`: `System` 接口、`sys` 实例、watch 函数 | M2b（依赖 Layer 1 的 `WatchOptions` 与 Layer 5 的 `matchFiles`/`resolveJSModule`） |
| `performance.ts`: mark/measure 埋点 | M2b（依赖 `sys.ts` 的 `System` 实例） |

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

### 11.2 待定（到相应里程碑再决策）

| # | 决策 | 何时需要 | 影响 |
|---|---|---|---|
| D7 | 热路径优化：`Span`/`unsafe`/对象池的引入边界 | M6（checker 可跑之后） | 先正确后快；过早优化会污染移植的可对照性 |

### 11.3 生成器（tools/Gen）

```
dotnet run --project tools/Gen --            # 重新生成，写入仓库
dotnet run --project tools/Gen -- --check    # 只校验不写入（CI / 本地预检）
```

- **输入基准**：`tools/ts-source.txt` 里记录的 checkout（当前为 release-6.0 @ `050880ce59`，版本 6.0.3）。移动基准时先改这里的注释，再重跑生成器。
- **覆盖**：`SyntaxKind`（396 个成员，其中 37 个是哨兵别名）、`NodeFlags`、`ModifierFlags`、`JsxFlags`、`SymbolFlags`、`CheckFlags`、`NodeCheckFlags`、`TypeFlags`、`SignatureFlags`、`TransformFlags`、`EmitFlags`。
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
   - 结果：**268 个类 / 255 个接口**，`CallExpression` 的链解析为 `CallExpression → LeftHandSideExpression → UpdateExpression → UnaryExpression → Expression → Node` ✓
   - 代价：`Identifier` 的 C# 基类因此是 `Node` 而非 `PrimaryExpression`（后者降为接口）。分类接口本身没有实字段（只有 brand），所以类型关系通过接口完整保留，不丢信息。
   - `PunctuationToken`/`KeywordToken`/`ModifierToken`/`NodeArray` 只经**类型别名**到达（`type DotToken = PunctuationToken<SyntaxKind.DotToken>`），位置规则看不到，用显式清单标为类。这也是 token 家族只需要少数几个类、而不是 187 个空类的原因。
   - 泛型参数在比较基名时必须剥离（`PunctuationToken<TKind>` → `PunctuationToken`），否则继承图与角色判定全线失配。

6. **类型模型的输入源是 TypeScript 编译器自己的解析器，不是 C# 侧的行扫描。** 最初生成器用行扫描 + 正则啃 `types.ts`，在修掉五个坑之后仍然是脆弱的：成员省略分号、泛型参数里嵌套尖括号、单行接口、`}` 与下一个声明同行、`const enum` 值跨行。既然 checkout 的 `lib/typescript.js` 就是完整的 6.0.3 编译器，改由 `tools/Gen/extract.cjs`（Node）用它解析并输出 JSON，C# 侧只做反序列化。
   - 提取出的模型与行扫描在**接口数与别名数上完全一致**（523 / 274），但**成员数从 3011 降到 1832** —— 行扫描把 `foo(x): void;` 这类方法签名也当成了数据成员，而 `ts.isPropertySignature` 正确排除了它们。
   - `node` 依赖被限定在生成环节：`src/` 下没有任何东西依赖它，提取出的 JSON 写在 `artifacts/`（不提交）。

---

## 12. M2b 现状（节点模型，进行中）

### 12.1 已完成

| 项 | 结果 |
|---|---|
| 类型模型提取 | `tools/Gen/extract.cjs` + `TsModel`；75 个枚举 / 537 个接口 / 276 个别名（三个源文件合并） |
| 类型映射器 | `TsTypeMapper`：联合降级为共同基类、`~`、泛型、数组、函数类型、`Map`→`Dictionary` 等 |
| 角色分类 | 268 个类（继承链）/ 255 个接口（mixin）；`CallExpression → LeftHandSideExpression → UpdateExpression → UnaryExpression → Expression → Node` |
| 生成物 | `Model/NodeInterfaces.cs`、`Model/NodeClasses.cs`，以及全部 73 个枚举 |
| 一致性校验 | `GeneratedTablesTests` 直接用生成器重跑并逐字节比对（走 TS 解析器路径） |

### 12.2 未完成

**生成的节点层尚未编译通过**（约 50 个 `CS0738`），问题集中在一类：

> TS 允许子接口**收窄**成员类型 —— `FunctionDeclaration.body` 是 `Block`，而 `FunctionLikeDeclarationBase.body` 是 `ConciseBody`。C# 要求实现成员与**每一个**接口精确匹配，于是同一个名字在不同接口上各有一个类型时就没有单一合法答案。

已按「同名成员取最宽类型（多候选时降为 `object?`）」处理，基类链与接口链的组合仍有约 50 处遗漏。

因此在 `TypeScript.Compiler.csproj` 里暂时 `Compile Remove` 了这组文件（`NodeInterfaces.cs`、`NodeClasses.cs`、`Node.cs`、`NodeArray.cs`、`Token.cs`），**仓库保持全绿**。解决后删掉那段 `ItemGroup` 即可启用。
3. **字符串值枚举生成为 `readonly struct` 包装，而不是 C# enum，也不是裸 `const string`。** TS 里有 4 个这样的枚举（`Extension`、`InternalSymbolName`、`PrivateIdentifierKind`、`ForegroundColorEscapeSequences`）。
   - C# enum 不能携带字符串值 —— 无法表示 `"__call"`、`"export="`、`"\u001b[90m"`。
   - 裸 `const string` 会丢掉名义类型，`fileExtensionIs` 这类函数的签名就退化成 `string, string`，没有任何东西能区分 `Extension` 与随手写的字面量。
   - 生成的 struct 同时保留两者：参数与字段可以声明为 `Extension`，底层字符串通过 `.Value` 取出。实现 `IEquatable<T>`、`==`/`!=`、`GetHashCode`（与 `string.GetHashCode(..., Ordinal)` 一致，保证字典键可用）、`ToString()`。
   - **不生成到 `string` 的隐式转换**：包装的意义就是调用点必须显式写 `.Value`，类型信息不在赋值处悄悄丢失。代价是 TS 里「把枚举成员直接当字符串传」的位置（例如把 `readonly Extension[]` 传给接受 `readonly string[]` 的 `getAnyExtensionFromPath`）在 C# 中要逐个 unwrap，`Paths` 里已按此处理并加了 PORT-NOTE。
   - 与之相关：生成器**不照抄**字符串字面量。JS 的 `\x` 是定长两位、`\0`/`\'` 的规则也与 C# 不同，所以字面量先解码成实际字符，再按 C# 规则重新编码；遇到不认识的转义会中止生成而不是猜。
   - `default(T)` 在 C# 里总是存在（TS 没有这个概念），struct 的 `Value` 对 `default` 返回空串而不抛异常，且与任何真实成员都不相等。
4. 另外还有一类**字符串字面量联合类型**（`TypeOfTag`、`BufferEncoding`、`OrganizeImportsTypeOrder`）。它们没有成员名，C# 中直接用 `string` 表示、值在调用点内联；到实际用到时（`TypeOfTag` 在 checker，`BufferEncoding` 在 `sys.ts`）再评估是否需要命名常量。
