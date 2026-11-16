// The shared suites use process-wide state (the Keyframe meter, static gauge providers, in-process servers), so the
// fact and theory hosts must not run concurrently.
[assembly: Xunit.v3.Parallelization(Mode = Xunit.Sdk.ParallelMode.None)]
