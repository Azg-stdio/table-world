using System.Linq;
using Core.Engine;
using Core.Net;
using Core.Scripting;
using UnityEngine;

namespace TableWorld.View.Net
{
    /// <summary>
    /// Table scripts (M5, ADR 0003): the host (or a player alone) runs the table's Lua; clients never do. Scripts load
    /// whenever a table is loaded, rewound or replaced, hear every command the host orders with the acting seat's colour,
    /// and their timers advance every frame. What they broadcast goes to everyone's chat; their errors to the log.
    /// </summary>
    public sealed partial class NetworkTableSession
    {
        private ScriptHost _scripts;

        /// <summary>The script host of this machine; null on a client.</summary>
        public ScriptHost Scripts => IsClient ? null : _scripts;

        private void StartScripts()
        {
            _scripts = new ScriptHost(() => _table != null ? _table.Engine : null, command => _table.Execute(command));
            _scripts.Broadcast += text => Announce(text);
            _scripts.Log += text => Debug.Log("[Lua] " + text);
        }

        /// <summary>A table arrived (loaded, rewound, received): the host runs its scripts from the start.</summary>
        private void LoadScripts()
        {
            if (_table == null || _table.Engine == null || IsClient)
                return;
            if (_scripts == null)
                StartScripts();
            _scripts.Load(Time.unscaledTimeAsDouble);
        }

        private void TickScripts()
        {
            if (_scripts != null && !IsClient && _scripts.ScriptCount > 0)
                _scripts.Tick(Time.unscaledTimeAsDouble);
        }

        /// <summary>Tells the scripts about a command, with the colour of the seat that did it.</summary>
        private void ScriptsHear(Command command, CommandResult result, string seat)
        {
            if (_scripts == null || IsClient || _scripts.ScriptCount == 0)
                return;
            var color = seat == null ? null : _table.Engine.State.Players.Seats.FirstOrDefault(s => s.Id == seat)?.Color;
            _scripts.OnCommand(command, result, color);
        }
    }
}
