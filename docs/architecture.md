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
| `performance.ts` / `performanceCore.ts` | 189/86 | `Core/` | 计时埋点，C# 用 `Stopwatch`/`Activity` 承接 |
| `path.ts` | 1,024 | `Core/` | 路径规范化；C# 对照 `Path` 但**必须复刻 TS 语义**（大小写、`/` 分隔符） |
| `sys.ts` | 1,826 | `Core/` | `System` 接口：文件读写、目录、环境、`CompilerHost` 的 OS 实现 |
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
| **M2** | Layer 1：`SyntaxKind` 全量 + 标志 + `Node` 基类骨架 | 枚举值与 TS 逐项校验（脚本比对） |
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
