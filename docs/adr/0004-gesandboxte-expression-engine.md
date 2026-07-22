# ADR 0004 – Gesandboxte Expression-Engine hinter einer Abstraktion

- **Status:** Akzeptiert
- **Kontext-Issue:** #22 – `IExpressionEvaluator` + Kontext-Modell / #23 – DynamicExpresso-Implementierung
  (Sandbox); erweitert um #24 (Compile-Check) und #34 (Austausch per Option)
- **Betroffen:** `src/Flirty/Expressions/`, `src/Flirty/Runtime/` (Transitionen, Trigger), `src/Flirty.Designer`

## Kontext

Branching und Trigger hängen an booleschen Bedingungsausdrücken (`Transition.Expression`,
`TriggerDefinition.Expression`) wie `age > 18` oder `positions.Count > 0`. Diese Ausdrücke werden im
**Designer** von Anwendern geschrieben und **in der Datenbank** abgelegt – sie sind also *Daten*, nicht
Code, und werden zur Laufzeit gegen die Antworten einer laufenden Session ausgewertet.

Damit ist die Auswertung die **sicherheitskritische Stelle** der Engine: Was hier ausgeführt werden
kann, kann jeder ausführen, der Schreibzugriff auf die Dialog-Konfiguration hat. Zwei weitere
Anforderungen kommen dazu: Der Designer muss einen Ausdruck **beim Speichern** prüfen können (ohne
ihn auszuführen – es gibt zu diesem Zeitpunkt keine Session), und die Werte liegen als **roher
JSON-Text** in `SessionAnswer.Value`, sind also erst zu typisieren.

## Entscheidung

Die Auswertung läuft ausschließlich über die Abstraktion `IExpressionEvaluator`
(`src/Flirty/Expressions/IExpressionEvaluator.cs`) mit zwei Zusagen:

- `Evaluate(expression, context)` – wertet aus und ist **fail-loud** (`ExpressionEvaluationException`),
- `Validate(expression, context)` – **kompiliert nur, führt nicht aus**, und meldet Fehler als
  Ergebnis (`ExpressionValidationResult` mit Meldung und Position) statt per Ausnahme.

Default-Implementierung ist `DynamicExpressoExpressionEvaluator` auf Basis von
[DynamicExpresso](https://github.com/dynamicexpresso/DynamicExpresso), **gesandboxt**:

- Interpreter-Optionen strikt `PrimitiveTypes | SystemKeywords` – **ohne** `CommonTypes`
  (kein `Math`, `Convert`, `Enumerable`),
- **keine** Reflection (`EnableReflection` wird nicht aufgerufen), **keine** Zuweisungen
  (`EnableAssignment(AssignmentOperators.None)`),
- erreichbar sind damit **nur** die injizierten Kontext-Variablen und deren Instanz-Member;
  nicht gewhitelistete Typen wie `System.IO.File` existieren für den Ausdruck nicht.

Der `ExpressionContext` wird auf **flache Top-Level-Bezeichner** gebunden (je `Question.Key`, je
`CollectionKey`, dazu `now`/`iterationIndex`/`session`) – passend zu Ausdrücken, die Anwender
schreiben. Auswertung und Compile-Check nutzen **denselben** Interpreter-Aufbau; ein Ausdruck, den
der Designer akzeptiert, ist damit derselbe, den die Runtime sieht.

## Verworfene Alternativen

- **Roslyn-Scripting / roher C#-`eval`.** Volle BCL auf einer aus der Datenbank gelesenen
  Zeichenkette – das ist Remote Code Execution mit Extraschritten. Dazu spürbare Compile-Kosten je
  Ausdruck. Ausgeschlossen, unabhängig vom Komfortgewinn.
- **NCalc.** Funktional tragfähig und ebenfalls gesandboxt, aber schwächer beim Zugriff auf
  .NET-Typen und Instanz-Member (`positions.Count`, `session.StartedAt`), was das Kontext-Modell
  verbogen hätte. Bleibt über die Abstraktion jederzeit als Ersatz wählbar – genau dafür ist sie da.
- **Eigene Mini-Grammatik.** Maximal sicher und maximal teuer: Parser, Fehlermeldungen mit Position,
  Typregeln und Dokumentation wären Eigenbau – und für Anwender wäre die Syntax fremd.
- **Bedingungen als Code in der Host-App** (Delegates statt Ausdrücke). Sicher, aber widerspricht dem
  Ziel, Dialoge im Designer **ohne Deployment** zu ändern; jede neue Verzweigung wäre ein Release.

## Konsequenzen

**Positiv**

- Die Sandbox ist eine **Positivliste**: Was nicht gebunden wurde, ist nicht erreichbar. Das ist
  prüfbar und bleibt es auch, wenn der Ausdrucksvorrat wächst.
- `Validate` erlaubt dem Designer die Prüfung **beim Speichern** – Fehler entstehen beim Konfigurieren,
  nicht mitten in der Session eines Anwenders.
- Der Evaluator ist zustandslos (je Auswertung ein frischer Interpreter) → als **Singleton**
  registriert; austauschbar über `o.UseExpressionEvaluator<T>()`.

**Negativ**

- Der Sprachumfang ist bewusst klein. Kein `Math`, kein LINQ – wächst der Bedarf, ist das eine
  bewusste Erweiterung der Whitelist, kein Aufdrehen von `CommonTypes`.
- Werte sind roher JSON-Text und werden von der Engine typisiert. Ein **Datum ist deshalb eine
  Zeichenkette** – `geburtstag < now` scheitert zu Recht. Das ist der häufigste Stolperstein und
  gehört in jede Designer-Hilfe.
- Der Designer muss den Laufzeitkontext **spiegeln**, weil er keine Session hat:
  `DesignerExpressionContext` (Musterkontext, #40) und `RunExpressionContext` (echte Bindungen im
  Testlauf, #43). Beide Spiegel haben Vergleichstests gegen ihr Core-Original – Änderungen am
  `SessionExpressionContextBuilder` oder am `LoopResolver` sind dort mitzuziehen.
- Ein eigener Evaluator muss **beide** Zusagen einhalten (Evaluate fail-loud, Validate nur
  kompilieren); das Kurzschließen `null`er/leerer Ausdrücke bleibt Aufgabe der Runtime.

Details: [BRANCHING-EXPRESSIONS.md](../BRANCHING-EXPRESSIONS.md), Designer-Seite in
[DESIGNER.md](../DESIGNER.md).
