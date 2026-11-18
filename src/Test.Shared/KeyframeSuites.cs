namespace Test.Shared
{
    using System.Collections.Generic;
    using Touchstone.Core;

    /// <summary>
    /// Every Keyframe test suite, consumed by all test runners.
    /// </summary>
    public sealed class KeyframeSuites : ITestSuiteProvider
    {
        /// <summary>
        /// All suites, with fresh descriptors on each access.
        /// </summary>
        public static IReadOnlyList<TestSuiteDescriptor> All
        {
            get
            {
                List<TestSuiteDescriptor> suites = new List<TestSuiteDescriptor>
                {
                    SettingsSuite.Create(),
                    RequestSuite.Create(),
                    CoercionSuite.Create(),
                    PromptSuite.Create(),
                    MediaSuite.Create(),
                    AnalysisSuite.Create(),
                    AudioSuite.Create(),
                    ListenSuite.Create(),
                    MeasureSuite.Create(),
                    SectionSuite.Create(),
                    SceneAudioSuite.Create(),
                    AnalyticsSuite.Create(),
                    SegmentSuite.Create(),
                    ServerSuite.Create(),
                    McpSuite.Create(),
                    ProviderSuite.Create(),
                    LiveProviderSuite.Create()
                };

                suites.AddRange(DatabaseSuite.Create());
                return suites;
            }
        }

        /// <inheritdoc />
        public IReadOnlyList<TestSuiteDescriptor> Suites
        {
            get
            {
                return All;
            }
        }
    }
}
