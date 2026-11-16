namespace Test.Shared
{
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Keyframe.Core.Analysis;
    using Keyframe.Core.Enums;
    using Keyframe.Core.Requests;
    using Touchstone.Core;

    /// <summary>
    /// Prompt and schema construction.
    /// </summary>
    public static class PromptSuite
    {
        private const string Id = "Prompts";

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite.</returns>
        public static TestSuiteDescriptor Create()
        {
            return new TestSuiteDescriptor(Id, "Prompts and schemas", new[]
            {
                new TestCaseDescriptor(Id, "ContextInEveryPrompt", "Every context item appears in summary, combined, and per-field prompts", ct =>
                {
                    List<ContextItem> items = new List<ContextItem>
                    {
                        new ContextItem { Label = "Location", Text = "Austin" },
                        new ContextItem { Text = "Orange collars mean adopted." }
                    };
                    MediaSubject subject = new MediaSubject { CallerContext = ContextFormatter.Render(items) };
                    FieldSpec field = new FieldSpec { Key = "k", Prompt = "Question?" };

                    string[] prompts = new[]
                    {
                        PromptBuilder.Summarize(subject, new SummarizeSpec { Enable = true }),
                        PromptBuilder.ExtractCombined(subject, new List<FieldSpec> { field }),
                        PromptBuilder.ExtractSingle(subject, field)
                    };

                    foreach (string p in prompts)
                    {
                        Check.True(p.Contains("<context>") && p.Contains("- Location: Austin") && p.Contains("- Orange collars mean adopted."), "context block in: " + p);
                    }

                    return Task.CompletedTask;
                }, new[] { "fast" }),

                new TestCaseDescriptor(Id, "SingleContextPlain", "A single unlabeled context item renders as plain text", ct =>
                {
                    string? text = ContextFormatter.Render(new List<ContextItem> { new ContextItem { Text = "  Just this.  " } });
                    Check.Equal("Just this.", text, "rendered");
                    Check.Equal(null, ContextFormatter.Render(new List<ContextItem>()), "empty");
                    return Task.CompletedTask;
                }, new[] { "fast" }),

                new TestCaseDescriptor(Id, "CombinedSchema", "Combined schema lists every field with its type", ct =>
                {
                    List<FieldSpec> fields = new List<FieldSpec>
                    {
                        new FieldSpec { Key = "hasCats", Type = FieldTypeEnum.Boolean, Prompt = "Cats?" },
                        new FieldSpec { Key = "setting", Type = FieldTypeEnum.Enum, Prompt = "Where?", Options = new List<string> { "Indoors", "Outdoors" } }
                    };
                    string schema = FieldSchemaBuilder.Combined(fields);
                    Check.True(schema.Contains("\"hasCats\":{\"type\":\"boolean\""), "boolean property");
                    Check.True(schema.Contains("\"enum\":[\"Indoors\",\"Outdoors\"]"), "enum options");
                    Check.True(schema.Contains("\"required\":[\"hasCats\",\"setting\"]"), "required keys");
                    return Task.CompletedTask;
                }, new[] { "fast" }),

                new TestCaseDescriptor(Id, "Truncate", "Long transcripts keep head and tail", ct =>
                {
                    string text = new string('a', 5000) + new string('z', 5000);
                    string cut = PromptBuilder.Truncate(text, 1000);
                    Check.True(cut.Length <= 1000 && cut.StartsWith("a") && cut.EndsWith("z"), "head and tail kept");
                    return Task.CompletedTask;
                }, new[] { "fast" })
            });
        }
    }
}
