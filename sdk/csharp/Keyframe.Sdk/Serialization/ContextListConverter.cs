namespace Keyframe.Sdk.Serialization
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using Keyframe.Sdk.Requests;

    /// <summary>
    /// Reads request context as a single string, an array of strings, an array of context item objects, or a mix.
    /// Always writes an array of context item objects.
    /// Thread safety: stateless and safe for concurrent use.
    /// </summary>
    public class ContextListConverter : JsonConverter<List<ContextItem>>
    {
        /// <inheritdoc />
        public override List<ContextItem>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            List<ContextItem> items = new List<ContextItem>();
            switch (reader.TokenType)
            {
                case JsonTokenType.Null:
                    return items;
                case JsonTokenType.String:
                    items.Add(new ContextItem { Text = reader.GetString() ?? string.Empty });
                    return items;
                case JsonTokenType.StartObject:
                    items.Add(ReadItem(ref reader, options));
                    return items;
                case JsonTokenType.StartArray:
                    while (reader.Read())
                    {
                        if (reader.TokenType == JsonTokenType.EndArray) return items;
                        if (reader.TokenType == JsonTokenType.Null) continue;
                        if (reader.TokenType == JsonTokenType.String) items.Add(new ContextItem { Text = reader.GetString() ?? string.Empty });
                        else if (reader.TokenType == JsonTokenType.StartObject) items.Add(ReadItem(ref reader, options));
                        else throw new JsonException("Context array entries must be strings or objects with Label and Text.");
                    }

                    throw new JsonException("Unterminated Context array.");
                default:
                    throw new JsonException("Context must be a string, an array of strings, or an array of objects with Label and Text.");
            }
        }

        /// <inheritdoc />
        public override void Write(Utf8JsonWriter writer, List<ContextItem> value, JsonSerializerOptions options)
        {
            writer.WriteStartArray();
            if (value != null)
            {
                foreach (ContextItem item in value)
                {
                    writer.WriteStartObject();
                    writer.WriteString("Label", item.Label);
                    writer.WriteString("Text", item.Text);
                    writer.WriteEndObject();
                }
            }

            writer.WriteEndArray();
        }

        private static ContextItem ReadItem(ref Utf8JsonReader reader, JsonSerializerOptions options)
        {
            ContextItem? item = JsonSerializer.Deserialize<ContextItem>(ref reader, options);
            return item ?? new ContextItem();
        }
    }
}
