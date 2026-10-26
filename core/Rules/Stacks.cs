namespace Core.Rules
{
    /// <summary>
    /// Counted stacks (G14): stackable tokens and tiles that are the same piece merge into one object with a count, like
    /// TTS chip stacks. "The same piece" means everything but id, position, count, lock and container is equal.
    /// </summary>
    public static class Stacks
    {
        public const int MaxCount = 500;

        public static bool IsStackable(TableObject obj) => obj is Token { Stackable: true } || obj is Tile { Stackable: true };

        /// <summary>How many pieces the object is: its count when stackable, else 1.</summary>
        public static int CountOf(TableObject obj) => obj switch
        {
            Token t => t.Count,
            Tile t => t.Count,
            _ => 1,
        };

        public static void SetCount(TableObject obj, int count)
        {
            switch (obj)
            {
                case Token t: t.Count = count; break;
                case Tile t: t.Count = count; break;
            }
        }

        /// <summary>True when <paramref name="a"/> and <paramref name="b"/> are two stackable pieces of the same kind.</summary>
        public static bool SameKind(TableObject a, TableObject b)
        {
            if (a == b || !IsStackable(a) || !IsStackable(b) || a.GetType() != b.GetType() || a.States.Count > 0 || b.States.Count > 0)
                return false;
            return Newtonsoft.Json.Linq.JToken.DeepEquals(Signature(a), Signature(b));
        }

        /// <summary>
        /// True when <paramref name="a"/> is the same piece as <paramref name="b"/>, stackable or not: what an infinite bag
        /// takes back from its template (a coin from the bank). Copies get new ids for their states too, so states are
        /// compared without them.
        /// </summary>
        public static bool SamePiece(TableObject a, TableObject b)
        {
            if (a == b || a.GetType() != b.GetType())
                return false;
            var sa = Signature(a);
            var sb = Signature(b);
            foreach (var json in new[] { sa, sb })
                if (json["states"] is Newtonsoft.Json.Linq.JObject states)
                    foreach (var state in states.Properties())
                        (state.Value as Newtonsoft.Json.Linq.JObject)?.Remove("id");
            return Newtonsoft.Json.Linq.JToken.DeepEquals(sa, sb);
        }

        private static Newtonsoft.Json.Linq.JObject Signature(TableObject obj)
        {
            var json = TableJson.ToJson(obj);
            foreach (var key in new[] { "id", "transform", "count", "locked", "container" })
                json.Remove(key);
            return json;
        }
    }
}
