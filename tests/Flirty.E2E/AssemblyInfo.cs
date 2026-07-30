// Both E2E suites (the web sample's chat UI, #45/#47, and the designer, #46) are separate test classes
// and thus separate xUnit collections – so they ran in PARALLEL by default. Each hosts a real Kestrel
// and drives its own Chromium: on the two cores of the CI runner they then compete for CPU and run into
// Playwright timeouts. For the same reason the pipeline already separates the two test assemblies (see
// docs/CI.md); here it applies within the E2E assembly.
// Cost: nothing – run sequentially, the whole suite takes about twenty seconds.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
