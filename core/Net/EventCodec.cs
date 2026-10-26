using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using Core.Engine;
using Core.Rules;
using Newtonsoft.Json.Linq;

namespace Core.Net
{
    /// <summary>
    /// State events on the wire: <c>{ "type": "DieRolled", "fields": { ... } }</c>. A host-run order (ADR 0012) carries
    /// the events the host's command produced, since peers do not replay it: the log and the view's animations (a roll
    /// landing on its value, a deck spinning) read them. Events are immutable, so their fields are read and written
    /// directly, base class included.
    /// </summary>
    public static class EventCodec
    {
        public static JObject Encode(StateEvent e)
        {
            var fields = new JObject();
            foreach (var field in Fields(e.GetType()))
                if (field.GetValue(e) is { } value)
                    fields[Key(field)] = TableJson.ToToken(value);
            var message = new JObject { ["type"] = e.GetType().Name };
            if (fields.Count > 0)
                message["fields"] = fields;
            return message;
        }

        public static StateEvent Decode(JObject message)
        {
            var name = (string?)message["type"] ?? throw new FormatException("Event without type.");
            var type = typeof(StateEvent).Assembly.GetType("Core.Engine." + name);
            if (type == null || !typeof(StateEvent).IsAssignableFrom(type) || type.IsAbstract)
                throw new FormatException($"Unknown event type '{name}'.");
            var e = (StateEvent)FormatterServices.GetUninitializedObject(type);
            var fields = message["fields"] as JObject ?? new JObject();
            foreach (var field in Fields(type))
                field.SetValue(e, fields[Key(field)] is { } token ? TableJson.FromToken(token, field.FieldType) : null);
            return e;
        }

        public static JArray EncodeAll(IEnumerable<StateEvent> events) => new JArray(events.Select(Encode));

        public static List<StateEvent> DecodeAll(JToken? events) =>
            (events as JArray ?? new JArray()).OfType<JObject>().Select(Decode).ToList();

        /// <summary>A property's backing field goes by the property's name (<c>&lt;ObjectId&gt;k__BackingField</c> is <c>ObjectId</c>).</summary>
        private static string Key(FieldInfo field) =>
            field.Name.StartsWith("<") && field.Name.IndexOf('>') is var end && end > 1 ? field.Name.Substring(1, end - 1) : field.Name;

        private static IEnumerable<FieldInfo> Fields(Type type)
        {
            for (var t = type; t != null && t != typeof(object); t = t.BaseType)
                foreach (var field in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    yield return field;
        }
    }
}
