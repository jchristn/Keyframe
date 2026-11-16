namespace Test.Shared
{
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Keyframe.Core.Enums;
    using Keyframe.Core.Requests;
    using Keyframe.Core.Serialization;
    using Touchstone.Core;

    /// <summary>
    /// Request parsing and validation, including the three accepted context shapes.
    /// </summary>
    public static class RequestSuite
    {
        private const string Id = "Requests";

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite.</returns>
        public static TestSuiteDescriptor Create()
        {
            return new TestSuiteDescriptor(Id, "Request contracts", new[]
            {
                new TestCaseDescriptor(Id, "OwnerExample", "The owner's example request parses and validates", ct =>
                {
                    string json = @"{
                        ""image"": { ""url"": ""http://foo.bar/baz.jpg"" },
                        ""functions"": {
                            ""summarize"": { ""enable"": true, ""key"": ""summary"" },
                            ""extract"": { ""enable"": true, ""fields"": [
                                { ""key"": ""hasCats"", ""type"": ""boolean"", ""prompt"": ""Does this image have cats?"" },
                                { ""key"": ""dogCount"", ""type"": ""integer"", ""prompt"": ""How many dogs do you see in this image"" }
                            ] }
                        }
                    }";
                    ImageAnalysisRequest? request = KeyframeSerializer.Deserialize<ImageAnalysisRequest>(json);
                    Check.True(request != null, "parsed");
                    Check.Equal("summary", request!.Functions.Summarize.Key, "summary key");
                    Check.Equal(FieldTypeEnum.Integer, request.Functions.Extract.Fields[1].Type, "camelCase enum value");
                    List<string> errors = RequestValidator.Validate(request);
                    Check.Equal(0, errors.Count, "errors: " + string.Join(" ", errors));
                    return Task.CompletedTask;
                }, new[] { "fast" }),

                new TestCaseDescriptor(Id, "ContextString", "Context accepts a plain string", ct =>
                {
                    ImageAnalysisRequest r = Parse(@"""Context"": ""Shelter photos.""");
                    Check.Equal(1, r.Context.Count, "items");
                    Check.Equal("Shelter photos.", r.Context[0].Text, "text");
                    return Task.CompletedTask;
                }, new[] { "fast" }),

                new TestCaseDescriptor(Id, "ContextArray", "Context accepts strings and labeled items together", ct =>
                {
                    ImageAnalysisRequest r = Parse(@"""Context"": [ ""First note."", { ""Label"": ""Location"", ""Text"": ""Austin"" }, null ]");
                    Check.Equal(2, r.Context.Count, "items (nulls dropped)");
                    Check.Equal("Location", r.Context[1].Label, "label");
                    string json = KeyframeSerializer.Serialize(r);
                    Check.True(json.Contains("\"Context\":[{\"Label\":null,\"Text\":\"First note.\"}"), "context always serializes as items");
                    return Task.CompletedTask;
                }, new[] { "fast" }),

                new TestCaseDescriptor(Id, "ContextLimits", "Context limits are enforced", ct =>
                {
                    ImageAnalysisRequest r = Valid();
                    for (int i = 0; i < 51; i++) r.Context.Add(new ContextItem { Text = "x" });
                    r.Context.Add(new ContextItem { Text = " " });
                    List<string> errors = RequestValidator.Validate(r);
                    Check.True(errors.Any(e => e.Contains("at most 50 items")), "item count");
                    Check.True(errors.Any(e => e.Contains("Text is required")), "empty text");
                    return Task.CompletedTask;
                }, new[] { "fast" }),

                new TestCaseDescriptor(Id, "SourceRules", "Media source requires exactly one of Url or Data", ct =>
                {
                    ImageAnalysisRequest both = Valid();
                    both.Image!.Data = "AAAA";
                    Check.True(RequestValidator.Validate(both).Any(e => e.Contains("not both")), "both");

                    ImageAnalysisRequest neither = Valid();
                    neither.Image!.Url = null;
                    Check.True(RequestValidator.Validate(neither).Any(e => e.Contains("must specify Url or Data")), "neither");

                    ImageAnalysisRequest ftp = Valid();
                    ftp.Image!.Url = "ftp://example.com/a.jpg";
                    Check.True(RequestValidator.Validate(ftp).Any(e => e.Contains("http or https")), "scheme");
                    return Task.CompletedTask;
                }, new[] { "fast" }),

                new TestCaseDescriptor(Id, "FieldRules", "Field keys, prompts, and constraints are validated", ct =>
                {
                    ImageAnalysisRequest r = Valid();
                    r.Functions.Summarize.Enable = true;
                    r.Functions.Extract.Enable = true;
                    r.Functions.Extract.Fields = new List<FieldSpec>
                    {
                        new FieldSpec { Key = "a", Prompt = "ok" },
                        new FieldSpec { Key = "A", Prompt = "duplicate" },
                        new FieldSpec { Key = "bad key", Prompt = "spaces" },
                        new FieldSpec { Key = "Summary", Prompt = "collides" },
                        new FieldSpec { Key = "e", Type = FieldTypeEnum.Enum, Prompt = "no options" },
                        new FieldSpec { Key = "n", Type = FieldTypeEnum.Integer, Prompt = "range", Minimum = 5, Maximum = 1 },
                        new FieldSpec { Key = "p", Prompt = "" }
                    };
                    List<string> errors = RequestValidator.Validate(r);
                    Check.True(errors.Any(e => e.Contains("duplicated")), "duplicate");
                    Check.True(errors.Any(e => e.Contains("[2].Key")), "bad key");
                    Check.True(errors.Any(e => e.Contains("collides with the summary key")), "collision");
                    Check.True(errors.Any(e => e.Contains("Options is required")), "enum options");
                    Check.True(errors.Any(e => e.Contains("Minimum must not exceed Maximum")), "range");
                    Check.True(errors.Any(e => e.Contains("[6].Prompt is required")), "prompt");
                    return Task.CompletedTask;
                }, new[] { "fast" }),

                new TestCaseDescriptor(Id, "NoFunctions", "At least one function must be enabled", ct =>
                {
                    ImageAnalysisRequest r = Valid();
                    r.Functions.Summarize.Enable = false;
                    Check.True(RequestValidator.Validate(r).Any(e => e.Contains("Enable at least one function")), "image");

                    VideoAnalysisRequest v = new VideoAnalysisRequest { Video = new MediaSource { Url = "https://example.com/v.mp4" } };
                    Check.True(RequestValidator.Validate(v).Any(e => e.Contains("Enable at least one function")), "video");
                    v.Functions.Transcribe.Enable = true;
                    Check.Equal(0, RequestValidator.Validate(v).Count, "transcribe-only video is valid");
                    return Task.CompletedTask;
                }, new[] { "fast" }),

                new TestCaseDescriptor(Id, "SceneRules", "Scene detection bounds are validated", ct =>
                {
                    VideoAnalysisRequest v = new VideoAnalysisRequest { Video = new MediaSource { Url = "https://example.com/v.mp4" } };
                    v.Functions.Scenes.Enable = true;
                    v.Functions.Scenes.Summarize.Enable = true;
                    v.Functions.Scenes.Detection.MinSceneMs = 70000;
                    v.Functions.Scenes.Detection.MaxSceneMs = 60000;
                    Check.True(RequestValidator.Validate(v).Any(e => e.Contains("MinSceneMs must be less than MaxSceneMs")), "min max");
                    return Task.CompletedTask;
                }, new[] { "fast" })
            });
        }

        private static ImageAnalysisRequest Parse(string contextJson)
        {
            string json = "{ \"Image\": { \"Url\": \"https://example.com/a.jpg\" }, " + contextJson + ", \"Functions\": { \"Summarize\": { \"Enable\": true } } }";
            ImageAnalysisRequest? r = KeyframeSerializer.Deserialize<ImageAnalysisRequest>(json);
            Check.True(r != null, "parsed");
            return r!;
        }

        private static ImageAnalysisRequest Valid()
        {
            ImageAnalysisRequest r = new ImageAnalysisRequest { Image = new MediaSource { Url = "https://example.com/a.jpg" } };
            r.Functions.Summarize.Enable = true;
            return r;
        }
    }
}
