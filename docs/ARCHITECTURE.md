# Flirty – Architektur

> Chatbot-/Dialog-Engine für .NET. Der Anwender baut nur noch die UI; Flirty übernimmt
> Persistenz, Antwort-Parsing/-Validierung, Branching, Loops, Resume, editierbare Antworten
> und Trigger (Rückkanäle in die Host-App).

## 1. Ziel & Motivation

Wer heute einen Chatbot-Dialog baut, implementiert jedes Mal aufs Neue: Fragen/Antworten
persistieren, Antworten parsen, Verzweigungslogik (Branching), Wiederaufnahme (Resume) und
Rückkanäle in die eigene App. Das ist repetitiv und fehleranfällig.

**Flirty** kapselt diese Logik als wiederverwendbare Engine. Dialoge werden über einen
**Blazor-Designer** konfiguriert (auch von nicht-technischen Nutzern). Die Integration in
fremde Apps erfolgt über **DI-Extension-Methods** und optional bereitgestellte
**WebAPI-Endpunkte**. Eine mitgegebene **DB-Connection** wird automatisch migriert.

## 2. Feature-Überblick

| Feature | Umsetzung |
|---|---|
| Resume innerhalb eines Dialogs | `DialogSession` + `CurrentQuestionId`, `ResumeDialogQuery` |
| Fragen wieder bearbeitbar | `EditAnswerCommand` + Pfad-Neuberechnung |
| Branching (mehrere Zweige) | `Transition` + Expression-Engine (`IExpressionEvaluator`) |
| Loops (Liste bis Breaking Question) | Branching-Zyklus + `LoopDefinition`-Marker, Iterations-Collection |
| Trigger nach Antwort / Abschluss | Mediator-Notifications (In-Process) + Outbound-Webhooks |
| Einfache DI-Registrierung | `services.AddFlirty(o => …)` Extension-Methods |
| DB-Connection + Auto-Migration | `o.UseSqlServer/UsePostgreSql/UseSqlite` + `o.ApplyMigrations()` |
| Optionale WebAPI-Endpunkte | Paket `Flirty.AspNetCore`, `app.MapFlirtyEndpoints()` |
| Designer, Multi-DB | Blazor Web App, Connection-Profile + `IDbContextFactory` |
| Designer CRUD | Dialoge / Fragen / Antworten / Branching / Loops / Trigger |
| Entwürfe durchspielen | Test-Runner im Designer + `StartDialogVersionCommand` (ohne Veröffentlichung) |

## 3. Grundsatzentscheidungen

| Thema | Entscheidung |
|---|---|
| Target Framework | **.NET 10** (alle Projekte) |
| DB-Provider | **SQLite + PostgreSQL + SQL Server**, Kern provider-agnostisch via EF Core – [ADR 0001](./adr/0001-migrationen-pro-provider.md) |
| Designer-Hosting | **Blazor Web App, Server-interaktiv** |
| Branching | **Expression-/Script-Engine**, gesandboxt (Default: DynamicExpresso) – [ADR 0004](./adr/0004-gesandboxte-expression-engine.md) |
| Trigger | **In-Process (Mediator-Notifications) + Outbound-Webhooks** |
| Mediator | **Mediator (martinothamar)** – Source-Generator, MIT – [ADR 0002](./adr/0002-mediator-als-in-process-bus.md) |
| Endpunkte | **Optional**, eigenes Projekt `Flirty.AspNetCore`; Core bleibt **ASP.NET-frei** – [ADR 0003](./adr/0003-aspnet-freier-core.md) |
| NuGet | `Flirty` + `Flirty.AspNetCore` als **veröffentlichbare Packages** |
| Dokumentation | XML-Docs (CS1591 als Error) + `docs/`-Guides + ADRs, Teil jeder DoD |

## 4. Solution-Struktur

```
Flirty.sln
├─ src/
│  ├─ Flirty              Core-Engine (REINE Class-Library, KEIN ASP.NET):
│  │                        Domain, Runtime, Persistenz (EF Core),
│  │                        Mediator-Commands/Queries/Notifications,
│  │                        Expression-Engine, Trigger, DI-Extensions
│  ├─ Flirty.AspNetCore   OPTIONAL: WebAPI-Endpunkt-Mapping (MapFlirtyEndpoints),
│  │                        dünne Schicht über die Mediator-Commands
│  ├─ Flirty.Designer     Blazor Web App (Server-interaktiv): Dialog-/Frage-/Antwort-/
│  │                        Branching-/Loop-/Trigger-Konfiguration, Test-Runner, Multi-DB
│  ├─ Flirty.Migrations.Sqlite       \
│  ├─ Flirty.Migrations.PostgreSql    } EF-Migrationen pro Provider (IsPackable=false,
│  ├─ Flirty.Migrations.SqlServer    /    DLLs werden ins Flirty-Paket gebündelt)
│  ├─ Flirty.Samples      Console-Single-Project (nur Core + eigener Handler)
│  └─ Flirty.Samples.Web  Minimal-API + statische Chat-UI (nutzt Flirty.AspNetCore):
│                          Resume/Edit/Branching/Loop/Trigger + Webhook-Empfänger
└─ tests/
   ├─ Flirty.Tests        xUnit Unit-/Integrationstests (EF Core SQLite in-memory)
   └─ Flirty.E2E          Playwright-E2E (Designer + Web-Sample)
```

**Wichtig:** `Flirty` hat **keine** ASP.NET-Abhängigkeit und lässt sich unverändert in eine
reine Console-/Worker-App hängen. `Flirty.AspNetCore` (`FrameworkReference
Microsoft.AspNetCore.App`) wird nur referenziert, wenn Web/Endpunkte gewünscht sind.

## 5. Domain-Modell (Konfiguration)

- **Dialog** – `Id`, `Key`, `Name`, `Description`, `Version`, `IsPublished`, `StartQuestionId`, Timestamps.
- **Question** – `Id`, `DialogId`, `Key`, `Text`, `Type` (SingleChoice, MultiChoice, FreeText, Number, Date, Boolean), `Order`, `IsRequired`, `ValidationRules` (JSON).
- **AnswerOption** – `Id`, `QuestionId`, `Key`, `Label`, `Value`, `Order`.
- **Transition** – `Id`, `DialogId`, `FromQuestionId`, `Expression`, `TargetQuestionId`, `Priority`, `IsDefault`. Geordnete Liste bedingter Übergänge je Frage; erste zutreffende gewinnt, sonst Default. Ein `TargetQuestionId` auf eine **frühere** Frage bildet einen **Loop-Zyklus**.
- **LoopDefinition** – `Id`, `DialogId`, `CollectionKey`, `EntryQuestionId`, `BreakingQuestionId`. Metadaten-/Marker-Ebene über dem Branching für Runtime-Sammlung und Designer-Visualisierung. Der Exit ist **keine** eigene Eigenschaft, sondern läuft über die normale `Transition`-Mechanik (der Exit-Übergang der Breaking Question).
- **TriggerDefinition** – `Id`, `DialogId`, `Scope` (OnDialogStarted/AfterAnswer/AfterQuestion/OnDialogCompleted), `QuestionId?`, `Kind` (InProcess|Webhook), `Config` (JSON), `Expression?`.

## 6. Runtime-/Session-State

- **DialogSession** – `Id`, `DialogId`, `DialogVersion`, `ExternalUserKey`, `Status` (InProgress/Completed/Abandoned), `CurrentQuestionId`, `StartedAt`, `CompletedAt`. → **Resume**.
- **SessionAnswer** – `Id`, `SessionId`, `QuestionId`, `Value` (JSON), `AnsweredAt`, `Sequence`, `LoopInstanceId?`, `IterationIndex?`. → editierbare Antworten; Loop-Iterationen erlauben mehrere Antworten pro `QuestionId` (ein Eintrag je Iteration).

## 7. Kern-Services (In-Process-API via Mediator)

Alle Engine-Operationen sind **Mediator-Commands/Queries**; In-Process-Trigger sind
**Mediator-Notifications**. Host-Apps nutzen entweder die Facade `IFlirtyEngine` oder senden
Commands direkt per `ISender` (Facade + erster Command umgesetzt in #25, siehe [RUNTIME.md](./RUNTIME.md)).

**Commands/Queries**
- `StartDialogCommand(dialogKey, externalUserKey)` → Session + erste Frage (oder Resume). Facade:
  `IFlirtyEngine.StartDialogAsync`. Umgesetzt in #25, Details in [RUNTIME.md](./RUNTIME.md).
  *(Bewusst ohne Seed-Parameter: Startwerte hätten bis heute keinen Speicherort im Modell – der
  Ausdruckskontext speist sich ausschließlich aus `SessionAnswer`.)*
- `StartDialogVersionCommand(dialogId, externalUserKey)` → wie oben, aber gegen eine **konkrete
  Dialogversion unabhängig vom Veröffentlichungsstatus**. Facade: `IFlirtyEngine.StartDialogVersionAsync`.
  Umgesetzt in #43 für den [Test-Runner des Designers](./DESIGNER.md#test-runner-43) – ohne ihn wäre ein
  Entwurf nicht durchspielbar, und „zum Testen kurz veröffentlichen" hätte ihn für echte Anwender scharf
  geschaltet. **Bewusst ohne HTTP-Endpunkt**: über HTTP bleibt der Publish-Status die Produktionsschranke.
  Details in [RUNTIME.md](./RUNTIME.md#startdialogversioncommand-43).
- `ResumeDialogQuery(sessionId)` → Session-Status + aktuelle Frage + bisherige Antworten (rein lesend).
  Facade: `IFlirtyEngine.ResumeDialogAsync`. Umgesetzt in #27, Details in [RUNTIME.md](./RUNTIME.md).
  *(Der Resume-oder-Neu-Pfad je Anwender liegt bei `StartDialogCommand`; einen eigenen
  `externalUserKey`-Lookup gibt es bewusst nicht.)*
- `SubmitAnswerCommand(sessionId, questionId, value)` → validiert → persistiert → Transition-Auswertung → nächste Frage/Completion. Facade: `IFlirtyEngine.SubmitAnswerAsync`. Umgesetzt in #26, Details in [RUNTIME.md](./RUNTIME.md). *(Publiziert seit #31 `AnswerSubmitted`/`QuestionAnswered`/`DialogCompleted`.)*
- `EditAnswerCommand(sessionId, questionId, value)` → frühere Antwort überschreiben, nachgelagerten Pfad neu berechnen/invalidieren (öffnet ggf. eine abgeschlossene Session wieder). Facade: `IFlirtyEngine.EditAnswerAsync`. Umgesetzt in #28, Details in [RUNTIME.md](./RUNTIME.md). *(Publiziert seit #31 bei Abschluss `DialogCompleted`.)*

**Notifications (= In-Process-Trigger)** – `DialogStartedNotification`, `AnswerSubmittedNotification`, `QuestionAnsweredNotification`, `DialogCompletedNotification`. Der Nutzer „hängt seine Handler rein" per `INotificationHandler<T>` (funktioniert 1:1 in einer Console-App). Contracts + Publikation aus den Command-Handlern umgesetzt in #31, Details in [TRIGGERS.md](./TRIGGERS.md).

**Weitere Services**
- `IExpressionEvaluator` (`Flirty.Expressions`) – Ausdrucks-Engine `bool Evaluate(string expression, ExpressionContext context)`. Default `DynamicExpressoExpressionEvaluator` (#23). Der unveränderliche `ExpressionContext` bündelt: `Answers` (nach `Question.Key`), `Collections` (Loop-Antworten je Iteration nach `CollectionKey`), `IterationIndex`, `Now`, `Session`; Werte sind roher JSON-Text (Typisierung erst in der Engine). Interface + Kontext-Modell umgesetzt in #22, Details in [BRANCHING-EXPRESSIONS.md](./BRANCHING-EXPRESSIONS.md). Seit #26 als Default-Singleton in `AddFlirty()` registriert (erster Runtime-Konsument: Transition-Auswertung von `SubmitAnswerCommand`); der austauschbare `o.UseExpressionEvaluator<T>()`-Overload ist seit #34 verfügbar.
- `IAnswerValidator` – typisierte, regelbasierte Antwort-Validierung (Typ + `ValidationRules`), als Mediator-`IPipelineBehavior` (`AnswerValidationPipelineBehavior`) vor Submit/Edit. Umgesetzt in #30, Details in [VALIDATION.md](./VALIDATION.md).
- Webhook-`INotificationHandler` – Outbound-HTTP-`POST` (`IHttpClientFactory` + Standard-Resilience: Retry/Timeout), umgesetzt in #33. Ziele kommen aus zwei additiven Quellen: im Code per `o.AddWebhook(scope, url, expression?)` registriert (Registrierung als Stub seit #34) **und seit #42** aus den am Dialog konfigurierten `TriggerDefinition`s (`Kind = Webhook`, Konfiguration als JSON nach dem Schema `TriggerConfig`). Der eingebaute `WebhookNotificationHandler` (auto-registriert) filtert nach `TriggerScope` (bei `AfterQuestion` zusätzlich nach der Frage), wertet optionale Bedingungen via `IExpressionEvaluator` aus und liefert best-effort aus. Details in [TRIGGERS.md](./TRIGGERS.md#outbound-webhooks).
- `IDialogStore` – Repository über `FlirtyDbContext` (umgesetzt in #21, Details in [PERSISTENCE.md](./PERSISTENCE.md#idialogstore-repository-21)).

## 8. Persistenz & Migrationen

- **`FlirtyDbContext`** (EF Core 10), Provider-Wahl via Options.
- **Migrationen pro Provider** (EF-Anforderung): getrennte Migrations-Assemblies
  `Flirty.Migrations.{Sqlite,PostgreSql,SqlServer}`; zur Laufzeit über `MigrationsAssembly` selektiert
  (umgesetzt in #19, siehe [PERSISTENCE.md](./PERSISTENCE.md) inkl. [ADR 0001](./adr/0001-migrationen-pro-provider.md)).
- **Auto-Apply** via `o.ApplyMigrations()` → `FlirtyMigrationHostedService` (`IHostedService`) ruft beim Start `Database.MigrateAsync()`; die Migrations-Assemblies werden ins `Flirty`-NuGet-Paket gebündelt (umgesetzt in #20, siehe [PERSISTENCE.md](./PERSISTENCE.md)).
- **Multi-DB im Designer**: Connection-Profile (Provider + ConnectionString) lokal verwaltet, `IDbContextFactory` öffnet zur Laufzeit gegen das gewählte Profil (umgesetzt in #37; Provider-Wahl als Wert über `FlirtyDatabaseProvider` + `UseFlirtyProvider`, siehe [DESIGNER.md](./DESIGNER.md) und [PERSISTENCE.md](./PERSISTENCE.md)).

## 9. Integrations-API

```csharp
// Core – reicht für eine reine Console-Single-Project-App
services.AddFlirty(o => {
    o.UseSqlServer(conn);                 // oder UsePostgreSql / UseSqlite
    o.ApplyMigrations();                  // optional: Auto-Migration beim Start
    o.UseExpressionEvaluator<MyEval>();    // Expression-Engine austauschbar
    o.AddWebhook(TriggerScope.OnDialogCompleted, url);  // Outbound-Webhook (Auslieferung seit #33)
});
// In-Process-Trigger = Mediator-Notification-Handler:
services.AddScoped<INotificationHandler<DialogCompletedNotification>, MyDoneHandler>();

// NUR bei Web/Endpunkten (Paket Flirty.AspNetCore):
app.MapFlirtyEndpoints("/flirty");
```

**Endpunkte** (`Flirty.AspNetCore`): `POST /flirty/sessions`, `GET /flirty/sessions/{id}`,
`POST /flirty/sessions/{id}/answers`, `PUT /flirty/sessions/{id}/answers/{questionId}`.
`MapFlirtyEndpoints` sendet die Runtime-Commands direkt per `ISender` und mappt
sie auf Request-/Response-DTOs; Engine-Ausnahmen werden auf `ProblemDetails` (404/400/409) abgebildet.
Umgesetzt in #35, Details in [GETTING-STARTED-WebApi.md](./GETTING-STARTED-WebApi.md). Das optionale
**Admin-CRUD** (`app.MapFlirtyAdminEndpoints("/flirty/admin")`, opt-in, per `RequireAuthorization()`
absicherbar) verwaltet den Konfigurationsgraphen – Dialoge (`/dialogs`, inkl. `publish`/`unpublish`),
Fragen (`.../questions`), Optionen (`.../options`), Übergänge (`.../transitions`), Schleifen-Marker
(`.../loops`) und Trigger (`.../triggers`) – über dieselbe Mediator-/DTO-/Filter-Mechanik. Umgesetzt in
#36, die Schleifen-Endpunkte in #41, die Trigger-Endpunkte in #42; Details ebd.

## 10. Loops (Schleifen)

Loops entstehen **über das vorhandene Branching**: eine Transition zeigt auf eine frühere
Frage (Zyklus). Der `LoopDefinition`-Marker bewirkt zweierlei:
1. **Runtime** sammelt je Iteration die Antwort der Einstiegsfrage unter `CollectionKey` (statt zu überschreiben) — `SessionAnswer.LoopInstanceId`/`IterationIndex` machen mehrere Antworten pro Frage möglich. Umgesetzt in **#29** (Details in [LOOPS.md](./LOOPS.md)).
2. **Designer** visualisiert den Zyklus als Loop-Block mit markierter **Breaking Question** und warnt vor
   fehlendem oder unerreichbarem Ausstieg (Endlosschleife) sowie überlappenden Bereichen. Umgesetzt in
   **#41** (Details in [DESIGNER.md](./DESIGNER.md#loop-editor-41)).

Die **Breaking Question** ist die Frage, deren Exit-Transition den Zyklus verlässt; danach
läuft der Dialog normal weiter. Break-Bedingungen und nachgelagertes Branching sehen die
gesammelte Collection im Expression-Kontext (z. B. `positions.Count > 0`).

## 11. Design-Notizen

1. **Mediator (martinothamar)**: Source-Generator (kein Reflection-Overhead), MIT. Engine-Ops = Commands/Queries, Trigger = Notifications. Cross-Cutting via `IPipelineBehavior` (Logging, Validierung, Transaktionen). **Umgesetzt in #14:** der `AddFlirty()`-Stub verdrahtet den Mediator (`ServiceLifetime.Scoped`) und registriert die offen-generischen Basis-Behaviors `LoggingPipelineBehavior<,>` und `ValidationPipelineBehavior<,>` (manuelle Registrierung – martinothamar-Vorgabe). Der Source-Generator läuft im Core, daher muss der `AddMediator`-Aufruf im Core liegen. Details siehe [MEDIATOR.md](./MEDIATOR.md), verworfene Alternativen in [ADR 0002](./adr/0002-mediator-als-in-process-bus.md).
2. **ASP.NET-frei im Core**: reine Console-/Worker-Nutzung möglich. Verworfene Alternativen (ein Paket mit ASP.NET-Referenz, `#if`-Varianten) in [ADR 0003](./adr/0003-aspnet-freier-core.md).
3. **Expression-Sicherheit**: kein roher C#-`eval`. DynamicExpresso ist gesandboxt (Member-Whitelist); Ausdrücke werden im Designer beim Speichern kompiliert/validiert. Austauschbar über `IExpressionEvaluator` (Alternative: NCalc). Verworfene Alternativen (Roslyn-Scripting, eigene Grammatik) in [ADR 0004](./adr/0004-gesandboxte-expression-engine.md).
4. **Dialog-Versionierung**: Sessions pinnen `DialogVersion` → Editieren publizierter Dialoge bricht laufende Sessions nicht.
5. **Loops = Branching + Marker**: kein separater Runtime-Sonderpfad.
6. **NuGet-Packaging**: `Flirty` + `Flirty.AspNetCore` mit vollständigen Metadaten (MIT-Lizenz, Icon, README), SourceLink und Symbolpaketen (`snupkg`); übrige Projekte `IsPackable=false`. Paketversion **datumsbasiert** (`JJJJMM.Revision`, z.B. `202604.1`), Assembly-Version davon entkoppelt (`Jahr.Monat.Revision`, UInt16-Grenze). Details: [NUGET-PACKAGING.md](./NUGET-PACKAGING.md).

## 12. Dokumentation („alles dokumentiert")

Doku ist **Definition-of-Done jedes Issues**:
- XML-Doc-Kommentare auf allen public Typen/Membern; `GenerateDocumentationFile` + **CS1591 als Error** (zentral in `Directory.Build.props`).
- `docs/`-Guides: `ARCHITECTURE.md`, `DOMAIN-MODEL.md`, `MEDIATOR.md`, `PERSISTENCE.md`, `RUNTIME.md`,
  `BRANCHING-EXPRESSIONS.md`, `LOOPS.md`, `VALIDATION.md`, `TRIGGERS.md`, `DESIGNER.md`,
  `GETTING-STARTED-Console.md`, `GETTING-STARTED-WebApi.md`, `GETTING-STARTED-Sample-Web.md`,
  `NUGET-PACKAGING.md`, `CI.md`, `ROADMAP.md`, `BACKLOG.md`. Der Wegweiser mit einer Zeile je Guide steht
  in der `CLAUDE.md` im Repo-Root.
- ADRs unter [`docs/adr/`](./adr/README.md) – die Entscheidungen samt **verworfenen Alternativen**:
  [0001 Migrationen pro Provider](./adr/0001-migrationen-pro-provider.md),
  [0002 Mediator](./adr/0002-mediator-als-in-process-bus.md),
  [0003 ASP.NET-freier Core](./adr/0003-aspnet-freier-core.md),
  [0004 Expression-Engine](./adr/0004-gesandboxte-expression-engine.md). Abgrenzung: Guides
  beschreiben, **wie** etwas funktioniert, und wachsen mit dem Code; ADRs beschreiben, **warum** es so
  ist, und werden nicht fortgeschrieben (Nachtrag oder Ablösung statt Umschreiben).
- Root-`README.md` mit Quickstart (Console + Web); Codebeispiele aus den kompilierbaren Samples (kein
  Doku-Drift). Sie ist zugleich die **Paketseite beider NuGet-Pakete** (`PackageReadmeFile`) – daher nur
  absolute Links und Bilder von der nuget.org-Allowlist, festgehalten in
  [NUGET-PACKAGING.md](./NUGET-PACKAGING.md#die-root-readme-ist-die-paketseite-52) und abgesichert durch
  `tests/Flirty.Tests/Docs/PackageReadmeTests.cs`.

## 13. Verifikation

- **Build/Test**: `dotnet build Flirty.sln`, `dotnet test` nach jedem Epic.
- **Kern-Runtime**: Unit-Tests für Branching, Loops, Resume, Edit-Pfad, Trigger (In-Process + Webhook-Mock) gegen SQLite in-memory.
- **Provider**: Migration + Smoke-CRUD gegen SQLite (optional PostgreSQL/SQL Server via Container).
- **Console-Nutzung**: Console-Sample ohne ASP.NET-Referenz durchspielen.
- **Loops**: mehrere Listen-Einträge erfassen, Breaking Question beendet, Collection im Kontext.
- **Web-E2E**: Web-Sample + Designer via Playwright (Branching, Loop, Resume nach Reload, Edit) – #45/#47 und #46.
- **Coverage**: die CI misst `Flirty` + `Flirty.AspNetCore` (coverlet + ReportGenerator, ohne
  Schwellwert-Gate), siehe [CI.md § Coverage](./CI.md#coverage).
- **NuGet**: `dotnet pack` erzeugt beide `.nupkg` (+ `.snupkg`); veröffentlicht wird über den eigenen,
  manuell ausgelösten Workflow hinter einem Freigabe-Gate, siehe
  [NUGET-PACKAGING.md § Publizieren](./NUGET-PACKAGING.md#publizieren-49).

---

> Backlog / Issue-Liste siehe [BACKLOG.md](./BACKLOG.md). Entscheidungshistorie unter
> [`docs/adr/`](./adr/README.md).
