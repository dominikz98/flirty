// Beide E2E-Suiten (Chat-UI der Web-Sample, #45/#47, und Designer, #46) sind eigene Testklassen und
// damit für xUnit eigene Collections – sie liefen also standardmäßig PARALLEL. Jede hostet ein echtes
// Kestrel und steuert einen eigenen Chromium: auf den zwei Kernen des CI-Runners konkurrieren sie
// dann um CPU und laufen in Playwright-Timeouts. Aus demselben Grund trennt die Pipeline schon die
// beiden Test-Assemblies (siehe docs/CI.md); hier gilt es innerhalb der E2E-Assembly.
// Kosten: nichts – nacheinander braucht die ganze Suite (neun Tests) rund zwanzig Sekunden.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
