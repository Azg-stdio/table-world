using System.Collections.Generic;
using System.Linq;
using Core.Net;
using Newtonsoft.Json.Linq;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace TableWorld.View.Net
{
    /// <summary>
    /// Live presence (G1–G3): each player sends where their pointer is and the poses of what they carry, up to
    /// <see cref="PresenceRate"/> times a second over an unreliable channel; the host relays every client's frame to the
    /// others. <see cref="PresenceView"/> draws it. Presence never changes the table: drops still end in move commands.
    /// </summary>
    public sealed partial class NetworkTableSession
    {
        public const string PresenceMessage = "twp";
        public const float PresenceRate = 15f;
        /// <summary>Unchanged presence is still re-sent this often, so other players do not time it out.</summary>
        public const float PresenceKeepAlive = 0.5f;
        /// <summary>Above this size a frame goes reliable (fragmented) instead of unreliable.</summary>
        public const int UnreliableLimit = 1100;

        private readonly PresenceBoard _presence = new PresenceBoard();
        private readonly Dictionary<string, float> _remoteHeldAt = new Dictionary<string, float>();
        private TableInteraction _interaction;
        private long _presenceSeq;
        private float _presenceSentAt = -1f;
        private string _presenceKey;

        /// <summary>The other players heard from recently (never this machine's own frame).</summary>
        public IEnumerable<(string Peer, PresenceFrame Frame)> Others =>
            _network != null && _network.IsListening ? _presence.Active(Time.unscaledTimeAsDouble) : Enumerable.Empty<(string, PresenceFrame)>();

        /// <summary>True while, or shortly after, another player carried the object: their reports own its pose, not ours.</summary>
        public bool RemoteHeldRecently(string objectId) =>
            _remoteHeldAt.TryGetValue(objectId, out var at) && Time.unscaledTime - at < TouchSeconds;

        private void RegisterPresence(bool host)
        {
            _network.CustomMessagingManager.RegisterNamedMessageHandler(PresenceMessage, host ? (CustomMessagingManager.HandleNamedMessageDelegate)OnHostPresence : OnClientPresence);
        }

        private void UnregisterPresence()
        {
            if (_network != null && _network.CustomMessagingManager != null)
                _network.CustomMessagingManager.UnregisterNamedMessageHandler(PresenceMessage);
            foreach (var peer in _presence.Active(double.MaxValue).Select(p => p.Peer).ToList())
                _presence.Remove(peer);
            _remoteHeldAt.Clear();
            _presenceKey = null;
        }

        private void Update()
        {
            if (_network == null || !_network.IsListening || !Synced)
                return;
            SendPresence();
        }

        /// <summary>This player's frame, sent when it changed (at most <see cref="PresenceRate"/>/s) or as a keep-alive.</summary>
        private void SendPresence()
        {
            var now = Time.unscaledTime;
            if (now - _presenceSentAt < 1f / PresenceRate)
                return;
            _interaction ??= FindAnyObjectByType<TableInteraction>();
            var frame = new PresenceFrame { Seat = _table.LocalSeat ?? "" };
            if (_interaction != null)
            {
                if (_interaction.PointerOnTable is { } pointer)
                    frame.Pointer = pointer.ToCore();
                foreach (var view in _interaction.HeldGroup)
                    if (view != null)
                        frame.Held.Add(new HeldPose { Id = view.Id, Position = view.transform.position.ToCore(), Rotation = view.transform.rotation.eulerAngles.ToCore() });
            }
            // Millimetre and degree resolution is plenty; it also keeps an idle player quiet.
            var key = string.Join("|", new[] { frame.Pointer is { } p ? $"{p.X:F3},{p.Z:F3}" : "-" }
                .Concat(frame.Held.Select(h => $"{h.Id}:{h.Position.X:F3},{h.Position.Y:F3},{h.Position.Z:F3},{h.Rotation.Y:F0},{h.Rotation.X:F0},{h.Rotation.Z:F0}")));
            if (key == _presenceKey && now - _presenceSentAt < PresenceKeepAlive)
                return;
            _presenceKey = key;
            _presenceSentAt = now;
            frame.Sequence = ++_presenceSeq;
            var message = PresenceCodec.Encode(frame);
            if (IsHost)
                foreach (var client in _network.ConnectedClientsIds.Where(c => c != NetworkManager.ServerClientId).ToList())
                    SendPresenceTo(client, message);
            else
                SendPresenceTo(NetworkManager.ServerClientId, message);
        }

        private void OnHostPresence(ulong sender, FastBufferReader reader)
        {
            reader.ReadValueSafe(out string text);
            var frame = PresenceCodec.Decode(JObject.Parse(text));
            // A client speaks only for the seat it was given.
            if (frame == null || !_seatOf.TryGetValue(sender, out var seat) || seat == null)
                return;
            frame.Seat = seat;
            if (!Take(frame))
                return;
            var relayed = PresenceCodec.Encode(frame);
            foreach (var client in _network.ConnectedClientsIds.Where(c => c != NetworkManager.ServerClientId && c != sender).ToList())
                SendPresenceTo(client, relayed);
        }

        private void OnClientPresence(ulong sender, FastBufferReader reader)
        {
            reader.ReadValueSafe(out string text);
            if (PresenceCodec.Decode(JObject.Parse(text)) is { } frame && frame.Seat != _table.LocalSeat)
                Take(frame);
        }

        private bool Take(PresenceFrame frame)
        {
            if (!_presence.Update(frame.Seat, frame, Time.unscaledTimeAsDouble))
                return false;
            foreach (var held in frame.Held)
                _remoteHeldAt[held.Id] = Time.unscaledTime;
            return true;
        }

        private void SendPresenceTo(ulong client, JObject message)
        {
            var text = message.ToString(Newtonsoft.Json.Formatting.None);
            var delivery = text.Length * 2 < UnreliableLimit ? NetworkDelivery.UnreliableSequenced : NetworkDelivery.ReliableFragmentedSequenced;
            using var writer = new FastBufferWriter(text.Length * 2 + 64, Allocator.Temp, 1024 * 1024);
            writer.WriteValueSafe(text);
            _network.CustomMessagingManager.SendNamedMessage(PresenceMessage, client, writer, delivery);
        }
    }
}
