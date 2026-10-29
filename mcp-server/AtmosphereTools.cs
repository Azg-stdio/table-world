using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using Core.Music;
using Core.Rules;
using ModelContextProtocol.Server;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace TableWorld.Mcp
{
    /// <summary>
    /// A game's place and mood (M12): its room, light, panels, music and sounds. The music comes from the player's own
    /// music library (packs they imported), never shipped with the game: a game names its tracks, and players who lack
    /// a pack receive the tracks from the host while playing.
    /// </summary>
    [McpServerToolType]
    public static class AtmosphereTools
    {
        /// <summary>The library the tools read; tests point it at a folder of their own.</summary>
        internal static string? LibraryFolder { get; set; }

        private static MusicLibrary Library() => new MusicLibrary(LibraryFolder);

        [McpServerTool(Name = "list_music"), Description("The player's music library: every pack they imported and its tracks, each with its address (library:<pack>/<track>), title, length in seconds and tags (ambient, dark, action, loop, fx...). Use it to pick a game's soundtrack: ambient or loop tracks under talk and play, dark ones for tense moments (a night, a betrayal), fx for short stingers (with a sound effect). search matches words in titles, packs and tags; tag keeps one tag. The music is the player's, not the game's: games name tracks and players who lack them receive them while playing.")]
        public static string ListMusic(
            [Description("Words to match in titles, pack names and tags, e.g. 'dark' or 'ambient loop'.")] string? search = null,
            [Description("Only tracks with this tag, e.g. loop.")] string? tag = null,
            [Description("At most this many tracks (default 80).")] int limit = 80)
        {
            var library = Library();
            var found = library.Search(search, tag).ToList();
            return GameTools.Json(new
            {
                ok = true, folder = library.Folder,
                packs = library.Packs.Select(p => new { id = p.Id, name = p.Name, author = p.Author, source = p.Source, tracks = p.Tracks.Count }),
                tracks = found.Take(Math.Max(1, limit)).Select(f => new
                {
                    url = MusicLibrary.Url(f.Pack, f.Track), title = f.Track.Title, pack = f.Pack.Name, seconds = f.Track.Seconds, tags = f.Track.Tags,
                }),
                more = found.Count > limit ? found.Count - limit : (int?)null,
                hint = library.Packs.Count == 0
                    ? "The library is empty. Ask the player for a music pack they may use (a zip or a folder of MP3/OGG/WAV files) and import it with import_music."
                    : null,
            });
        }

        [McpServerTool(Name = "import_music"), Description("Import a music pack into the player's music library from a zip or a folder of audio files (MP3, OGG, WAV): one file per track in its smallest format, loops kept as their own tracks, the pack's licence file kept beside them. Only import music the player has the right to use: ask them, then pass rightsConfirmed true. Importing the same pack again replaces it. Replies with the pack and its tracks.")]
        public static string ImportMusic(
            [Description("Full path of the zip or folder on this computer.")] string path,
            [Description("The player confirmed they may use this music (its licence allows it).")] bool rightsConfirmed = false,
            [Description("The pack's name; default the zip's or folder's name.")] string? name = null,
            [Description("Who made it, when the pack does not say.")] string? author = null,
            [Description("Where to get it (the author's page), for friends who want it too.")] string? source = null)
        {
            if (!rightsConfirmed)
                return GameTools.Json(new { ok = false, error = "Import only music the player may use.", hint = "Ask the player whether the pack's licence lets them use it in their games; then pass rightsConfirmed true." });
            try
            {
                var pack = Library().Import(path, true, name, author, source);
                return GameTools.Json(new
                {
                    ok = true, pack = new { id = pack.Id, name = pack.Name, author = pack.Author, license = pack.LicenseFile },
                    tracks = pack.Tracks.Select(t => new { url = MusicLibrary.Prefix + pack.Id + "/" + t.Id, title = t.Title, seconds = t.Seconds, tags = t.Tags }),
                });
            }
            catch (Exception e) when (e is IOException || e is InvalidDataException || e is UnauthorizedAccessException || e is InvalidOperationException)
            {
                return GameTools.Json(new { ok = false, error = e.Message });
            }
        }

        [McpServerTool(Name = "set_music"), Description("The game's soundtrack: named playlists of tracks (library:<pack>/<track> from list_music, an audio asset's id, or a web address), e.g. {\"tavern\":[\"library:fantasy/ambient-1-loop\",...],\"ritual\":[...]}, which playlist starts with the game, and its volume (0-1, under each player's music volume). Merges with the playlists already there; an empty list removes one. Phases and actions change the music with the music effect: {\"music\":{\"playlist\":\"ritual\",\"crossfade\":3}} (also track, pause, resume, stop, volume); a playlist already playing is left as it is. Mark moments with the sound effect: {\"sound\":{\"play\":\"gong\",\"to\":\"awake\"}} (built-in cues gong, chime, bell, drum, sting, fanfare, page, whoosh, knock; or a library track or audio asset).")]
        public static string SetMusic(GameSession session,
            [Description("JSON object: playlist name -> array of track addresses (or {url, title}).")] string? playlistsJson = null,
            [Description("The playlist the game starts with.")] string? start = null,
            [Description("Play each playlist in a random order.")] bool? shuffle = null,
            [Description("Play the same track again when it ends.")] bool? repeat = null,
            [Description("The game's music level, 0-1.")] float? volume = null) =>
            session.Locked(() =>
            {
                var library = Library();
                var missing = new List<string>();
                var result = session.Edit(doc =>
                {
                    var music = doc["music"] as JObject ?? new JObject();
                    if (playlistsJson != null)
                    {
                        JObject lists;
                        try
                        {
                            lists = JObject.Parse(playlistsJson);
                        }
                        catch (JsonException e)
                        {
                            throw new EditException(new ValidationError("playlistsJson", ErrorCodes.InvalidValue, "Not a JSON object: " + e.Message));
                        }
                        var all = music["playlists"] as JObject ?? new JObject();
                        foreach (var pair in lists)
                        {
                            if (!(pair.Value is JArray tracks) || tracks.Count == 0)
                            {
                                all.Remove(pair.Key);
                                continue;
                            }
                            var list = new JArray();
                            foreach (var entry in tracks)
                            {
                                var url = entry.Type == JTokenType.String ? (string)entry! : (string?)entry["url"] ?? "";
                                var track = new JObject { ["url"] = url };
                                var title = entry is JObject o ? (string?)o["title"] : null;
                                if (library.Find(url) is { } found)
                                {
                                    track["title"] = title ?? found.Track.Title;
                                    track["hash"] = found.Track.Hash;
                                }
                                else
                                {
                                    if (title != null)
                                        track["title"] = title;
                                    if (MusicLibrary.IsLibraryUrl(url))
                                        missing.Add(url);
                                }
                                list.Add(track);
                            }
                            all[pair.Key] = list;
                        }
                        music["playlists"] = all;
                    }
                    if (start != null)
                        music["start"] = start;
                    if (shuffle != null)
                        music["shuffle"] = shuffle;
                    if (repeat != null)
                        music["repeat"] = repeat;
                    if (volume != null)
                        music["volume"] = volume;
                    doc["music"] = music;
                });
                return GameTools.Json(new
                {
                    ok = result.Ok, problems = GameTools.ErrorList(result.Errors), savedTo = session.FilePath,
                    notInLibrary = missing.Count > 0 ? missing : null,
                    hint = missing.Count > 0 ? "These tracks are not in this computer's music library (list_music): the game names them, but nobody here can play them until the pack is imported." : null,
                });
            });

        [McpServerTool(Name = "preview_music"), Description("Play a track in the game window so the player hears it while you build (only on this computer, the table's music unchanged): a library track, an audio asset's id, or a web address. stop true stops the preview. Needs the game window open.")]
        public static string PreviewMusic(GameSession session,
            [Description("The track to hear.")] string? track = null,
            [Description("Stop the preview.")] bool stop = false)
        {
            if (!session.Remote)
                return GameTools.Json(new { ok = false, error = "No game window is connected. Start Table World and it connects within a few seconds." });
            string? path = null;
            if (!stop && track != null && MusicLibrary.IsLibraryUrl(track))
            {
                path = Library().PathOf(track);
                if (path == null)
                    return GameTools.Json(new { ok = false, error = $"'{track}' is not in the music library.", hint = "list_music shows what is there." });
            }
            var reply = session.Bridge!.Request(new JObject { ["type"] = "preview_music", ["url"] = stop ? null : track, ["path"] = path }).GetAwaiter().GetResult();
            return GameTools.Json(new { ok = reply != null && (bool?)reply["ok"] == true, error = (string?)reply?["error"], playing = stop ? null : track });
        }

        [McpServerTool(Name = "set_environment"), Description("The place the table sits in and how the game looks: room gameNight (a dim room with a warm lamp, the default), tavern (stone, beams, a chandelier of candles, candles on the walls, a fire in the hearth: medieval and fantasy games), karaoke, or none; theme default or parchment (parchment panels, ink text and gold titles for the game's panels); wallColor/floorColor [r,g,b] 0-1; lighting {ambientIntensity, ambientColor, lampIntensity, lampColor, tableLightIntensity, tableLightColor}; table {feltColor, woodColor, feltAsset, size [w,d]}; look {ambientOcclusion, fog, bloom, vignette} (0-1 each, the room's post-processing). Pieces keep their true colours under any mood. Merges with what is there. Change light while playing with the light effect: {\"light\":{\"part\":\"room\",\"scale\":0.1,\"tint\":[0.6,0.7,1],\"seconds\":2}}. Check the result with screenshot.")]
        public static string SetEnvironment(GameSession session,
            [Description("gameNight, tavern, karaoke or none.")] string? room = null,
            [Description("default or parchment.")] string? theme = null,
            [Description("JSON: wallColor, floorColor, lighting, table and look, any of them, e.g. {\"table\":{\"feltColor\":[0.3,0.07,0.07]}}.")] string? json = null) =>
            session.Locked(() =>
            {
                var result = session.Edit(doc =>
                {
                    var environment = doc["environment"] as JObject ?? new JObject();
                    if (room != null)
                        environment["room"] = room;
                    if (theme != null)
                        environment["theme"] = theme;
                    if (json != null)
                    {
                        JObject change;
                        try
                        {
                            change = JObject.Parse(json);
                        }
                        catch (JsonException e)
                        {
                            throw new EditException(new ValidationError("json", ErrorCodes.InvalidValue, "Not a JSON object: " + e.Message));
                        }
                        environment.Merge(change, new JsonMergeSettings { MergeArrayHandling = MergeArrayHandling.Replace });
                    }
                    doc["environment"] = environment;
                });
                return GameTools.Json(result.Ok
                    ? (object)new { ok = true, problems = GameTools.ErrorList(result.Errors), environment = JObject.Parse(TableJson.Serialize(session.Document))["environment"], savedTo = session.FilePath }
                    : new { ok = false, errors = GameTools.ErrorList(result.Errors), hint = "Not applied. room: gameNight, tavern, karaoke or none; theme: default or parchment." });
            });
    }
}
