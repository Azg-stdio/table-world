namespace Core.Tests.Rules
{
    /// <summary>Hand-written table documents shared by the Rules tests.</summary>
    internal static class SampleTables
    {
        /// <summary>One of every object and zone type, two seats, bag contents, a state, a mixed-sheet deck.</summary>
        public const string AllTypes = @"{
  ""schemaVersion"": ""0.1"",
  ""assets"": [
    { ""id"": ""std_faces"", ""type"": ""image"", ""path"": ""builtin:standard_deck"", ""columns"": 10, ""rows"": 7 },
    { ""id"": ""std_back"", ""type"": ""image"", ""path"": ""builtin:standard_back"" },
    { ""id"": ""promo"", ""type"": ""image"", ""path"": ""assets/promo.png"", ""columns"": 2, ""rows"": 1 },
    { ""id"": ""coin"", ""type"": ""image"", ""path"": ""assets/coin.png"" },
    { ""id"": ""dragon"", ""type"": ""model"", ""path"": ""assets/dragon.glb"", ""hash"": ""sha256:00"" }
  ],
  ""objects"": [
    { ""id"": ""deck_main"", ""type"": ""Deck"", ""name"": ""Main deck"", ""faceAsset"": ""std_faces"", ""backAsset"": ""std_back"",
      ""transform"": { ""position"": [0, 0, 0], ""rotation"": [0, 0, 180] },
      ""cards"": [
        { ""faceIndex"": 0, ""name"": ""Ace of hearts"", ""meta"": { ""rank"": 14, ""suit"": ""hearts"" } },
        { ""id"": ""promo_card"", ""faceAsset"": ""promo"", ""faceIndex"": 1, ""tags"": [""promo""] }
      ] },
    { ""id"": ""loose_card"", ""type"": ""Card"", ""faceAsset"": ""std_faces"", ""faceIndex"": 51, ""backAsset"": ""std_back"",
      ""size"": [0.0635, 0.0889], ""gmNotes"": ""secret"", ""meta"": { ""rank"": 2 } },
    { ""id"": ""coin_1"", ""type"": ""Token"", ""asset"": ""coin"", ""shape"": ""round"", ""thickness"": 0.003, ""stackable"": true, ""tint"": [1, 0.8, 0] },
    { ""id"": ""hex_1"", ""type"": ""Tile"", ""shape"": ""hex"", ""locked"": true },
    { ""id"": ""d20"", ""type"": ""Die"", ""faces"": 20, ""value"": 17 },
    { ""id"": ""d6_labeled"", ""type"": ""Die"", ""faces"": 6, ""labels"": [""a"", ""b"", ""c"", ""d"", ""e"", ""f""] },
    { ""id"": ""dragon_1"", ""type"": ""Model"", ""asset"": ""dragon"", ""kind"": ""figurine"", ""collider"": ""box"", ""material"": ""metal"",
      ""states"": { ""2"": { ""id"": ""dragon_1_alt"", ""type"": ""Figure"", ""height"": 0.05 } } },
    { ""id"": ""bag_1"", ""type"": ""Bag"", ""transform"": { ""position"": [0.4, 0, 0], ""scale"": [1.5, 1.5, 1.5] } },
    { ""id"": ""bagged_die"", ""type"": ""Die"", ""container"": ""bag_1"" },
    { ""id"": ""coins"", ""type"": ""InfiniteBag"" },
    { ""id"": ""coin_template"", ""type"": ""Token"", ""container"": ""coins"", ""ttsExtra"": { ""Sticky"": true } },
    { ""id"": ""board"", ""type"": ""Board"", ""size"": [0.6, 0.4] },
    { ""id"": ""hero"", ""type"": ""Figure"", ""script"": ""function onLoad() end"" }
  ],
  ""zones"": [
    { ""id"": ""hand_p1"", ""type"": ""HandZone"", ""owner"": ""seat_1"", ""size"": [0.4, 0.1, 0.2] },
    { ""id"": ""hand_p2"", ""type"": ""HandZone"", ""owner"": ""seat_2"", ""size"": [0.4, 0.1, 0.2] },
    { ""id"": ""battlefield"", ""type"": ""ScriptingZone"", ""size"": [0.3, 0.1, 0.3] },
    { ""id"": ""gm_area"", ""type"": ""HiddenZone"", ""visibleTo"": [""host""] },
    { ""id"": ""snap_discard"", ""type"": ""SnapPoint"", ""rotationSnap"": true, ""parent"": ""board"" }
  ],
  ""players"": { ""seats"": [
    { ""id"": ""seat_1"", ""color"": ""red"", ""handZone"": ""hand_p1"" },
    { ""id"": ""seat_2"", ""color"": ""blue"", ""handZone"": ""hand_p2"" }
  ] }
}";

        /// <summary>Wraps object JSON in an otherwise valid table with one image sheet ('sheet', 10×7) and one back ('back').</summary>
        public static string WithObjects(string objectsJson, string zonesJson = "", string seatsJson = "") => @"{
  ""schemaVersion"": ""0.1"",
  ""assets"": [
    { ""id"": ""sheet"", ""type"": ""image"", ""path"": ""a.png"", ""columns"": 10, ""rows"": 7 },
    { ""id"": ""back"", ""type"": ""image"", ""path"": ""b.png"" },
    { ""id"": ""mesh"", ""type"": ""model"", ""path"": ""m.glb"" }
  ],
  ""objects"": [" + objectsJson + @"],
  ""zones"": [" + zonesJson + @"],
  ""players"": { ""seats"": [" + seatsJson + @"] }
}";
    }
}
