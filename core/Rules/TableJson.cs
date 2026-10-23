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

        /// <summary>The contracts the reader and writer use (camelCase keys, empty collections omitted).</summary>
        internal static IContractResolver ContractResolver => Resolver;

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

        /// <summary>Deep copy of one object through its JSON form (used for InfiniteBag clones).</summary>
        internal static T CloneObject<T>(T obj) where T : TableObject
        {
            var token = JToken.FromObject(obj, JsonSerializer.Create(WriteSettings));
            var serializer = JsonSerializer.Create(new JsonSerializerSettings { ContractResolver = Resolver, Converters = CreateConverters() });
            return (T)token.ToObject<TableObject>(serializer)!;
        }

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
            new SingleKeyConverter<Effect>(EffectKinds.ByName),
            new SingleKeyConverter<Condition>(ConditionKinds.ByName),
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

        /// <summary>
        /// <c>{ "kind": { fields } }</c> for effects and conditions. <c>and</c>/<c>or</c> hold an array, <c>not</c> one condition.
        /// Malformed entries read as null (KeyChecker reports them).
        /// </summary>
        private sealed class SingleKeyConverter<TBase> : JsonConverter where TBase : class
        {
            private readonly IReadOnlyDictionary<string, Type> _kinds;

            public SingleKeyConverter(IReadOnlyDictionary<string, Type> kinds)
            {
                _kinds = kinds;
            }

            // Writing matches converters on the runtime type (MoveEffect), reading on the declared one (Effect).
            public override bool CanConvert(Type objectType) => typeof(TBase).IsAssignableFrom(objectType);

            public override object? ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
            {
                if (reader.TokenType == JsonToken.Null)
                    return null;
                var path = reader.Path;
                if (!(JToken.Load(reader) is JObject jo) || jo.Count != 1)
                    return null;
                var property = jo.Properties().First();
                if (!_kinds.TryGetValue(property.Name, out var concrete))
                    return null;

                var instance = Activator.CreateInstance(concrete);
                if (ConditionKinds.IsListForm(concrete))
                {
                    var list = new List<Condition>();
                    if (property.Value is JArray array)
                        foreach (var item in array)
                            if (item.ToObject<Condition>(serializer) is { } c)
                                list.Add(c);
                    if (instance is AndCondition and)
                        and.Conditions = list;
                    else
                        ((OrCondition)instance).Conditions = list;
                    return instance;
                }
                if (instance is NotCondition not)
                {
                    not.Condition = property.Value.ToObject<Condition>(serializer);
                    return instance;
                }
                if (property.Value is JObject inner)
                {
                    using var sub = new JTokenReader(inner, path.Length == 0 ? property.Name : path + "." + property.Name);
                    serializer.Populate(sub, instance);
                }
                return instance;
            }

            public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
            {
                writer.WriteStartObject();
                switch (value)
                {
                    case AndCondition and:
                        writer.WritePropertyName("and");
                        serializer.Serialize(writer, and.Conditions);
                        break;
                    case OrCondition or:
                        writer.WritePropertyName("or");
                        serializer.Serialize(writer, or.Conditions);
                        break;
                    case NotCondition not:
                        writer.WritePropertyName("not");
                        serializer.Serialize(writer, not.Condition);
                        break;
                    default:
                        writer.WritePropertyName(value is Effect e ? e.Kind : ((Condition)value!).Kind);
                        // The concrete type with its own contract; this converter only handles the base type.
                        var contract = (JsonObjectContract)serializer.ContractResolver.ResolveContract(value!.GetType());
                        writer.WriteStartObject();
                        foreach (var property in contract.Properties)
                        {
                            if (property.Ignored || !property.Readable || (property.ShouldSerialize != null && !property.ShouldSerialize(value)))
                                continue;
                            var v = property.ValueProvider!.GetValue(value);
                            if (v == null || (property.PropertyType != null && property.PropertyType.IsValueType && Equals(v, Activator.CreateInstance(property.PropertyType))))
                                continue;
                            writer.WritePropertyName(property.PropertyName!);
                            serializer.Serialize(writer, v);
                        }
                        writer.WriteEndObject();
                        break;
                }
                writer.WriteEndObject();
            }
        }

        /// <summary>Walks the raw JSON alongside the model's contracts and reports every key the model does not have.</summary>
        private static class KeyChecker
        {
            public static void Check(JToken token, Type type, List<ValidationError> errors)
            {
                type = Nullable.GetUnderlyingType(type) ?? type;
                if (token.Type == JTokenType.Null || typeof(JToken).IsAssignableFrom(type))
                    return;

                if (type == typeof(Effect) || type == typeof(Condition))
                {
                    CheckSingleKey(token, type, errors);
                    return;
                }

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

            private static void CheckSingleKey(JToken token, Type baseType, List<ValidationError> errors)
            {
                var kinds = baseType == typeof(Effect) ? EffectKinds.ByName : ConditionKinds.ByName;
                var noun = baseType == typeof(Effect) ? "effect" : "condition";
                if (!(token is JObject jo) || jo.Count != 1)
                {
                    errors.Add(new ValidationError(token.Path, ErrorCodes.InvalidValue,
                        $"An {noun} is an object with exactly one key naming its kind.", $"Kinds: {string.Join(", ", kinds.Keys)}."));
                    return;
                }
                var property = jo.Properties().First();
                if (!kinds.TryGetValue(property.Name, out var concrete))
                {
                    errors.Add(new ValidationError(property.Path, baseType == typeof(Effect) ? ErrorCodes.UnknownEffect : ErrorCodes.UnknownCondition,
                        $"Unknown {noun} '{property.Name}'.", Suggest.OneOf(property.Name, kinds.Keys)));
                    return;
                }
                if (ConditionKinds.IsListForm(concrete))
                {
                    if (property.Value is JArray array)
                        foreach (var item in array)
                            Check(item, typeof(Condition), errors);
                    else
                        errors.Add(new ValidationError(property.Path, ErrorCodes.InvalidValue, $"'{property.Name}' takes an array of conditions."));
                    return;
                }
                if (concrete == typeof(NotCondition))
                {
                    Check(property.Value, typeof(Condition), errors);
                    return;
                }
                if (property.Value.Type != JTokenType.Object)
                {
                    errors.Add(new ValidationError(property.Path, ErrorCodes.InvalidValue, $"'{property.Name}' takes an object (use {{}} when it has no fields)."));
                    return;
                }
                Check(property.Value, concrete, errors);
            }
        }
    }
}
