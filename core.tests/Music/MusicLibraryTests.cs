using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Core.Engine;
using Core.Music;
using Core.Net;
using Core.Rules;
using Newtonsoft.Json.Linq;

namespace Core.Tests.Music
{
    /// <summary>
    /// The music library and the game's playlists (M12, T13.3): packs imported from a zip or a folder into the player's
    /// own folder, playlists of library tracks that start with the game and change with its phases, and tracks that a
    /// joiner without the pack receives from the host while one with it plays its own copy.
    /// </summary>
    public sealed class MusicLibraryTests : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "tw-music-" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, true);
        }

        /// <summary>A WAV of <paramref name="seconds"/> of silence (8 kHz, 8-bit mono).</summary>
        private static byte[] Wav(double seconds, byte fill = 128)
        {
            var samples = (int)(8000 * seconds);
            var data = new byte[44 + samples];
            Encoding.ASCII.GetBytes("RIFF").CopyTo(data, 0);
            BitConverter.GetBytes(36 + samples).CopyTo(data, 4);
            Encoding.ASCII.GetBytes("WAVEfmt ").CopyTo(data, 8);
            BitConverter.GetBytes(16).CopyTo(data, 16);
            BitConverter.GetBytes((short)1).CopyTo(data, 20);
            BitConverter.GetBytes((short)1).CopyTo(data, 22);
            BitConverter.GetBytes(8000).CopyTo(data, 24);
            BitConverter.GetBytes(8000).CopyTo(data, 28);
            BitConverter.GetBytes((short)1).CopyTo(data, 32);
            BitConverter.GetBytes((short)8).CopyTo(data, 34);
            Encoding.ASCII.GetBytes("data").CopyTo(data, 36);
            BitConverter.GetBytes(samples).CopyTo(data, 40);
            for (var i = 44; i < data.Length; i++)
                data[i] = fill;
            return data;
        }

        /// <summary>The headers the length is read from: a Vorbis identification header (44.1 kHz) and a last page.</summary>
        private static byte[] Ogg(double seconds)
        {
            var bytes = new byte[200];
            Encoding.ASCII.GetBytes("OggS").CopyTo(bytes, 0);
            Encoding.ASCII.GetBytes("\u0001vorbis").CopyTo(bytes, 28);
            BitConverter.GetBytes(0u).CopyTo(bytes, 35);
            bytes[39] = 2;
            BitConverter.GetBytes(44100u).CopyTo(bytes, 40);
            Encoding.ASCII.GetBytes("OggS").CopyTo(bytes, 120);
            BitConverter.GetBytes((long)(44100 * seconds)).CopyTo(bytes, 126);
            return bytes;
        }

        /// <summary>A pack laid out as the AlkaKrab ones are: each track in several formats, loops apart, a licence.</summary>
        private string Pack()
        {
            var zip = Path.Combine(_root, "Fantasy Pack Vol.3.zip");
            Directory.CreateDirectory(_root);
            if (File.Exists(zip))
                return zip;
            using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
            {
                void Add(string path, byte[] data)
                {
                    using var stream = archive.CreateEntry(path).Open();
                    stream.Write(data, 0, data.Length);
                }
                Add("Tracks/ogg/Ambient 1.ogg", Ogg(240.5));
                Add("Tracks/mp3/Ambient 1.mp3", new byte[300]);
                Add("Tracks/wav/Ambient 1.wav", Wav(2));
                Add("Tracks/ogg/Ambient 10.ogg", Ogg(61));
                Add("Tracks/ogg/Ambient 2.ogg", Ogg(30));
                Add("Loops/ogg/Ambient 1 Loop.ogg", Ogg(120));
                Add("Tracks/wav/Dark 1.wav", Wav(1.5, 100));
                Add("Tracks/mp3/desktop.ini", new byte[3]);
                Add("AlkaKrab Music License Info.pdf", Encoding.ASCII.GetBytes("%PDF licence"));
                Add("__MACOSX/Tracks/._Ambient 1.ogg", new byte[10]);
            }
            return zip;
        }

        [Fact]
        public void APack_IsImported_OneFilePerTrack_InTheSmallestFormat_WithItsLicence()
        {
            var library = new MusicLibrary(Path.Combine(_root, "Music"));
            Assert.Throws<InvalidOperationException>(() => library.Import(Pack(), rightsConfirmed: false));
            var pack = library.Import(Pack(), rightsConfirmed: true, sourcePage: "https://alkakrab.itch.io/");
            Assert.Equal("fantasy-pack-vol-3", pack.Id);
            Assert.Equal("AlkaKrab", pack.Author);
            Assert.Equal("LICENSE.pdf", pack.LicenseFile);
            Assert.True(File.Exists(Path.Combine(library.Folder, pack.Id, "LICENSE.pdf")));
            // Natural order, OGG chosen over MP3 and WAV, loops kept as their own tracks, the stray files left out.
            Assert.Equal(new[] { "ambient-1", "ambient-1-loop", "ambient-2", "ambient-10", "dark-1" }, pack.Tracks.Select(t => t.Id));
            var first = pack.Tracks[0];
            Assert.Equal("ambient-1.ogg", first.File);
            Assert.Equal("Ambient 1", first.Title);
            Assert.Equal(240.5, first.Seconds);
            Assert.Equal("Ambient 1 (loop)", pack.Tracks[1].Title);
            Assert.Contains("loop", pack.Tracks[1].Tags);
            Assert.Contains("ambient", first.Tags);
            Assert.Equal(1.5, pack.Tracks[4].Seconds);
            Assert.Equal("dark-1.wav", pack.Tracks[4].File);

            // Another process (the agent's server) sees the same library.
            var again = new MusicLibrary(library.Folder);
            Assert.Equal("library:fantasy-pack-vol-3/dark-1", MusicLibrary.Url(again.Packs.Single(), again.Packs.Single().Tracks[4]));
            Assert.Equal(again.FileOf(again.Packs[0], again.Packs[0].Tracks[0]), again.PathOf("library:fantasy-pack-vol-3/ambient-1"));
            Assert.Equal(again.PathOf("library:fantasy-pack-vol-3/dark-1"), again.PathByHash(pack.Tracks[4].Hash));
            Assert.Null(again.PathOf("library:fantasy-pack-vol-3/nope"));
            Assert.Equal(new[] { "dark-1" }, again.Search("dark").Select(f => f.Track.Id));
            Assert.Equal(new[] { "ambient-1-loop" }, again.Search(tag: "loop").Select(f => f.Track.Id));
            Assert.True(again.Remove(pack.Id));
            Assert.Empty(again.Packs);
        }

        private const string Game = @"{
  ""music"": {
    ""playlists"": {
      ""tavern"": [ { ""title"": ""Ambient 1"", ""url"": ""library:fantasy-pack-vol-3/ambient-1"" }, { ""url"": ""library:fantasy-pack-vol-3/ambient-2"" } ],
      ""ritual"": [ { ""title"": ""Dark 1"", ""url"": ""library:fantasy-pack-vol-3/dark-1"" } ] },
    ""start"": ""tavern"", ""volume"": 0.7 },
  ""players"": { ""seats"": [ { ""id"": ""a"", ""color"": ""red"" }, { ""id"": ""b"", ""color"": ""blue"" } ] },
  ""turn"": { ""phases"": [ ""day"", ""night"" ] },
  ""phases"": [
    { ""id"": ""day"", ""actions"": [ ""sleep"", ""hush"" ], ""onEnter"": [ { ""music"": { ""playlist"": ""tavern"" } } ] },
    { ""id"": ""night"", ""actions"": [ ""wake"" ], ""onEnter"": [ { ""music"": { ""playlist"": ""ritual"", ""crossfade"": 3 } } ] } ],
  ""actions"": [
    { ""id"": ""sleep"", ""actor"": ""any"", ""effects"": [ { ""endPhase"": {} } ] },
    { ""id"": ""hush"", ""actor"": ""any"", ""effects"": [ { ""music"": { ""pause"": true } } ] },
    { ""id"": ""wake"", ""actor"": ""any"", ""effects"": [ { ""endPhase"": {} } ] } ]
}";

        private static GameEngine Start(string json = Game)
        {
            var parsed = TableJson.Parse(json);
            Assert.True(parsed.Ok, string.Join("\n", parsed.Errors));
            Assert.Empty(TableValidator.Validate(parsed.State!));
            var game = new GameEngine(parsed.State!, 1);
            Assert.True(game.Execute(new StartGameCommand()).Ok);
            return game;
        }

        [Fact]
        public void TheGamesPlaylist_StartsWithIt_ChangesWithItsPhases_AndIsNotRestartedByTheSamePhase()
        {
            var game = Start();
            var music = game.State.Music!;
            Assert.Equal("tavern", music.List);
            Assert.True(music.Playing);
            Assert.Equal("library:fantasy-pack-vol-3/ambient-1", music.Current!.Url);
            Assert.Equal(0.7f, music.Volume!.Value, 3);
            var starts = music.Starts;

            Assert.True(game.Execute(new PerformActionCommand { Actor = "a", ActionId = "sleep" }).Ok);
            Assert.Equal("ritual", game.State.Music!.List);
            Assert.Equal("Dark 1", game.State.Music.Current!.Title);
            Assert.Equal(3f, game.State.Music.Crossfade!.Value, 3);
            Assert.Equal(starts + 1, game.State.Music.Starts);

            Assert.True(game.Execute(new PerformActionCommand { Actor = "a", ActionId = "wake" }).Ok);
            Assert.Equal("tavern", game.State.Music!.List);
            var back = game.State.Music.Starts;
            // The day again, and the same playlist is still on: nothing starts over.
            Assert.True(game.Execute(new PerformActionCommand { Actor = "a", ActionId = "hush" }).Ok);
            Assert.False(game.State.Music!.Playing);
            Assert.Equal(back, game.State.Music.Starts);

            // The table, saved and loaded, keeps its playlists.
            var copy = TableJson.Parse(TableJson.Serialize(game.State)).State!;
            Assert.Equal(new[] { "tavern", "ritual" }, copy.Music!.Playlists.Keys);
            Assert.Equal("tavern", copy.Music.Start);
        }

        [Fact]
        public void TheMusic_CopiesOverTheWire_Exactly()
        {
            // The first track of a playlist (index 0) and a library track used to come back different: the client's copy
            // of the table then failed the host's hash and asked for the whole table again.
            var game = Start();
            Assert.Equal(0, game.State.Music!.Index);
            var text = TableJson.Serialize(game.State, indented: false);
            Assert.DoesNotContain("isLibrary", text);
            Assert.Equal(text, TableJson.Serialize(TableJson.Copy(game.State), indented: false));
            Assert.Equal(text, TableJson.Serialize(TableJson.Parse(text).State!, indented: false));
            Assert.Equal(SeatView.Hash(game.State, game.Table), SeatView.Hash(TableJson.Copy(game.State)));
        }

        [Fact]
        public void AMissingPlaylist_OrABadTrack_IsAValidationError()
        {
            var errors = TableValidator.Validate(TableJson.Parse(Game.Replace(@"""playlist"": ""ritual""", @"""playlist"": ""rituals""")).State!);
            Assert.Contains(errors, e => e.Path.EndsWith(".playlist") && e.Suggestion!.Contains("ritual"));
            errors = TableValidator.Validate(TableJson.Parse(Game.Replace("library:fantasy-pack-vol-3/dark-1", "dark-1.ogg")).State!);
            Assert.Contains(errors, e => e.Path == "music.playlists.ritual[0].url");
            errors = TableValidator.Validate(TableJson.Parse(Game.Replace(@"""start"": ""tavern""", @"""start"": ""inn""")).State!);
            Assert.Contains(errors, e => e.Path == "music.start");
        }

        [Fact]
        public void ATrack_TravelsFromTheHost_UnlessTheJoinerHasItInTheirLibrary()
        {
            var library = new MusicLibrary(Path.Combine(_root, "Music"));
            var pack = library.Import(Pack(), rightsConfirmed: true);
            var game = Start();
            var host = new AssetHost(_ => null)
            {
                ReadTrack = track => library.PathOf(track.Url) is { } path ? File.ReadAllBytes(path) : null,
            };
            host.Build(game.State);
            // The first track to play first; every library track the game names, once.
            var ids = host.Entries.Select(e => e.Id).ToList();
            Assert.Equal("music:library:fantasy-pack-vol-3/ambient-1", ids[0]);
            Assert.Equal(3, ids.Count);
            Assert.Equal(pack.Tracks.Single(t => t.Id == "dark-1").Hash, host.Entries.Single(e => e.Id.EndsWith("dark-1")).Hash);

            // A joiner without the pack downloads the tracks; one with it downloads nothing and reads its own files.
            JObject? Pump(AssetClient client, JObject? request)
            {
                while (request != null)
                {
                    JObject? next = null;
                    foreach (var reply in host.Receive("c", request))
                        next = client.Receive(reply.Message) ?? next;
                    request = next;
                }
                return null;
            }
            var stranger = new AssetClient(new MemoryAssetStore());
            Pump(stranger, stranger.Receive(new JObject { ["kind"] = "snapshot", ["assets"] = host.Manifest() }));
            Assert.Equal(3, stranger.Fetched);
            Assert.True(stranger.Complete);

            var friend = new AssetClient(new LibraryAwareStore(new MemoryAssetStore(), library.PathByHash));
            Assert.Null(friend.Receive(new JObject { ["kind"] = "snapshot", ["assets"] = host.Manifest() }));
            Assert.Equal(0, friend.Fetched);
            Assert.Equal(3, friend.FromCache);
            Assert.Equal(File.ReadAllBytes(library.PathOf("library:fantasy-pack-vol-3/dark-1")!), friend.Read("music:library:fantasy-pack-vol-3/dark-1"));

            // A playlist changed while playing: the manifest alone reaches the players already there.
            var before = host.ManifestKey;
            game.State.Music!.Playlists["ritual"].Add(new MusicTrack { Url = "library:fantasy-pack-vol-3/ambient-10" });
            host.Build(game.State);
            Assert.NotEqual(before, host.ManifestKey);
            Assert.NotNull(stranger.Receive(host.ManifestMessage()));
        }
    }
}
