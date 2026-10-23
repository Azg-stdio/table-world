using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace Core.Rules
{
    public sealed class TableParseResult
    {
        /// <summary>Null only when the text is not a JSON object. May be partial when <see cref="Errors"/> is not empty.</summary>
        public TableState? State { get; }
        public IReadOnlyList<ValidationError> Errors { get; }
        public bool Ok => State != null && Errors.Count == 0;

        internal TableParseResult(TableState? state, IReadOnlyList<ValidationError> errors)
        {
            State = state;
            Errors = errors;
        }
    }

    /// <summary>
    /// Reads and writes <see cref="TableState"/> JSON. Reading is strict: unknown keys, unknown types and wrong value
    /// types are reported (all of them, with paths) instead of ignored. Semantic checks run only when the structure is valid.
    /// </summary>
    public static class TableJson
    {
        private static readonly IContractResolver Resolver = new TableContractResolver();

        private static readonly JsonSerializerSettings WriteSettings = new JsonSerializerSettings
        {
            ContractResolver = Resolver,
            NullValueHandling = NullValueHandling.Ignore,
            DefaultValueHandling = DefaultValueHandling.Ignore,
            Converters = CreateConverters(),
        };

        public static TableParseResult Parse(string json)
        {
            var errors = new List<ValidationError>();
            JToken root;
            try
            {
                using var reader = new JsonTextReader(new StringReader(json)) { DateParseHandling = DateParseHandling.None };
                root = JToken.ReadFrom(reader);
            }
            catch (JsonReaderException e)
            {
                errors.Add(new ValidationError(e.Path ?? "", ErrorCodes.InvalidJson, CleanMessage(e.Message)));
                return new TableParseResult(null, errors);
            }

            if (root.Type != JTokenType.Object)
            {
                errors.Add(new ValidationError("", ErrorCodes.InvalidJson, "The table document must be a JSON object."));
                return new TableParseResult(null, errors);
            }

            KeyChecker.Check(root, typeof(TableState), errors);

            var serializer = JsonSerializer.Create(new JsonSerializerSettings
            {
                ContractResolver = Resolver,
                Converters = CreateConverters(),
                DateParseHandling = DateParseHandling.None,
            });
            serializer.Error += (_, args) =>
            {
                // Newtonsoft raises the same error again on every parent; record it once, where it happened.
                if (args.CurrentObject != args.ErrorContext.OriginalObject)
                    return;
                errors.Add(new ValidationError(args.ErrorContext.Path ?? "", ErrorCodes.InvalidValue, CleanMessage(args.ErrorContext.Error.Message)));
                args.ErrorContext.Handled = true;
            };

            var state = root.ToObject<TableState>(serializer) ?? new TableState();
            state.Assets.RemoveAll(a => a == null);
            state.Objects.RemoveAll(o => o == null);
            state.Zones.RemoveAll(z => z == null);
            state.Players.Seats.RemoveAll(s => s == null);

            if (errors.Count == 0)
                errors.AddRange(TableValidator.Validate(state));
            return new TableParseResult(state, errors);
        }

        public static string Serialize(TableState state, bool indented = true) =>
            JsonConvert.SerializeObject(state, indented ? Formatting.Indented : Formatting.None, WriteSettings);

        /// <summary>Content hash of the canonical (compact) JSON: equal hashes mean equal tables.</summary>
        public static string Hash(TableState state)
        {
            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(Serialize(state, indented: false)));
            var hex = new StringBuilder("sha256:", 7 + bytes.Length * 2);
            foreach (var b in bytes)
                hex.Append(b.ToString("x2"));
            return hex.ToString();
        }

        private static List<JsonConverter> CreateConverters() => new List<JsonConverter>
        {
            new StringEnumConverter(new CamelCaseNamingStrategy(), allowIntegerValues: false),
            new PolymorphicConverter<TableObject>(TableObjectTypes.ByName),
            new PolymorphicConverter<Zone>(ZoneTypes.ByName),
        };

        /// <summary>Drops Newtonsoft's " Path 'x', line 1, position 2." suffix: the path is reported separately.</summary>
        private static string CleanMessage(string message)
        {
            var index = message.IndexOf(" Path '", StringComparison.Ordinal);
            return index >= 0 ? message.Substring(0, index) : message;
        }

        /// <summary>camelCase keys, dictionary keys untouched, empty collections not written.</summary>
        private sealed class TableContractResolver : DefaultContractResolver
        {
            public TableContractResolver()
            {
                NamingStrategy = new CamelCaseNamingStrategy { ProcessDictionaryKeys = false };
            }

            /// <summary>Explicit Order first (id, type), then base-class properties before derived ones.</summary>
            protected override IList<JsonProperty> CreateProperties(Type type, MemberSerialization memberSerialization) =>
                base.CreateProperties(type, memberSerialization)
                    .OrderBy(p => p.Order ?? 0)
                    .ThenBy(p => Depth(p.DeclaringType))
                    .ToList();

            private static int Depth(Type? type)
            {
                var depth = 0;
                for (var t = type; t != null; t = t.BaseType)
                    depth++;
                return depth;
            }

            protected override JsonProperty CreateProperty(MemberInfo member, MemberSerialization memberSerialization)
            {
                var property = base.CreateProperty(member, memberSerialization);
                var type = property.PropertyType;
                if (type != null && type != typeof(string) && typeof(ICollection).IsAssignableFrom(type))
                {
                    var existing = property.ShouldSerialize;
                    property.ShouldSerialize = owner =>
                        (existing == null || existing(owner)) &&
                        !(property.ValueProvider?.GetValue(owner) is ICollection { Count: 0 });
                }
                return property;
            }
        }

        /// <summary>Picks the concrete class from the <c>type</c> key. Unknown types read as null (KeyChecker reports them).</summary>
        private sealed class PolymorphicConverter<TBase> : JsonConverter where TBase : class
        {
            private readonly IReadOnlyDictionary<string, Type> _types;

            public PolymorphicConverter(IReadOnlyDictionary<string, Type> types)
            {
                _types = types;
            }

            public override bool CanConvert(Type objectType) => objectType == typeof(TBase);
            public override bool CanWrite => false;

            public override object? ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
            {
                if (reader.TokenType == JsonToken.Null)
                    return null;

                var path = reader.Path;
                var token = JToken.Load(reader);
                if (!(token is JObject jo))
                    throw new JsonSerializationException($"Expected an object for {typeof(TBase).Name}.");
                if (!(jo["type"] is JValue { Type: JTokenType.String } typeValue) || !_types.TryGetValue((string)typeValue!, out var concrete))
                    return null;

                var instance = Activator.CreateInstance(concrete);
                using var sub = new JTokenReader(jo, path);
                serializer.Populate(sub, instance);
                return instance;
            }

            public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer) =>
                throw new NotSupportedException();
        }

        /// <summary>Walks the raw JSON alongside the model's contracts and reports every key the model does not have.</summary>
        private static class KeyChecker
        {
            public static void Check(JToken token, Type type, List<ValidationError> errors)
            {
                type = Nullable.GetUnderlyingType(type) ?? type;
                if (token.Type == JTokenType.Null || typeof(JToken).IsAssignableFrom(type))
                    return;

                if (type == typeof(TableObject) || type == typeof(Zone))
                {
                    var registry = type == typeof(TableObject) ? TableObjectTypes.ByName : ZoneTypes.ByName;
                    if (!(token is JObject jo))
                        return;
                    var typeToken = jo["type"];
                    if (typeToken == null)
                    {
                        errors.Add(new ValidationError(jo.Path, ErrorCodes.MissingField, $"Missing 'type' for this {Noun(type)}.",
                            $"Use one of: {string.Join(", ", registry.Keys)}."));
                        return;
                    }
                    var name = typeToken.Type == JTokenType.String ? (string)typeToken! : typeToken.ToString();
                    if (!registry.TryGetValue(name, out var concrete))
                    {
                        errors.Add(new ValidationError(typeToken.Path, ErrorCodes.UnknownType, $"Unknown {Noun(type)} type '{name}'.",
                            Suggest.OneOf(name, registry.Keys)));
                        return;
                    }
                    type = concrete;
                }

                switch (Resolver.ResolveContract(type))
                {
                    case JsonObjectContract objectContract when objectContract.Converter == null && token is JObject obj:
                        foreach (var property in obj.Properties())
                        {
                            var match = objectContract.Properties.FirstOrDefault(p => !p.Ignored && p.PropertyName == property.Name);
                            if (match == null)
                            {
                                var valid = objectContract.Properties.Where(p => !p.Ignored).Select(p => p.PropertyName!);
                                errors.Add(new ValidationError(property.Path, ErrorCodes.UnknownKey,
                                    $"Unknown key '{property.Name}' on {type.Name}.", Suggest.OneOf(property.Name, valid)));
                            }
                            else if (match.PropertyType != null)
                            {
                                Check(property.Value, match.PropertyType, errors);
                            }
                        }
                        break;
                    case JsonArrayContract arrayContract when token is JArray array && arrayContract.CollectionItemType != null:
                        foreach (var item in array)
                            Check(item, arrayContract.CollectionItemType, errors);
                        break;
                    case JsonDictionaryContract dictionaryContract when token is JObject dict && dictionaryContract.DictionaryValueType != null:
                        foreach (var property in dict.Properties())
                            Check(property.Value, dictionaryContract.DictionaryValueType, errors);
                        break;
                }
            }

            private static string Noun(Type type) => type == typeof(Zone) ? "zone" : "object";
        }
    }
}
