using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace Core.Rules
{
    /// <summary>
    /// JSON Schema (draft 2020-12) of the game document, generated from the same contracts the strict reader uses, so
    /// docs/rules-schema.v0.json cannot drift from the code. Semantic rules (references, ids) are only in the validator.
    /// </summary>
    public static class SchemaGenerator
    {
        public static JObject Generate()
        {
            var defs = new JObject();
            var root = new JObject
            {
                ["$schema"] = "https://json-schema.org/draft/2020-12/schema",
                ["$id"] = "https://table-world/rules-schema.v0.json",
                ["title"] = "Table World game document v" + TableState.CurrentSchemaVersion,
                ["$ref"] = Ref(typeof(TableState), defs),
                ["$defs"] = defs,
            };
            return root;
        }

        private static string Ref(Type type, JObject defs)
        {
            var name = type.Name;
            if (defs[name] == null)
            {
                defs[name] = new JObject();   // placeholder against recursion
                defs[name] = Define(type, defs);
            }
            return "#/$defs/" + name;
        }

        private static JObject Define(Type type, JObject defs)
        {
            if (type == typeof(TableObject) || type == typeof(Zone))
            {
                var registry = type == typeof(TableObject) ? TableObjectTypes.ByName : ZoneTypes.ByName;
                return new JObject { ["oneOf"] = new JArray(registry.Values.Select(t => new JObject { ["$ref"] = Ref(t, defs) })) };
            }
            if (type == typeof(Effect) || type == typeof(Condition))
            {
                var kinds = type == typeof(Effect) ? EffectKinds.ByName : ConditionKinds.ByName;
                return new JObject
                {
                    ["oneOf"] = new JArray(kinds.Select(p => new JObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JObject { [p.Key] = KindBody(p.Value, defs) },
                        ["required"] = new JArray(p.Key),
                        ["additionalProperties"] = false,
                    })),
                };
            }

            var contract = (JsonObjectContract)TableJson.ContractResolver.ResolveContract(type);
            var properties = new JObject();
            foreach (var property in contract.Properties.Where(p => !p.Ignored && p.PropertyType != null))
            {
                if (property.PropertyName == "type" && (typeof(TableObject).IsAssignableFrom(type) || typeof(Zone).IsAssignableFrom(type)))
                    properties["type"] = new JObject { ["const"] = type.Name };
                else
                    properties[property.PropertyName!] = Schema(property.PropertyType!, defs);
            }
            var schema = new JObject { ["type"] = "object", ["properties"] = properties, ["additionalProperties"] = false };
            if (properties["type"] is JObject { } typeSchema && typeSchema["const"] != null)
                schema["required"] = new JArray("type");
            return schema;
        }

        private static JToken KindBody(Type kind, JObject defs)
        {
            if (ConditionKinds.IsListForm(kind))
                return new JObject { ["type"] = "array", ["items"] = new JObject { ["$ref"] = Ref(typeof(Condition), defs) } };
            if (kind == typeof(NotCondition))
                return new JObject { ["$ref"] = Ref(typeof(Condition), defs) };
            return new JObject { ["$ref"] = Ref(kind, defs) };
        }

        private static JObject Schema(Type type, JObject defs)
        {
            type = Nullable.GetUnderlyingType(type) ?? type;
            if (type == typeof(string)) return new JObject { ["type"] = "string" };
            if (type == typeof(bool)) return new JObject { ["type"] = "boolean" };
            if (type == typeof(int) || type == typeof(long)) return new JObject { ["type"] = "integer" };
            if (type == typeof(ulong)) return new JObject { ["type"] = "integer", ["minimum"] = 0 };
            if (type == typeof(float) || type == typeof(double)) return new JObject { ["type"] = "number" };
            if (type == typeof(Vec3)) return NumberArray(3);
            if (type == typeof(Vec2)) return NumberArray(2);
            if (type == typeof(JObject)) return new JObject { ["type"] = "object" };
            if (typeof(JToken).IsAssignableFrom(type)) return new JObject();
            if (type.IsEnum)
                return new JObject { ["enum"] = new JArray(Enum.GetNames(type).Select(n => char.ToLowerInvariant(n[0]) + n.Substring(1))) };
            if (type.IsArray)
                return new JObject { ["type"] = "array", ["items"] = Schema(type.GetElementType()!, defs) };
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Dictionary<,>))
                return new JObject { ["type"] = "object", ["additionalProperties"] = Schema(type.GetGenericArguments()[1], defs) };
            if (type.IsGenericType && typeof(IEnumerable).IsAssignableFrom(type))
                return new JObject { ["type"] = "array", ["items"] = Schema(type.GetGenericArguments()[0], defs) };
            return new JObject { ["$ref"] = Ref(type, defs) };
        }

        private static JObject NumberArray(int length) => new JObject
        {
            ["type"] = "array",
            ["items"] = new JObject { ["type"] = "number" },
            ["minItems"] = length,
            ["maxItems"] = length,
        };
    }
}
