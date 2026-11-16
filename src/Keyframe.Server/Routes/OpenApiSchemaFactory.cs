namespace Keyframe.Server.Routes
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;
    using System.Text.Json.Serialization;
    using WatsonWebserver.Core.OpenApi;

    /// <summary>
    /// Builds OpenAPI schemas from C# types by reflection: PascalCase property names (matching the serializer), enums
    /// as string lists, depth-limited. Thread safety: safe for concurrent use.
    /// </summary>
    public static class OpenApiSchemaFactory
    {
        #region Private-Members

        private const int MaxDepth = 5;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build a schema for a type.
        /// </summary>
        /// <param name="type">Type.</param>
        /// <returns>Schema.</returns>
        /// <exception cref="ArgumentNullException">Thrown when type is null.</exception>
        public static OpenApiSchemaMetadata For(Type type)
        {
            if (type == null) throw new ArgumentNullException(nameof(type));
            return Build(type, 0);
        }

        #endregion

        #region Private-Methods

        private static OpenApiSchemaMetadata Build(Type type, int depth)
        {
            Type? underlying = Nullable.GetUnderlyingType(type);
            if (underlying != null) type = underlying;

            if (type == typeof(object))
            {
                OpenApiSchemaMetadata any = new OpenApiSchemaMetadata();
                any.Description = "Any JSON value.";
                return any;
            }

            if (type == typeof(string)) return OpenApiSchemaMetadata.String();
            if (type == typeof(bool)) return OpenApiSchemaMetadata.Boolean();
            if (type == typeof(int) || type == typeof(short)) return OpenApiSchemaMetadata.Integer("int32");
            if (type == typeof(long)) return OpenApiSchemaMetadata.Integer("int64");
            if (type == typeof(double) || type == typeof(float) || type == typeof(decimal)) return OpenApiSchemaMetadata.Number("double");
            if (type == typeof(DateTime) || type == typeof(DateTimeOffset)) return OpenApiSchemaMetadata.String("date-time");
            if (type.IsEnum)
            {
                OpenApiSchemaMetadata e = OpenApiSchemaMetadata.String();
                e.Enum = Enum.GetNames(type).Cast<object>().ToList();
                return e;
            }

            if (typeof(IDictionary).IsAssignableFrom(type))
            {
                OpenApiSchemaMetadata map = OpenApiSchemaMetadata.Create("object", null!);
                map.Description = "Map keyed by string.";
                return map;
            }

            if (typeof(IEnumerable).IsAssignableFrom(type))
            {
                Type element = type.IsArray ? type.GetElementType()! : (type.IsGenericType ? type.GetGenericArguments()[0] : typeof(object));
                return OpenApiSchemaMetadata.CreateArray(depth >= MaxDepth ? OpenApiSchemaMetadata.Create("object", null!) : Build(element, depth + 1));
            }

            OpenApiSchemaMetadata schema = OpenApiSchemaMetadata.Create("object", null!);
            if (depth >= MaxDepth) return schema;

            schema.Properties = new Dictionary<string, OpenApiSchemaMetadata>();
            foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (property.GetCustomAttribute<JsonIgnoreAttribute>() != null) continue;
                if (!property.CanRead || property.GetIndexParameters().Length > 0) continue;
                JsonPropertyNameAttribute? named = property.GetCustomAttribute<JsonPropertyNameAttribute>();
                string name = named != null ? named.Name : property.Name;
                schema.Properties[name] = Build(property.PropertyType, depth + 1);
            }

            return schema;
        }

        #endregion
    }
}
