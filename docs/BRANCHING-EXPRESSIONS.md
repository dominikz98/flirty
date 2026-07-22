# Branching & Expressions: Expression-Engine

Wie Flirty Bedingungsausdrücke des Branchings auswertet – die Abstraktion `IExpressionEvaluator`
und das Kontext-Modell `ExpressionContext`. Umgesetzt in Issue **#22** (EPIC 2). Referenz:
[ARCHITECTURE.md](./ARCHITECTURE.md) §7/§10/§11, Modell-Details in [DOMAIN-MODEL.md](./DOMAIN-MODEL.md).

## Überblick

Verzweigungen (Branching) und Schleifen (Loops) hängen an **booleschen Bedingungsausdrücken**:

- `Transition.Expression` – entscheidet, welcher Übergang von einer Frage greift.
- `TriggerDefinition.Expression` – entscheidet, ob ein Trigger auslöst.

Ausgewertet werden diese Ausdrücke über die austauschbare Engine `IExpressionEvaluator`
(Namespace `Flirty.Expressions`). #22 legt nur die Abstraktion fest; die gesandboxte Default-Engine
(#23), der Compile-Check für den Designer (#24) und die DI-Integration (#34) sind unten in eigenen
Abschnitten beschrieben.

## `IExpressionEvaluator`

```csharp
namespace Flirty.Expressions;

public interface IExpressionEvaluator
{
    bool Evaluate(string expression, ExpressionContext context);
}
```

- **Synchron:** Die Auswertung ist eine reine In-Memory-Operation (die Default-Engine DynamicExpresso
  arbeitet synchron) – daher kein `async`/`CancellationToken`.
- **Null/leerer Ausdruck:** Ein `null`er oder leerer `Expression` gilt fachlich als
  *bedingungslos zutreffend*. Diese Kurzschluss-Behandlung liegt bei der **Runtime**, nicht beim
  Evaluator; Implementierungen dürfen einen nicht-leeren Ausdruck erwarten.

## Kontext-Modell `ExpressionContext`

Der unveränderliche `ExpressionContext` bündelt die zum Auswertungszeitpunkt sichtbaren Daten einer
laufenden Session. Er bildet die fünf in ARCHITECTURE §7 genannten Bausteine ab:

| Baustein | Property | Typ | Bedeutung |
|---|---|---|---|
| `answers` | `Answers` | `IReadOnlyDictionary<string, string?>` | Antworten indiziert nach `Question.Key` |
| Loop-Collections | `Collections` | `IReadOnlyDictionary<string, IReadOnlyList<string?>>` | je Iteration gesammelte Antworten, indiziert nach `LoopDefinition.CollectionKey` |
| `iterationIndex` | `IterationIndex` | `int?` | nullbasierter Iterationsindex, `null` außerhalb einer Schleife |
| `now` | `Now` | `DateTimeOffset` | Auswertungszeitpunkt |
| `session` | `Session` | `DialogSession` | die laufende Session |

Die Werte sind **roher JSON-Text** – exakt wie in `SessionAnswer.Value` gespeichert (Format
abhängig vom Fragetyp). Die typisierte Deserialisierung (z. B. `"42"` → `int`) übernimmt die konkrete
Engine (#23); das Kontext-Modell selbst bleibt bewusst untypisiert.

Nicht angegebene Sammlungen werden als **leere, nicht-`null`e** Sammlungen initialisiert; `Session`
ist Pflicht (Guard via `ArgumentNullException`).

```csharp
var context = new ExpressionContext(
    session,
    now: DateTimeOffset.UtcNow,
    answers: new Dictionary<string, string?> { ["age"] = "42" },
    collections: new Dictionary<string, IReadOnlyList<string?>>
    {
        ["positions"] = ["{\"title\":\"Dev\"}", "{\"title\":\"Lead\"}"],
    },
    iterationIndex: null);
```

## Beispiel-Ausdrücke

Die Default-Engine wertet Ausdrücke wie diese aus (vgl. ARCHITECTURE §10):

```text
age > 18                 // Verzweigung nach numerischer Antwort
positions.Count > 0      // Break-Bedingung einer Schleife über die gesammelte Collection
```

## Default-Engine: `DynamicExpressoExpressionEvaluator` (#23)

Die gesandboxte Default-Implementierung von `IExpressionEvaluator` (Namespace `Flirty.Expressions`)
setzt auf [DynamicExpresso](https://github.com/dynamicexpresso/DynamicExpresso) auf. Sie ist
**synchron** und **zustandslos** (je Auswertung ein frischer, isolierter Interpreter) und damit als
Singleton nutzbar.

### Verfügbare Ausdrucks-Variablen

Der Kontext wird auf flache Top-Level-Bezeichner abgebildet – passend zu Ausdrücken wie `age > 18`:

| Bezeichner | Quelle | Typisierung |
|---|---|---|
| je `Question.Key` (z. B. `age`, `name`) | `Answers` | aus JSON deserialisiert |
| je `CollectionKey` (z. B. `positions`) | `Collections` | Liste der Iterationswerte (`.Count`, Elementzugriff) |
| `now` | `Now` | `DateTimeOffset` |
| `iterationIndex` | `IterationIndex` | `int?` |
| `session` | `Session` | `DialogSession` |

Reservierte Bezeichner (`now`, `iterationIndex`, `session`) werden zuletzt gesetzt und können daher
nicht von gleichnamigen Antwort-/Collection-Schlüsseln verdeckt werden.

### Typisierte Deserialisierung

Die roh als JSON-Text vorliegenden Werte werden typisiert übernommen: JSON-Zahl → `long`/`double`,
JSON-String → `string`, `true`/`false` → `bool`, Array → Liste, Objekt → Dictionary. Ist ein Wert
kein gültiges JSON (z. B. ein unquotierter Auswahl-Schlüssel), wird er unverändert als Zeichenkette
verwendet. Dadurch werten `age > 18` (bei `"42"`) und `name == "Ada"` (bei `"\"Ada\""`) korrekt aus.

### Sandbox (Member-Whitelist, kein roher C#-`eval`)

- Interpreter-Optionen strikt auf `PrimitiveTypes | SystemKeywords` beschränkt: Literale, Vergleichs-,
  Arithmetik- und UND/ODER-Operatoren. `CommonTypes` (`Math`, `Convert`, `Enumerable`) ist **nicht**
  aktiviert.
- **Reflection bleibt blockiert** (kein `EnableReflection`), **Zuweisungen sind deaktiviert**
  (`EnableAssignment(AssignmentOperators.None)`). Zugreifbar sind nur die injizierten Variablen und
  deren Instanz-Member. Nicht gewhitelistete Typen (z. B. `System.IO.File`) sind unerreichbar.
- **Fail-loud:** Syntaxfehler, unbekannte Bezeichner, Sandbox-Verletzungen und nicht-boolesche
  Ergebnisse werfen eine `ExpressionEvaluationException` (kapselt die Engine-Ursache in
  `InnerException`, hält die Engine austauschbar). Der *Compile-Check* beim Speichern (siehe
  [Validierung / Compile-Check](#validierung--compile-check-24)) nutzt dieselbe Sandbox, meldet Fehler
  aber als Ergebnis statt per Exception; das Kurzschließen `null`er/leerer Ausdrücke bleibt Aufgabe der
  Runtime.

## Validierung / Compile-Check (#24)

Für den Designer stellt die Engine neben `Evaluate` einen **Compile-Check** bereit: `Validate`
**kompiliert** einen Ausdruck (DynamicExpresso `Parse`), **führt ihn aber nicht aus**. So lassen sich
Ausdrücke bereits **beim Speichern** prüfen und Fehler melden – ohne Exception, mit strukturiertem
Ergebnis.

```csharp
public interface IExpressionEvaluator
{
    bool Evaluate(string expression, ExpressionContext context);

    // kompiliert, führt nicht aus:
    ExpressionValidationResult Validate(string expression, ExpressionContext context);
}
```

`ExpressionValidationResult` trägt:

| Property | Typ | Bedeutung |
|---|---|---|
| `IsValid` | `bool` | ob der Ausdruck kompilierbar ist |
| `Error` | `string?` | menschlesbare Fehlermeldung (`null`, wenn gültig) |
| `ErrorPosition` | `int?` | nullbasierte Fehlerposition im Ausdruck (soweit gemeldet), z. B. zum Unterstreichen im Designer |

Der übergebene `ExpressionContext` liefert die verfügbaren Variablen (und deren Typen) für die Prüfung –
die Validierung nutzt **dieselbe Sandbox und Variablen-Bindung wie `Evaluate`**. Erkannt werden damit:

- **Syntaxfehler** und ungültige Operator-Verwendung,
- **unbekannte Bezeichner** (Variablen, die der Kontext nicht kennt),
- **Injection-/Sandbox-Verletzungen** (Reflection, nicht gewhitelistete Typen wie `System.IO.File`),
- ein **nicht-boolesches** Ergebnis.

Verhalten:

- Ein `null`er/leerer Ausdruck gilt als **gültig** („bedingungslos zutreffend", konsistent zur Runtime).
- `Validate` **wirft nie** für einen fehlerhaften Ausdruck – Fehler landen im Ergebnis. Einzige Ausnahme:
  ein `null`er Kontext (`ArgumentNullException`).

```csharp
// Designer beim Speichern:
var result = evaluator.Validate(transition.Expression, context);
if (!result.IsValid)
{
    ShowError(result.Error, result.ErrorPosition);
}
```

### Musterkontext im Designer (#40)

Der Designer hat beim Bearbeiten eines Übergangs **keine laufende Session** – der Kontext für `Validate`
wird deshalb aus dem Dialog-Graphen gebaut (`Flirty.Designer/Services/DesignerExpressionContext.cs`).
Entscheidend sind dabei die **Typen**, nicht die Werte: Der Beispielwert je Frage ist derselbe rohe
JSON-Text, den die Laufzeit in `SessionAnswer.Value` ablegt, und wird von der Engine identisch
deserialisiert (`FreeText → "Text"`, `Number → 0`, `Boolean → true`, `Date → "2026-01-01"`,
`SingleChoice →` erster Optionswert, `MultiChoice →` Array der Optionswerte). Eine **Datumsantwort ist
damit auch im Designer eine Zeichenkette** – `geburtstag < now` wird zu Recht abgelehnt, weil es zur
Laufzeit ebenfalls scheitern würde.

Loop-Collections werden wie vom `LoopResolver` **stets** gebunden (vor der ersten Iteration als leere
Liste), damit `skills.Count > 0` prüfbar bleibt; die dafür nötigen `CollectionKey`s liefert
`GetDialogQuery` seit #40 lesend mit (`DialogDetail.Loops`). Schlüssel, die keine gültigen Bezeichner
sind oder von `now`/`iterationIndex`/`session` verdeckt werden, bindet der Designer nicht und weist sie
in seiner Bezeichner-Referenz als nicht nutzbar aus.

Für **Zeichenketten-Literale** gilt: Die Engine parst C#-Escapes (`\"`, `\\`, `\n`, …), aber **keine**
`\u00XX`-Escapes. Ein per `JsonSerializer` quotierter Wert ist deshalb nicht zwangsläufig ein gültiges
Ausdrucks-Literal – dessen Encoder schreibt ein Anführungszeichen als Unicode-Escape, was die Engine mit
„Invalid character escape sequence" ablehnt.

### Echte Bindungen im Testlauf (#43)

Der Musterkontext beantwortet „ist der Ausdruck **kompilierbar**?". Ob er auch das **Richtige** trifft,
zeigt der [Test-Runner](./DESIGNER.md#test-runner-43): Er stellt zu jedem Schritt eines echten Laufs die
tatsächlichen Bindungen dar (`Flirty.Designer/Services/RunExpressionContext.cs`) – Antworten je
Frage-Schlüssel, gesammelte Loop-Collections und den `iterationIndex`. Auch das ist ein **Spiegel** des
Core-internen `SessionExpressionContextBuilder` (der arbeitet auf einer `Dialog`-Entity mit geladenen
Navigationen, der Designer nur auf navigationsfreien Sichten); ein Test vergleicht beide an jedem Schritt
eines Durchlaufs. Wird am `SessionExpressionContextBuilder` oder am `LoopResolver` etwas geändert, ist
dieser Spiegel – wie `DesignerExpressionContext` und `LoopAnalyzer` – mitzuziehen.

## Runtime-Konsum (#26)

Erster Laufzeit-Konsument der Engine ist der `SubmitAnswerCommand`-Handler (#26, siehe
[RUNTIME.md](./RUNTIME.md#submitanswercommand)): Nach dem Persistieren einer Antwort wertet er die
ausgehenden `Transition`s der Frage nach `Priority` aus und wählt die erste zutreffende (sonst den
`IsDefault`-Übergang). Dabei liegt – wie oben beschrieben – das **Kurzschließen** eines `null`en/leeren
`Expression` (bedingungslos zutreffend) bei der Runtime, nicht beim Evaluator. Der `ExpressionContext`
wird aus den bisherigen `SessionAnswer`s gebildet (je Frage die zuletzt gegebene Antwort, indiziert
nach `Question.Key`); seit #26 ist die Default-Engine in `AddFlirty()` als Singleton registriert.

Seit der **Loop-Runtime (#29)** befüllt der geteilte `TransitionResolver` zusätzlich die beiden
Loop-Bausteine des Kontexts: `Collections` trägt je `CollectionKey` die Einstiegsantwort je Iteration
(jeder `CollectionKey` wird stets gebunden – ggf. leere Liste –, damit `positions.Count > 0` auch vor der
ersten Iteration auswertbar ist), und `iterationIndex` reflektiert die aktuelle Iteration der gerade
beantworteten Frage (`null` außerhalb einer Schleife). Details in [LOOPS.md](./LOOPS.md).

## DI-Integration & Austausch (#34)

`AddFlirty()` registriert die Default-Engine seit #26 als **Singleton** (sie ist zustandslos). Wer eine
andere Engine will – z. B. NCalc –, implementiert `IExpressionEvaluator` und ersetzt die
Default-Registrierung:

```csharp
services.AddFlirty(o =>
{
    o.UseSqlite(connectionString);
    o.UseExpressionEvaluator<MyEvaluator>();   // ersetzt DynamicExpressoExpressionEvaluator
});
```

Der eigene Typ wird ebenfalls als Singleton registriert. Zu erfüllen sind beide Zusagen dieses
Dokuments: `Evaluate` wirft bei nicht auswertbaren Ausdrücken (fail-loud), `Validate` **kompiliert nur**
und meldet Fehler als Ergebnis. Das Kurzschließen `null`er/leerer Ausdrücke bleibt Aufgabe der Runtime –
eine eigene Engine darf einen nicht-leeren Ausdruck erwarten.
