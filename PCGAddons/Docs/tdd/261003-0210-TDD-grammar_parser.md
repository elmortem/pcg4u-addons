Status: Готов к реализации

# PCG.Grammar: парсер грамматики и нода Print Grammar

Новый пакет `PCG.Grammar` в PCGAddons: строка грамматики в синтаксисе D-30 (`Docs/Plans/v2/maps/grammar-kit-demo.md` §1.3 в ProjectPCG) разбирается в AST с диагностикой по позиции, считается минимальный размер поддеревьев, проверяется семантика; нода `Print Grammar` показывает разбор в теле ноды. Это G1/1 — чистый C# без зависимости от ядра v2; солвер раскладки и `ModuleSet` — G1/2.

## Пакет `Packages/PCG.Grammar`

По образцу `Packages/PCG.Octree`:

- `package.json`: `name = "com.elmortem.pcg.grammar"`, `displayName = "PCG Grammar"`, `version = "0.1.0"`, `unity = "2022.3"`, `author = "Makar Osokin"`, `description = "Shape-grammar nodes for PCG4U: parse a grammar string and subdivide segments into modules."`, `dependencies = {}` (G1/2 добавит `com.unity.mathematics`).
- `Scripts/PCG.Grammar.asmdef`: `rootNamespace = "PCG.Grammar"`, references `["PCG"]`, `autoReferenced = true`.
- `Editor/PCG.Grammar.Editor.asmdef`: `rootNamespace = "PCG.Grammar.Editors"`, references `["PCG", "PCG.Editors", "PCG.Grammar"]`, `includePlatforms = ["Editor"]`.
- `Tests/Editor/PCG.Grammar.Tests.asmdef`: references `["UnityEngine.TestRunner", "UnityEditor.TestRunner", "PCG.Grammar"]`, `includePlatforms = ["Editor"]`, `defineConstraints = ["UNITY_INCLUDE_TESTS"]`, `autoReferenced = false`.
- `Scripts/PcgLibrary.cs`: `[assembly: PCG.GraphModel.PcgNodeLibrary("com.elmortem.pcg.grammar", "PCG Grammar", Description = "Shape-grammar nodes for PCG4U.", Tags = new[] { "pcg", "grammar", "modules" })]`. `FormatVersion` не задаётся: сборка ядра в `Assets/Plugins/PCG4U/PCG/PCG.dll` (2026-08-08) этого свойства ещё не имеет, а незаданное значение равно 1 (F1 1/4); явное `FormatVersion = 1` — после S-CORE.
- `Third-Party Notices.txt`: пакет не содержит стороннего кода; текст по образцу без разделов зависимостей.
- `Documentation~/Grammar-Addon.md` и `Documentation~/PCG.Grammar/Grammar/Print-Grammar-Node.md` (ASD-STE100, как `Octree-Addon.md`).
- `Packages/manifest.json`: `com.elmortem.pcg.grammar` в `testables` — иначе `agentbridge tests --assembly PCG.Grammar.Tests` тесты не увидит.

## Токенизатор — `Scripts/Parsing/`

```csharp
public enum GrammarTokenKind { Symbol, Number, Comma, Colon, Star, Plus, OpenBracket, CloseBracket, OpenAngle, CloseAngle, OpenBrace, CloseBrace, End, Invalid }
public readonly struct GrammarToken { public readonly GrammarTokenKind Kind; public readonly string Text; public readonly int Position; public readonly int Length; }
public sealed class GrammarError { public int Position; public int Length; public string Message; public override string ToString(); }   // "at 7: expected ']' or ','"
public static class GrammarTokenizer
{
	public static bool Tokenize(string text, List<GrammarToken> tokens, List<GrammarError> errors);   // пробелы, табы, переводы строк пропускаются; SYMBOL [A-Za-z_][A-Za-z0-9_]*; NUMBER — цифры с одной точкой, InvariantCulture; любой другой символ → Invalid с позицией, разбор останавливается
}
```

## AST — `Scripts/Ast/`

Каждый тип в своём файле:

```csharp
public enum GrammarNodeKind { Symbol, Sequence, Repeat, Priority, Choice }
public abstract class GrammarNode { public GrammarNodeKind Kind; public int Position; public int Length; public float MinSize; public abstract void Accept(IGrammarNodeVisitor visitor); }
public sealed class GrammarSymbolNode : GrammarNode { public string Name; }
public sealed class GrammarSequenceNode : GrammarNode { public List<GrammarNode> Items = new(); }
public sealed class GrammarRepeatNode : GrammarNode { public GrammarNode Inner; public int Min; public int Max; public const int Unbounded = -1; }   // '*' → (0, Unbounded), '+' → (1, Unbounded), N → (N, N)
public sealed class GrammarPriorityNode : GrammarNode { public List<GrammarNode> Alternatives = new(); }
public sealed class GrammarChoiceNode : GrammarNode { public List<GrammarNode> Alternatives = new(); public List<float> Weights = new(); }
public interface IGrammarNodeVisitor { void Visit(GrammarSymbolNode n); void Visit(GrammarSequenceNode n); void Visit(GrammarRepeatNode n); void Visit(GrammarPriorityNode n); void Visit(GrammarChoiceNode n); }
```

`Position`/`Length` — диапазон узла в исходной строке (для подсветки ошибок и `Print Grammar`). `MinSize` заполняет `GrammarMinSize` (ниже), до этого `0`.

## Парсер — `Scripts/Parsing/GrammarParser.cs`, `GrammarParseResult.cs`

```csharp
public sealed class GrammarParseResult
{
	public GrammarNode Ast;                                  // null при синтаксической ошибке
	public readonly List<GrammarError> Errors = new();
	public readonly List<string> Symbols = new();            // уникальные имена в порядке первого появления
	public bool Success => Errors.Count == 0;
}
public static class GrammarParser
{
	public static GrammarParseResult Parse(string text);
}
```

Рекурсивный спуск по EBNF §1.3: `grammar := sequence EOF`; `sequence := item {',' item}`; `item := atom [quantifier]`; `atom := SYMBOL | '[' sequence ']' | '<' sequence '>' | '{' weighted {',' weighted} '}'`; `weighted := alternative [':' NUMBER]`; `alternative := SYMBOL | '[' sequence ']' | '<' sequence '>'`; `quantifier := '*' | '+' | INT`. Без исключений: первая синтаксическая ошибка кладётся в `Errors` с позицией токена и перечнем ожидаемого (`expected symbol, '[', '<' or '{'`), `Ast = null`. Правила сверх EBNF: пустая строка или только пробелы — ошибка `empty grammar`; `[]`, `<>`, `{}` — ошибка `empty group`; квантификатор `INT` ≥ 1, иначе `quantifier must be 1 or more`; квантификатор после квантификатора (`A*3`) — ошибка; вес `NUMBER` > 0, иначе `weight must be greater than 0`; вес с дробной частью допустим (`A:0.5`); `<A>` с одной альтернативой и `{A}` допустимы (вырождаются в саму альтернативу без обёртки); `[A]` без квантификатора даёт `Sequence` из одного элемента, который схлопывается в элемент. Группа `[A,B]3` — `Repeat(Sequence)`, `A3` — `Repeat(Symbol)` (расширение D-30).

## Минимальный размер и семантика — `Scripts/Semantics/`

```csharp
public static class GrammarMinSize
{
	public static float Compute(GrammarNode ast, Func<string, float> sizeOf);   // §1.4 шаг 1; заполняет node.MinSize на всём дереве; Repeat с Min = 0 → 0; возвращает MinSize корня
}
public static class GrammarSemantics
{
	public static bool Check(GrammarNode ast, Func<string, bool> isKnown, Func<string, float> sizeOf, List<GrammarError> errors);
}
```

Проверки `Check` (все, не только первая): неизвестный символ — по одной ошибке на имя с позицией первого вхождения `unknown symbol 'X'`; `Repeat` с `Max = Unbounded` над поддеревом с `MinSize ≤ 0` — `infinite repeat: '…' has zero minimum size` (ловит `[A*]*`, `[A*]+` и модуль нулевого размера после G1/2); `isKnown = null` — проверка символов пропускается. `sizeOf` при неизвестном символе возвращает 0.

## Печать — `Scripts/Printing/GrammarPrinter.cs`

```csharp
public static class GrammarPrinter
{
	public static string Print(GrammarNode ast);        // канонический текст: "P,[A,P]*", веса "{A:2,B:1}", повтор N — "[..]3"; Parse(Print(Parse(s))) структурно равен Parse(s)
	public static string PrintTree(GrammarNode ast);    // по строке на узел, отступ два пробела: для `P,[A,P]*` — "Seq min=1", "  Symbol P", "  Repeat 0..∞ min=0", "    Seq min=2", "      Symbol A", "      Symbol P"; у Choice — "Choice" и "    [w=2] Seq …"
	public static bool StructurallyEqual(GrammarNode a, GrammarNode b);
}
```

## Нода `Print Grammar`

`Scripts/Nodes/PrintGrammarNode.cs`:

```csharp
[Serializable]
[PcgNodeInfo("Parses a grammar string and shows the parse tree, the minimum module count and the errors.", DisplayName = "Print Grammar", Category = "Grammar", Tags = new[] { "grammar", "debug", "print" })]
public class PrintGrammarNode : PcgNode
{
	[PcgMemberInfo("Grammar string. Example: P,[A,P]*", Tags = new[] { "grammar", "string" })]
	public string Grammar = "P,[A,P]*";
}
```

`Editor/PrintGrammarNodeExecutor.cs` — `PcgSyncNodeExecutor<PrintGrammarNode>, INodeInfo`. Портов нет; `IsEmpty => _result == null`; `CanCache => false`. Разбор — `Refresh()`: `GrammarParser.Parse(Data.Grammar)`, при успехе `GrammarMinSize.Compute(ast, _ => 1f)` (размер модуля 1 → минимальное число модулей; метрический размер появится с `ModuleSet` в G1/2) и `GrammarSemantics.Check(ast, null, _ => 1f, errors)`. `Refresh()` вызывается из `OnBind()`, `OnParametersChanged()` (после `base`) и `DoCompute()`; `DoCompute` дополнительно пишет `Debug.Log` с `PrintTree` или ошибками (имя ноды в префиксе). `HasNodeInfo => _result != null`; `NodeInfo`: первая строка `OK · min modules N · symbols A, B, P` или `Error at 7: expected ']' or ','`, далее строки `PrintTree` (не более 16, затем `…`). Строка с ошибкой дополняется фрагментом `Grammar` с маркером `^` под позицией. Поле `Grammar` на ноде правится штатным рендерером (`PcgNodeBodyRenderer` → `ApplyModifiedProperties` → `OnParametersChanged`).

## Тесты — `Tests/Editor/`

- `GrammarTokenizerTests`: символы с цифрами и подчёркиванием; числа `2`, `0.5`; пробелы и переводы строк; `Invalid` на `A;B` с позицией 1.
- `GrammarParserTests` (24 строки, каждая — `[TestCase]`): `A`; `A,B`; `A*`; `A+`; `A3`; `[A,B]`; `[A,B]3`; `[A,B]*`; `[A,B]+`; `<A,B,C>`; `{[A,P]:2,[BL,P]:1}*`; `P,[A,P]*`; `[<A,B>,P]3`; `{A:0.5,B}`; `G,[F]*,R`; вложенность трёх уровней; пробелы `P , [ A , P ] *`. Ошибки с ожидаемой позицией: `` (пусто), `[]`, `<>`, `{}`, `[A,B`, `A,,B`, `A*3`, `[A]0`, `{A:0}`, `{A:-1}`, `A B`, `A;B`. Проверки: `Kind` и структура, `Min/Max`, веса, `Symbols` без дублей в порядке появления, `Position/Length` корня и первого вложенного узла.
- `GrammarMinSizeTests`: `A` = 1; `A,B` = 2; `A*` = 0; `A+` = 1; `[A,B]3` = 6; `<A,[B,C]>` = 1; `{A,[B,C]}` = 1; с `sizeOf` из словаря (`A = 2.5`, `B = 1`): `[A,B]2` = 7.
- `GrammarSemanticsTests`: неизвестный символ один раз с позицией первого вхождения; два неизвестных — две ошибки; `[A*]*` и `[A*]+` — `infinite repeat`; `A*` — без ошибок; `isKnown = null` пропускает символы; модуль размера 0 под `*` — ошибка.
- `GrammarPrinterTests`: `Print` round-trip на всех валидных строках списка парсера (`StructurallyEqual(Parse(s), Parse(Print(Parse(s))))`); `PrintTree` для `P,[A,P]*` — ожидаемые шесть строк.

## Порядок работ

- Пакет: `package.json`, три asmdef, `PcgLibrary.cs`, `Third-Party Notices.txt`, `testables`; `agentbridge compile`.
- `GrammarTokenKind`, `GrammarToken`, `GrammarError`, `GrammarTokenizer`; `GrammarTokenizerTests`.
- AST (`GrammarNodeKind`, `GrammarNode`, пять узлов, `IGrammarNodeVisitor`).
- `GrammarParser`, `GrammarParseResult`; `GrammarParserTests`.
- `GrammarMinSize`, `GrammarSemantics`; тесты.
- `GrammarPrinter`; `GrammarPrinterTests`.
- `PrintGrammarNode`, `PrintGrammarNodeExecutor`; приёмка в окне графа.
- Документация.

## Приёмка

- `agentbridge tests --mode EditMode --assembly PCG.Grammar.Tests` зелёный, не меньше 40 тестов.
- Сцена `SampleScene`: нода `Print Grammar` добавляется из поиска (категория `Grammar`); с полем по умолчанию в теле ноды `OK · min modules 1 · symbols P, A` и дерево; ввод `P,[A,P` даёт красную строку с позицией и маркером `^`; ввод `[A*]*` — `infinite repeat`; окно не пишет исключений в консоль.
- Граф с нодой сохраняется и открывается заново с тем же разбором (`OnBind`).

## Документация

- `Docs/GRAMMAR_MAP.md` (по образцу `OCTREE_MAP.md`): структура пакета, синтаксис D-30 с EBNF, таблица нод (`Print Grammar`), контракт парсера (`GrammarParseResult`, правила ошибок), план G1/2–G1/4 одной строкой.
- `Docs/PROJECT_MAP.md`: `PCG.Grammar` в дереве папок, в таблице аддонов и в таблице пакетов (`com.elmortem.pcg.grammar`).
- `Documentation~/Grammar-Addon.md`, `Documentation~/PCG.Grammar/Grammar/Print-Grammar-Node.md` — ASD-STE100: синтаксис с примерами, что показывает нода.
- `Docs/Plans/v2/maps/grammar-kit-demo.md` (ProjectPCG): «Изменения после 2026-10-01» — пакет создан, парсер и `Print Grammar` готовы, `FormatVersion` не задан до S-CORE.
