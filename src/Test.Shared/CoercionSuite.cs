namespace Test.Shared
{
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Threading.Tasks;
    using Keyframe.Core.Analysis;
    using Keyframe.Core.Enums;
    using Keyframe.Core.Requests;
    using Keyframe.Core.Responses;
    using Touchstone.Core;

    /// <summary>
    /// Field coercion and model output parsing.
    /// </summary>
    public static class CoercionSuite
    {
        private const string Id = "Coercion";

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite.</returns>
        public static TestSuiteDescriptor Create()
        {
            return new TestSuiteDescriptor(Id, "Field coercion", new[]
            {
                new TestCaseDescriptor(Id, "Boolean", "Booleans accept JSON, words, and numbers", ct =>
                {
                    FieldSpec f = new FieldSpec { Key = "b", Type = FieldTypeEnum.Boolean, Prompt = "p" };
                    Check.Equal((object)true, Coerce(f, "true").Value, "true");
                    Check.Equal((object)false, Coerce(f, "\"No.\"").Value, "No.");
                    Check.Equal((object)true, Coerce(f, "1").Value, "1");
                    Check.Equal(false, Coerce(f, "\"not sure\"").Valid, "not sure is not a boolean");
                    return Task.CompletedTask;
                }, new[] { "fast" }),

                new TestCaseDescriptor(Id, "Integer", "Integers round, parse words, and clamp", ct =>
                {
                    FieldSpec f = new FieldSpec { Key = "n", Type = FieldTypeEnum.Integer, Prompt = "p", Minimum = 0, Maximum = 10 };
                    Check.Equal((object)2L, Coerce(f, "2").Value, "2");
                    Check.Equal((object)3L, Coerce(f, "\"three\"").Value, "three");
                    Check.Equal((object)4L, Coerce(f, "\"4 dogs\"").Value, "4 dogs");
                    FieldResult clamped = Coerce(f, "25");
                    Check.Equal((object)10L, clamped.Value, "clamped");
                    Check.True(clamped.Warnings.Count == 1, "clamp warning");
                    FieldResult rounded = Coerce(f, "2.6");
                    Check.Equal((object)3L, rounded.Value, "rounded");
                    return Task.CompletedTask;
                }, new[] { "fast" }),

                new TestCaseDescriptor(Id, "Enum", "Enums match options case-insensitively", ct =>
                {
                    FieldSpec f = new FieldSpec { Key = "s", Type = FieldTypeEnum.Enum, Prompt = "p", Options = new List<string> { "Indoors", "Outdoors" } };
                    Check.Equal((object)"Outdoors", Coerce(f, "\"outdoors.\"").Value, "match");
                    FieldResult bad = Coerce(f, "\"Underwater\"");
                    Check.Equal(false, bad.Valid, "no match is invalid");
                    Check.True(bad.Error != null && bad.Error.Contains("not one of"), "error text");
                    return Task.CompletedTask;
                }, new[] { "fast" }),

                new TestCaseDescriptor(Id, "List", "String lists accept arrays and comma text, and truncate", ct =>
                {
                    FieldSpec f = new FieldSpec { Key = "c", Type = FieldTypeEnum.StringList, Prompt = "p", MaxItems = 2 };
                    List<string>? fromArray = Coerce(f, "[\"red\", \"green\", \"blue\"]").Value as List<string>;
                    Check.True(fromArray != null && fromArray.Count == 2, "truncated to 2");
                    List<string>? fromText = Coerce(f, "\"red, green\"").Value as List<string>;
                    Check.True(fromText != null && fromText[1] == "green", "split");
                    return Task.CompletedTask;
                }, new[] { "fast" }),

                new TestCaseDescriptor(Id, "MissingAndDefault", "Missing values use the field default", ct =>
                {
                    FieldSpec f = new FieldSpec { Key = "d", Type = FieldTypeEnum.Integer, Prompt = "p", Default = 7 };
                    FieldResult missing = FieldCoercer.Coerce(f, false, default(JsonElement));
                    Check.Equal(false, missing.Valid, "invalid");
                    Check.Equal((object)7L, missing.Value, "default");
                    FieldResult nullValue = Coerce(f, "null");
                    Check.Equal((object)7L, nullValue.Value, "null uses default");
                    return Task.CompletedTask;
                }, new[] { "fast" }),

                new TestCaseDescriptor(Id, "ObjectSchema", "Object fields check required schema properties", ct =>
                {
                    FieldSpec f = new FieldSpec { Key = "o", Type = FieldTypeEnum.Object, Prompt = "p", Schema = "{\"required\":[\"make\"]}" };
                    Check.Equal(true, Coerce(f, "{\"make\":\"Acme\"}").Valid, "valid object");
                    Check.Equal(false, Coerce(f, "{\"model\":\"X\"}").Valid, "missing required");
                    return Task.CompletedTask;
                }, new[] { "fast" }),

                new TestCaseDescriptor(Id, "LenientParse", "Model output parsing strips fences and prose", ct =>
                {
                    bool ok = ModelOutputParser.TryParseObject("Sure! ```json\n{\"a\": 1, \"b\": [1,2],}\n``` hope that helps", out Dictionary<string, JsonElement> values, out string? error);
                    Check.True(ok, "parsed: " + error);
                    Check.Equal(1, values["A"].GetInt32(), "case-insensitive key lookup");
                    Check.Equal(false, ModelOutputParser.TryParseObject("{\"unterminated\": ", out values, out error), "malformed rejected");
                    return Task.CompletedTask;
                }, new[] { "fast" })
            });
        }

        private static FieldResult Coerce(FieldSpec field, string json)
        {
            using (JsonDocument doc = JsonDocument.Parse(json))
            {
                return FieldCoercer.Coerce(field, true, doc.RootElement.Clone());
            }
        }
    }
}
