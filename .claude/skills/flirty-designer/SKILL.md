---
name: flirty-designer
description: Den Blazor-Designer (Flirty.Designer) aufbauen oder erweitern – Dialog-/Frage-/Antwort-/Branching-/Loop-/Trigger-Konfiguration, Multi-DB-Connection-Profile. Verwenden bei "Designer", "Blazor-UI für Dialoge", "Dialog-Editor", "Branching-Editor", "Connection-Profil", "EPIC 7", Issues #37–#43.
---

# Blazor-Designer aufbauen / erweitern

> **Status: teils umgesetzt (EPIC 7, Issues #37–#43).** Fertig sind die
> **Connection-Profil-Verwaltung (#37)**, das **Dialog-CRUD (#38)**, der **Frage-Editor (#39)**, der
> **Branching-Editor (#40)** und der **Loop-Editor (#41)**; `docs/DESIGNER.md` beschreibt alle fünf.
> Der **Trigger-Editor (#42)** und der **Test-Runner (#43)** sind **noch offen**.
> Dieser Skill ist die **Leitplanke** für den weiteren
> Aufbau: die beabsichtigte Architektur und die Konventionen, an die man sich beim Implementieren halten
> soll. Referenz: `docs/DESIGNER.md`, `docs/ARCHITECTURE.md` §4/§8/§10, `docs/BACKLOG.md` EPIC 7.

## Ist-Zustand (verifiziert)

- `src/Flirty.Designer/Flirty.Designer.csproj`: `Microsoft.NET.Sdk.Web`, referenziert `..\Flirty` **und
  alle drei** `..\Flirty.Migrations.{Sqlite,PostgreSql,SqlServer}` (für Multi-DB-Migrate), plus
  `InternalsVisibleTo("Flirty.Tests")`; `BlazorDisableThrowNavigationException=true`.
- `Program.cs`: `AddRazorComponents().AddInteractiveServerComponents()` +
  `MapRazorComponents<App>().AddInteractiveServerRenderMode()` → **Blazor Web App, Server-interaktiv**.
  Ruft seit #37 **`AddFlirty()` (parameterlos)** auf; der `FlirtyDbContext` wird pro aktivem
  Connection-Profil über `FlirtyDesignerDbContextFactory : IDbContextFactory<FlirtyDbContext>` erzeugt.
- **Connection-Profile (#37):** `Models/ConnectionProfile.cs`, `Services/IConnectionProfileStore` +
  `JsonConnectionProfileStore` (JSON im ContentRoot, gitignored), `ActiveConnectionProfile` (Scoped,
  mit `Activate`/`Adopt`), `ConnectionProfileOperations` (Test-Connection/Migrate),
  `ConnectionProfileContextBuilder`; UI unter `Components/Pages/ConnectionProfiles.razor`
  (`/connections`) + `Components/Layout/NavMenu.razor`.
- **Dialog-CRUD (#38):** `Services/FlirtyAdminGateway.cs` (+ `AdminResult<T>`),
  `Models/DialogFormModel.cs`, Seiten `Components/Pages/Dialogs.razor` (`/dialogs`) und
  `Components/Pages/DialogEditor.razor` (`/dialogs/{id:guid}`). Gemeinsame UI-Klassen (`.editor`,
  `.field`, `.input`, `.btn`, `.data-table`, `.badge`, `.msg`, `.banner`, `.empty`, `.back`, `.confirm`,
  `h1 .badge`) liegen **global** in `wwwroot/app.css`; `*.razor.css` enthält nur Seitenspezifisches.
- **Frage-Editor (#39):** `Models/QuestionFormModel.cs` (Metadaten + Regel-JSON ⇄ Eingabefelder, mit
  Roh-JSON-Fallback), `Models/AnswerOptionFormModel.cs`, `Models/QuestionTypeLabels.cs` (deutsche
  Typnamen, `UsesOptions`), Seite `Components/Pages/QuestionEditor.razor`
  (`/dialogs/{dialogId:guid}/questions/{questionId:guid}`) und der Abschnitt „Fragen" in
  `DialogEditor.razor` (Liste, Inline-Anlegen, ↑/↓, Löschen).
- **Branching-Editor (#40):** `Models/TransitionFormModel.cs`, `Models/ExpressionVariable.cs`,
  `Services/DesignerExpressionContext.cs` (Musterkontext + Bezeichner-Referenz + Baustein-Einfüger),
  Seite `Components/Pages/TransitionEditor.razor`
  (`/dialogs/{dialogId:guid}/transitions/{transitionId:guid}`) und der Abschnitt „Übergänge (Branching)"
  in `DialogEditor.razor` (je Ausgangsfrage gruppiert, ↑/↓, Warnungen, Inline-Anlegen). Dafür liefert der
  Core `DialogDetail.Loops` (`LoopDetail`) mit.
- **Loop-Editor (#41):** `Models/LoopFormModel.cs`, `Models/LoopInsight.cs`, `Services/LoopAnalyzer.cs`
  (Bereichsermittlung + Warnungen), Seite `Components/Pages/LoopEditor.razor`
  (`/dialogs/{dialogId:guid}/loops/{loopId:guid}`) und der Abschnitt „Schleifen (Loops)" in
  `DialogEditor.razor` (Liste, Inline-Anlegen, Vorschläge aus unmarkierten Rücksprüngen). Dafür kam im
  Core das **Loop-CRUD** dazu (`Create/Update/DeleteLoopCommand`, `IDialogAdminStore.GetLoopAsync` /
  `LoopCollectionKeyExistsAsync` / `GetLoopsReferencingQuestionAsync`) sowie in `Flirty.AspNetCore`
  `Dtos/Admin/LoopDtos.cs`, die `.../loops`-Endpunkte und `Loops` in `DialogDetailResponse`.
- **Tests:** `tests/Flirty.Tests/Designer/` (`JsonConnectionProfileStoreTests`,
  `ConnectionProfileOperationsTests`, `FlirtyAdminGatewayTests`, `QuestionFormModelTests`,
  `DesignerExpressionContextTests`, `LoopAnalyzerTests`).

## Leitplanken für die Umsetzung

1. **Über die Engine arbeiten, nicht am DbContext vorbei.** Der Designer nutzt die vorhandenen
   Admin-Commands/Queries über `ISender` – **nicht** direkt `FlirtyDbContext` oder `IDialogAdminStore`.
   Vorhanden in `src/Flirty/Runtime/Admin/`:
   - Dialoge: `ListDialogsQuery`, `GetDialogQuery`, `CreateDialogCommand`, `UpdateDialogCommand`,
     `DeleteDialogCommand`, `PublishDialogCommand`, `UnpublishDialogCommand`.
   - Fragen: `Create/Update/DeleteQuestionCommand`. Optionen: `Create/Update/DeleteAnswerOptionCommand`.
     Übergänge (Branching): `Create/Update/DeleteTransitionCommand`.
   - Sichten (navigationsfrei) in `AdminModels.cs`: `DialogSummary`, `DialogDetail`, `QuestionDetail`,
     `AnswerOptionDetail`, `TransitionDetail`.
   - DI: `AddFlirty(...)` registriert `IDialogAdminStore`; im `Program.cs` des Designers ergänzen
     (inkl. Provider-Wahl je Connection-Profil).

   **Konkret seit #38: immer über `FlirtyAdminGateway`, nie `@inject ISender`.**
   ```csharp
   var result = await Admin.ExecuteAsync((sender, token) => sender.Send(new ListDialogsQuery(), token));
   if (!result.Success) { _error = result.Error; return; }
   ```
   Das Gateway öffnet je Operation einen frischen DI-Scope (in Blazor Server lebt ein Scope sonst den
   ganzen Circuit → der `FlirtyDbContext` bliebe an das zuerst benutzte Profil gepinnt, der
   Change-Tracker liefe voll, und der nicht threadsichere Kontext würde geteilt) und liefert ein
   `AdminResult<T>` mit deutscher Fehlermeldung statt einer Ausnahme, die den Circuit killt.

2. **Multi-DB per Connection-Profil (#37) — UMGESETZT.** Provider + ConnectionString als Profile lokal
   verwaltet; zur Laufzeit über `IDbContextFactory<FlirtyDbContext>` (Impl. `FlirtyDesignerDbContextFactory`)
   gegen das aktive Profil geöffnet. Das Provider→`MigrationsAssembly`-Mapping liefert das öffentliche
   Core-API `FlirtyDatabaseProvider` + `DbContextOptionsBuilder.UseFlirtyProvider(...)` (Details:
   `docs/DESIGNER.md`, `docs/PERSISTENCE.md`). Nicht duplizieren – dieses API wiederverwenden.

3. **Ausdrücke beim Speichern validieren (#40 umgesetzt, #42 analog).** Branching-Bedingungen und
   Trigger-Ausdrücke über `IExpressionEvaluator.Validate(...)` kompilieren/prüfen, bevor gespeichert
   wird – die Engine ist gesandboxt (kein `eval`), siehe `docs/BRANCHING-EXPRESSIONS.md`. Dasselbe
   Prinzip setzt #39 bereits für Validierungs-**Muster** um:
   `QuestionFormModel.TryBuildValidationRules` kompiliert die Regex mit demselben 250-ms-Timeout wie der
   `AnswerValidator`, statt den Fehler bis zur Laufzeit zu vertagen.

   **Den Kontext dafür liefert `DesignerExpressionContext` (#40) – wiederverwenden, nicht nachbauen.**
   Er bindet je Frage einen Beispielwert, dessen **Typ exakt der Laufzeit-Bindung entspricht** (Zahl →
   `long`, Datum → **Zeichenkette**, Mehrfachauswahl → Liste) und jede Loop-Collection als leere Liste.
   Für #42 genügt derselbe Kontext: `TriggerDefinition.Expression` läuft über dieselbe Engine.
   Zwei Fallen: Zeichenketten-Literale **nicht** per `JsonSerializer` quotieren (dessen
   `\u00XX`-Escapes lehnt der Parser ab), und die Fehlermeldung der Engine ist **englisch** – deutsch
   rahmen statt übersetzen.

   **Fachliches JSON immer über den Core-Typ serialisieren, nicht über ein Duplikat.** `#39` benutzt
   `Flirty.Validation.ValidationRules` direkt (camelCase, `WhenWritingNull`); enthält gespeichertes JSON
   unbekannte Felder, fällt der Editor auf ein Roh-JSON-Textfeld zurück, statt sie beim Speichern
   stillschweigend zu verwerfen. Bei `LoopDefinition`/`TriggerDefinition` (#41/#42) genauso vorgehen.

4. **Loops sind Branching + Marker (#41 umgesetzt).** Ein Zyklus entsteht durch eine `Transition` auf eine
   frühere Frage; `LoopDefinition` (CollectionKey/Entry/Breaking) macht ihn sichtbar. Der Branching-Editor
   zeichnet ihn als **Rücksprung**-Badge, den Marker pflegt der Loop-Editor. Siehe `docs/LOOPS.md`.

   **Graph-Analysen des Cores spiegeln, nicht importieren.** `Services/LoopAnalyzer.cs` rechnet den
   Schleifen-Bereich nach (`(vorwärts ab Entry, Stopp an Breaking) ∩ (rückwärts zu Breaking) ∪
   {Entry, Breaking}`), weil `LoopResolver` `internal` ist und eine `Dialog`-Entity mit Navigationen
   braucht – der Designer hat nur `DialogDetail`. Dieselbe Abgrenzung wie `DesignerExpressionContext` ↔
   `SessionExpressionContextBuilder`. **Pflicht dabei:** ein Test, der beide Implementierungen auf
   demselben Graphen vergleicht (`LoopAnalyzerTests`, Body indirekt über
   `LoopResolver.ResolveAssignment`), sonst driften sie stillschweigend auseinander.

   **Warnungen spiegeln die Resolver-Regeln, nicht die Intuition.** „Exit unerreichbar" folgt exakt dem
   `TransitionResolver`: erster zutreffender Nicht-Default gewinnt (leerer Ausdruck trifft immer zu),
   sonst der oberste Default. Weitere Fälle: kein Exit, kein Rücksprung, überlappende Bereiche (der
   `LoopResolver` wirft dann schon im Konstruktor – **jede** Session bricht ab) und verdeckende
   `CollectionKey`s (Prüfung über `DesignerExpressionContext.IsBindable`/`IdentifierNote` teilen, nicht
   duplizieren).

   **FK-lose Verweise brauchen Aufräumen.** `LoopDefinition` referenziert Fragen ohne Fremdschlüssel –
   `DeleteQuestionCommand` entfernt verweisende Marker deshalb mit, wie schon die Übergänge. Eindeutig
   erzwungen wird nur der `CollectionKey` je Dialog (`LoopCollectionKeyExistsAsync` →
   `InvalidOperationException` → 409); ohne ihn überschrieben sich zwei gleichnamige Marker in der
   Collection-Bindung still.

5. **Test-Runner (#43):** ein Dialog-Durchlauf im Designer über `IFlirtyEngine` gegen das gewählte Profil.

## Empfohlene Aufbaureihenfolge (EPIC 7)

#37 Connection-Profile ✅ → #38 Dialog-CRUD-UI ✅ → #39 Frage-Editor ✅ → #40 Branching-Editor ✅ →
#41 Loop-Editor ✅ → #42 Trigger-Editor → #43 Test-Runner.

Der Trigger-Editor (#42) hängt sich wie seine Vorgänger in die Detailseite
`Components/Pages/DialogEditor.razor` (`/dialogs/{id:guid}`) ein – der `GetDialogQuery` liefert dort
bereits Fragen, Optionen, Übergänge und Schleifen-Marker; für Trigger fehlt die Projektion noch (analog
`DialogDetail.Loops` ergänzen). **Muster aus #39/#41 übernehmen:** Liste (mit Inline-Anlegen,
↑/↓-Sortieren, Inline-Löschbestätigung) im `DialogEditor`, Details auf einer eigenen Unterseite. Beim
Sortieren den **Positionsindex** als neue `Order`/`Priority` schreiben statt nur zwei Werte zu tauschen
(repariert doppelte/lückenhafte Werte) und alle Updates in **einem** `ExecuteAsync`-Aufruf senden.

## Konventionen

- Blazor-Komponenten unter `Components/` (Pages in `Components/Pages/`), Server-interaktiver Render-Mode
  beibehalten.
- **Komponentennamen dürfen die Sichttypen aus `Flirty.Runtime.Admin` nicht verdecken** – deshalb heißen
  die Detailseiten `DialogEditor`/`QuestionEditor`/`TransitionEditor` und nicht
  `DialogDetail`/`QuestionDetail`/`TransitionDetail` (sonst verschattet der generierte Komponententyp den
  gleichnamigen Record) – und `LoopEditor` statt `LoopDetail`. Gilt genauso für kommende Seiten zu
  `TriggerDetail`/`AnswerOptionDetail`.
- **Live-Validierung braucht ein rohes `<textarea>` mit `@oninput`**: an einer `InputTextArea` lässt sich
  `@bind-Value:event="oninput"` nicht mit `@bind-Value:after` kombinieren (RZ10010), und ohne `oninput`
  prüft der Editor erst beim Verlassen des Felds.
- Gemeinsame UI-Klassen gehören nach `wwwroot/app.css` (global), nicht in jede `*.razor.css` kopiert.
- Bestätigungen **inline** im Komponentenzustand lösen, **kein** JS-`confirm`/`alert` – das blockiert
  sonst die Playwright-E2E (#46).
- UI-Texte und Doku **deutsch**. Der Designer ist `IsPackable=false` (kein NuGet-Paket) → CS1591 ist
  hier **kein** Fehler, XML-Docs sind optional.
- E2E-Tests des Designers gehören nach `tests/Flirty.E2E` (Playwright, #46) – aktuell noch Skelett.

## Definition of Done

Feature funktioniert im Server-interaktiven Designer über die Admin-Commands (via `FlirtyAdminGateway`) ·
Ausdrücke werden beim Speichern validiert · Service-Tests in `tests/Flirty.Tests/Designer/` ·
`docs/DESIGNER.md` beim jeweiligen Feature erweitern · ggf. Playwright-E2E (#46).

## Verifikation

```pwsh
dotnet build Flirty.sln
dotnet run --project src/Flirty.Designer     # Designer lokal starten
dotnet test tests/Flirty.Tests
```
