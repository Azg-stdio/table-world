using System;
using System.IO;
using System.Linq;
using Core.Engine;
using Core.Music;
using Core.Rules;
using UnityEngine;
using UnityEngine.UIElements;

namespace TableWorld.View.Hud
{
    /// <summary>
    /// The Music panel (M12, T13.3), opened with the Music button. Everyone sees what plays and browses their own music
    /// library: search, listen to a track on this machine only, import a pack (a zip or a folder, or whatever is in the
    /// library's Inbox folder). The host also plays, pauses and skips the table's music for everyone, switches between
    /// the game's playlists, and adds a library track to one of them. Styling lives in Hud.uss (music-*).
    /// </summary>
    public sealed class MusicPanel
    {
        private const string Hidden = "hidden";
        public const string InboxFolder = "Inbox";

        private readonly VisualElement _panel;
        private readonly Label _now;
        private readonly VisualElement _controls;
        private readonly VisualElement _playlists;
        private readonly Button _play;
        private readonly Button _shuffle;
        private readonly TextField _search;
        private readonly TextField _importPath;
        private readonly Toggle _rights;
        private readonly ScrollView _list;
        private readonly Label _hint;
        private readonly TableView _table;
        private readonly System.Random _random = new System.Random();
        private string _shownKey;

        public bool Visible => !_panel.ClassListContains(Hidden);

        private MusicPlayerView Player => _table.GetComponent<MusicPlayerView>();

        public MusicPanel(VisualElement root, TableView table)
        {
            _table = table;
            _panel = root.Q("musicPanel");
            _now = root.Q<Label>("musicNow");
            _controls = root.Q("musicControls");
            _playlists = root.Q("musicPlaylists");
            _play = root.Q<Button>("musicPlay");
            _shuffle = root.Q<Button>("musicShuffle");
            _search = root.Q<TextField>("musicSearch");
            _importPath = root.Q<TextField>("musicImportPath");
            _rights = root.Q<Toggle>("musicRights");
            _list = root.Q<ScrollView>("musicList");
            _hint = root.Q<Label>("musicHint");
            // Above the top bar and the game panel, which come later in the HUD.
            (root.Q("root") ?? root).Add(_panel);
            root.Q<Button>("musicButton").clicked += Toggle;
            root.Q<Button>("musicClose").clicked += Hide;
            root.Q<Button>("musicBack").clicked += () => Change(m => m.Next(_random, -1));
            root.Q<Button>("musicSkip").clicked += () => Change(m => m.Next(_random));
            _play.clicked += () => Change(m =>
            {
                var next = m.Clone();
                next.Playing = !m.Playing && m.Current != null;
                return next;
            });
            _shuffle.clicked += () => Change(m =>
            {
                var next = m.Clone();
                next.Shuffle = !m.Shuffle;
                return next;
            });
            root.Q<Button>("musicImport").clicked += Import;
            root.Q<Button>("musicFolder").clicked += OpenFolder;
            _search.RegisterValueChangedCallback(_ => _shownKey = null);
        }

        public void Toggle()
        {
            if (Visible)
                Hide();
            else
            {
                _shownKey = null;
                MusicPlayerView.Library.Reload();
                ImportInbox();
                _panel.RemoveFromClassList(Hidden);
            }
        }

        public void Hide()
        {
            _panel.AddToClassList(Hidden);
            if (Player != null && Player.PreviewUrl != null)
                Player.Preview(null);
        }

        /// <summary>The table's music after <paramref name="change"/>, for everyone (the host only).</summary>
        private void Change(Func<MusicState, MusicState> change)
        {
            if (!_table.HostsTable || _table.Engine == null)
                return;
            var music = _table.Engine.State.Music ?? new MusicState();
            _table.Execute(new SetMusicCommand { Music = change(music) });
            _shownKey = null;
        }

        public void Update()
        {
            if (!Visible || _table.Engine == null)
                return;
            var music = _table.Engine.State.Music;
            var player = Player;
            var key = $"{music?.Current?.Url}|{music?.Playing}|{music?.Shuffle}|{music?.List}|{music?.Playlists.Sum(p => p.Value.Count)}|{_search.value}|" +
                      $"{player?.PreviewUrl}|{player?.Waiting}|{_table.HostsTable}|{MusicPlayerView.Library.Packs.Count}";
            if (key == _shownKey)
                return;
            _shownKey = key;

            var title = music?.Current == null ? null : (string.IsNullOrWhiteSpace(music.Current.Title) ? music.Current.Url : music.Current.Title);
            _now.text = title == null ? Ui.T("No music playing.")
                : player != null && player.Waiting == music.Current.Url ? Ui.T("{title} (arriving from the host)", ("title", title))
                : music.Playing ? Ui.T("Playing: {title}", ("title", title)) : Ui.T("Paused: {title}", ("title", title));
            var hosting = _table.HostsTable;
            _controls.EnableInClassList(Hidden, !hosting);
            _play.text = music?.Playing == true ? Ui.T("Pause") : Ui.T("Play");
            _shuffle.text = music?.Shuffle == true ? Ui.T("● Shuffle") : Ui.T("Shuffle");
            _playlists.Clear();
            if (hosting && music != null)
                foreach (var name in music.Playlists.Keys)
                {
                    var list = name;
                    var button = new Button(() => Change(m => m.WithPlaylist(list, _random.Next))) { text = (music.List == list ? "● " : "") + Humanize(list) };
                    button.AddToClassList("hud-button");
                    _playlists.Add(button);
                }

            _list.Clear();
            var library = MusicPlayerView.Library;
            var tracks = library.Search(_search.value).Take(200).ToList();
            foreach (var (pack, track) in tracks)
                _list.Add(Row(pack, track, music, hosting));
            _hint.text = library.Packs.Count == 0
                ? Ui.T("Your music library is empty. Put music packs (zips or folders of MP3, OGG or WAV) in {folder}, or paste a path above, and import them. Only import music you may use.", ("folder", Path.Combine(library.Folder, InboxFolder)))
                : Ui.T("{count} tracks in {packs} packs. Tracks you add to a game reach players who lack them from you while playing.", ("count", library.Packs.Sum(p => p.Tracks.Count)), ("packs", library.Packs.Count));
        }

        private VisualElement Row(MusicPack pack, LibraryTrack track, MusicState music, bool hosting)
        {
            var url = MusicLibrary.Url(pack, track);
            var row = new VisualElement();
            row.AddToClassList("music-row");
            var name = new Label(track.Title) { enableRichText = false };
            name.AddToClassList("music-title");
            row.Add(name);
            var detail = new Label(Ui.T("{pack} · {length} · {tags}", ("pack", pack.Name), ("length", Length(track.Seconds)), ("tags", string.Join(", ", track.Tags)))) { enableRichText = false };
            detail.AddToClassList("music-detail");
            row.Add(detail);
            var previewing = Player != null && Player.PreviewUrl == url;
            row.Add(Button(previewing ? Ui.T("Stop") : Ui.T("Listen"), () =>
            {
                Player?.Preview(previewing ? null : url, previewing ? null : library().FileOf(pack, track));
                _shownKey = null;
            }));
            if (hosting)
            {
                if (music == null || music.Playlists.Count == 0)
                    row.Add(Button(Ui.T("Play for everyone"), () => Change(m =>
                    {
                        var next = m.Clone();
                        next.Current = new MusicTrack { Url = url, Title = track.Title, Hash = track.Hash };
                        next.Index = -1;
                        next.Playing = true;
                        next.Starts++;
                        return next;
                    })));
                else
                    foreach (var list in music.Playlists.Keys)
                    {
                        var target = list;
                        if (music.Playlists[list].Any(t => t.Url == url))
                            continue;
                        row.Add(Button(Ui.T("+ {playlist}", ("playlist", Humanize(list))), () => Change(m =>
                        {
                            var next = m.Clone();
                            next.Playlists[target].Add(new MusicTrack { Url = url, Title = track.Title, Hash = track.Hash });
                            if (next.List == target)
                                next.Playlist.Add(new MusicTrack { Url = url, Title = track.Title, Hash = track.Hash });
                            return next;
                        })));
                    }
            }
            return row;

            MusicLibrary library() => MusicPlayerView.Library;
        }

        private static Button Button(string text, Action action)
        {
            var button = new Button(action) { text = text };
            button.AddToClassList("hud-button");
            button.AddToClassList("music-action");
            return button;
        }

        private static string Length(double? seconds) =>
            seconds is { } s ? $"{(int)(s / 60)}:{(int)(s % 60):00}" : "?";

        private static string Humanize(string id) => Names.Humanize(id);

        /// <summary>Imports the pack at the path typed above, once the player says they may use it.</summary>
        private void Import()
        {
            var path = _importPath.value?.Trim().Trim('"');
            if (string.IsNullOrEmpty(path))
            {
                _hint.text = Ui.T("Paste the path of a zip or a folder of music first.");
                return;
            }
            if (!_rights.value)
            {
                _hint.text = Ui.T("Tick that you may use this music (its licence allows it) to import it.");
                return;
            }
            try
            {
                var pack = MusicPlayerView.Library.Import(path, true);
                _importPath.value = "";
                _shownKey = null;
                Update();
                _hint.text = Ui.T("Imported {pack}: {count} tracks.", ("pack", pack.Name), ("count", pack.Tracks.Count));
            }
            catch (Exception e) when (e is IOException || e is InvalidDataException || e is UnauthorizedAccessException || e is InvalidOperationException)
            {
                _hint.text = Ui.T("Could not import it: {why}", ("why", e.Message));
            }
        }

        /// <summary>Zips and folders the player put in the library's Inbox are imported when the panel opens, then removed from it.</summary>
        private void ImportInbox()
        {
            var inbox = Path.Combine(MusicPlayerView.Library.Folder, InboxFolder);
            try
            {
                Directory.CreateDirectory(inbox);
                foreach (var entry in Directory.GetFileSystemEntries(inbox))
                {
                    if (File.Exists(entry) && !entry.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                        continue;
                    // Putting a pack in the Inbox is the player's own choice to use it.
                    MusicPlayerView.Library.Import(entry, true);
                    if (Directory.Exists(entry))
                        Directory.Delete(entry, true);
                    else
                        File.Delete(entry);
                }
            }
            catch (Exception e) when (e is IOException || e is InvalidDataException || e is UnauthorizedAccessException)
            {
                Debug.LogWarning("[Music] Inbox import failed: " + e.Message);
            }
        }

        private static void OpenFolder()
        {
            var inbox = Path.Combine(MusicPlayerView.Library.Folder, InboxFolder);
            Directory.CreateDirectory(inbox);
            Application.OpenURL(new Uri(inbox).AbsoluteUri);
        }
    }
}
