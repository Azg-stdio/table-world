using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Core.Rules;
using Newtonsoft.Json;
using UnityEngine;

namespace TableWorld.View
{
    /// <summary>
    /// Named saves and autosaves (G30), in <see cref="TableView.SavesFolder"/>: <c>name.json</c> is the table,
    /// <c>name.meta.json</c> says what it is (the name as typed, the game, when, and the package folder its images come
    /// from, so a save of an image game loads its images whatever is open). Autosaves rotate through
    /// <see cref="SaveNames.AutosaveSlots"/> files while the table changes. Only the host (or a player alone) saves and loads.
    /// </summary>
    public sealed class SaveLibrary
    {
        public sealed class Entry
        {
            public string File { get; set; } = "";
            public string Name { get; set; } = "";
            public string Game { get; set; }
            public DateTime SavedAt { get; set; }
            public string PackageRoot { get; set; }
            public bool Autosave => SaveNames.IsAutosave(Path.GetFileNameWithoutExtension(File));
        }

        private sealed class Meta
        {
            public string Name { get; set; }
            public string Game { get; set; }
            public DateTime SavedAt { get; set; }
            public string PackageRoot { get; set; }
        }

        private readonly TableView _table;
        private int _autosaveSlot;
        private int _savedChanges;
        private double _lastAutosave;

        /// <summary>Where saves live; tests point it at a scratch folder.</summary>
        public string Folder { get; set; } = TableView.SavesFolder;

        public SaveLibrary(TableView table)
        {
            _table = table;
            _lastAutosave = Time.unscaledTimeAsDouble;
            // A table as loaded needs no autosave; only what is done to it afterwards does.
            _savedChanges = table != null ? table.Changes : 0;
            if (table != null)
                table.Loaded += () => _savedChanges = _table.Changes;
        }

        public bool CanSave => _table != null && _table.Engine != null && _table.CanRewind;

        /// <summary>Every save, newest first.</summary>
        public List<Entry> List()
        {
            if (!Directory.Exists(Folder))
                return new List<Entry>();
            var entries = new List<Entry>();
            foreach (var file in Directory.GetFiles(Folder, "*.json").Where(f => !f.EndsWith(".meta.json")))
            {
                var entry = new Entry { File = file, Name = Path.GetFileNameWithoutExtension(file), SavedAt = System.IO.File.GetLastWriteTime(file) };
                var meta = ReadMeta(file);
                if (meta != null)
                {
                    entry.Name = string.IsNullOrWhiteSpace(meta.Name) ? entry.Name : meta.Name;
                    entry.Game = meta.Game;
                    entry.SavedAt = meta.SavedAt == default ? entry.SavedAt : meta.SavedAt.ToLocalTime();
                    entry.PackageRoot = meta.PackageRoot;
                }
                entries.Add(entry);
            }
            return entries.OrderByDescending(e => e.SavedAt).ToList();
        }

        /// <summary>Saves the table as <paramref name="name"/>; returns the entry, or null (with a reason) when it cannot.</summary>
        public Entry Save(string name, out string error)
        {
            error = null;
            var file = SaveNames.ToFileName(name);
            if (file == null)
            {
                error = "Type a name for the save.";
                return null;
            }
            if (!CanSave)
            {
                error = "Only the host saves the table.";
                return null;
            }
            return Write(file, name.Trim(), out error);
        }

        private Entry Write(string file, string name, out string error)
        {
            error = null;
            var path = Path.Combine(Folder, file + ".json");
            try
            {
                Directory.CreateDirectory(Folder);
                _table.Save(path);
                var meta = new Meta { Name = name, Game = _table.Engine.State.Game?.Name, SavedAt = DateTime.UtcNow, PackageRoot = _table.PackageRoot };
                System.IO.File.WriteAllText(MetaPath(path), JsonConvert.SerializeObject(meta, Formatting.Indented));
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                error = $"Could not save: {e.Message}";
                return null;
            }
            _savedChanges = _table.Changes;
            return new Entry { File = path, Name = name, Game = _table.Engine.State.Game?.Name, SavedAt = DateTime.Now, PackageRoot = _table.PackageRoot };
        }

        public bool Load(Entry entry, out string error)
        {
            error = null;
            if (!CanSave)
            {
                error = "Only the host loads a table.";
                return false;
            }
            if (!System.IO.File.Exists(entry.File))
            {
                error = "That save is gone.";
                return false;
            }
            var root = entry.PackageRoot != null && Directory.Exists(entry.PackageRoot) ? entry.PackageRoot : _table.PackageRoot;
            if (!_table.Load(System.IO.File.ReadAllText(entry.File), root))
            {
                error = $"{entry.Name} is not a valid table (details in the log).";
                return false;
            }
            _savedChanges = _table.Changes;
            return true;
        }

        /// <summary><see cref="Load"/> under the loading screen; <paramref name="done"/> hears whether it worked and why not.</summary>
        public void LoadInSteps(Entry entry, Action<bool, string> done)
        {
            if (!CanSave)
            {
                done(false, "Only the host loads a table.");
                return;
            }
            if (!System.IO.File.Exists(entry.File))
            {
                done(false, "That save is gone.");
                return;
            }
            var root = entry.PackageRoot != null && Directory.Exists(entry.PackageRoot) ? entry.PackageRoot : _table.PackageRoot;
            _table.LoadFileInSteps(entry.File, entry.Name, ok =>
            {
                if (ok)
                    _savedChanges = _table.Changes;
                done(ok, ok ? null : $"{entry.Name} is not a valid table (details in the log).");
            }, root);
        }

        public void Delete(Entry entry)
        {
            System.IO.File.Delete(entry.File);
            System.IO.File.Delete(MetaPath(entry.File));
        }

        /// <summary>
        /// Called every frame: saves to the next autosave slot when <paramref name="minutes"/> passed since the last one
        /// (or since start) and the table changed meanwhile. Returns the autosave's name when it saved.
        /// </summary>
        public string Tick(int minutes)
        {
            // Pieces settling just after a load report their resting poses: that is not a change worth an autosave.
            if (_table != null && _table.SecondsSinceLoad < SettleSeconds)
                _savedChanges = _table.Changes;
            var now = Time.unscaledTimeAsDouble;
            if (minutes <= 0 || !CanSave || now - _lastAutosave < minutes * 60.0)
                return null;
            _lastAutosave = now;
            if (_table.Changes == _savedChanges)
                return null;
            var file = SaveNames.Autosave(++_autosaveSlot);
            return Write(file, $"Autosave {DateTime.Now:HH:mm}", out _) != null ? file : null;
        }

        public const float SettleSeconds = 3f;

        /// <summary>Forces the next <see cref="Tick"/> to be due (tests).</summary>
        public void AutosaveNow() => _lastAutosave = double.MinValue;

        private static string MetaPath(string path) => Path.ChangeExtension(path, null) + ".meta.json";

        private static Meta ReadMeta(string path)
        {
            try
            {
                var meta = MetaPath(path);
                return System.IO.File.Exists(meta) ? JsonConvert.DeserializeObject<Meta>(System.IO.File.ReadAllText(meta)) : null;
            }
            catch (Exception e) when (e is IOException || e is JsonException)
            {
                return null;
            }
        }
    }
}
