using System.Linq;
using Core.Net;
using TableWorld.View.Voice;
using Unity.Collections;
using Unity.Netcode;

namespace TableWorld.View.Net
{
    /// <summary>
    /// Voice chat over the session (T7.3): each 20 ms Opus packet goes unreliable to the host, which stamps it with the
    /// sender's seat and relays it to everyone else. A lost or late packet is concealed by the listener's
    /// <see cref="VoiceStream"/>; resending it would only arrive too late to play.
    /// </summary>
    public sealed partial class NetworkTableSession
    {
        public const string VoiceMessage = "twv";

        private VoiceChat _voice;

        private void RegisterVoice(bool host)
        {
            _voice ??= FindAnyObjectByType<VoiceChat>();
            _network.CustomMessagingManager.RegisterNamedMessageHandler(VoiceMessage, host ? (CustomMessagingManager.HandleNamedMessageDelegate)OnHostVoice : OnClientVoice);
            if (_voice != null)
            {
                _voice.Spoke -= OnSpoke;
                _voice.Spoke += OnSpoke;
                _voice.Online = () => _network != null && _network.IsListening && Synced && SpeakerKey() != null;
                _voice.SpeakerKey = SpeakerKey;
            }
        }

        /// <summary>Whose voice it is: the seat, or for a spectator "~" and their participant id (no chalker, still heard).</summary>
        private string SpeakerKey() => _table.LocalSeat ?? (LocalParticipant != null ? SpectatorVoice(LocalParticipant) : null);

        public static string SpectatorVoice(string participant) => "~" + participant;

        private void UnregisterVoice()
        {
            if (_network != null && _network.CustomMessagingManager != null)
                _network.CustomMessagingManager.UnregisterNamedMessageHandler(VoiceMessage);
            if (_voice != null)
                _voice.Clear();
        }

        private void OnSpoke(VoicePacket packet)
        {
            if (_network == null || !_network.IsListening)
                return;
            Stamp(packet);
            var bytes = VoiceCodec.Encode(packet);
            if (IsHost)
            {
                foreach (var client in _network.ConnectedClientsIds.Where(c => c != NetworkManager.ServerClientId).ToList())
                    if (HeardBy(packet.Seat, client))
                        SendVoiceTo(client, bytes);
            }
            else
                SendVoiceTo(NetworkManager.ServerClientId, bytes);
        }

        private void OnHostVoice(ulong sender, FastBufferReader reader)
        {
            reader.ReadValueSafe(out byte[] bytes);
            var packet = VoiceCodec.Decode(bytes);
            // A client speaks only for the seat it was given (a spectator for itself).
            if (packet == null || !_seatOf.TryGetValue(sender, out var seat))
                return;
            packet.Seat = seat ?? SpectatorVoice(sender.ToString());
            Measure(packet);
            if (Hears(packet.Seat, _table.Spectating ? null : _table.LocalSeat))
                _voice?.Receive(packet);
            var relayed = VoiceCodec.Encode(packet);
            foreach (var client in _network.ConnectedClientsIds.Where(c => c != NetworkManager.ServerClientId && c != sender).ToList())
                if (HeardBy(packet.Seat, client))
                    SendVoiceTo(client, relayed);
        }

        /// <summary>
        /// Voice channels (ADR 0012): the host relays a voice only to those the phase lets hear it (the awake among
        /// themselves at night, a team among itself). A spectator's voice ("~…") speaks as nobody's seat.
        /// </summary>
        private bool HeardBy(string speaker, ulong client) => Hears(speaker, _seatOf.TryGetValue(client, out var seat) ? seat : null);

        private bool Hears(string speaker, string listener) =>
            _table.Game == null || _table.Game.Hears(speaker != null && speaker.StartsWith("~") ? null : speaker, listener);

        private void OnClientVoice(ulong sender, FastBufferReader reader)
        {
            reader.ReadValueSafe(out byte[] bytes);
            if (VoiceCodec.Decode(bytes) is { } packet && packet.Seat != SpeakerKey())
            {
                Measure(packet);
                _voice?.Receive(packet);
            }
        }

        private void SendVoiceTo(ulong client, byte[] bytes)
        {
            using var writer = new FastBufferWriter(bytes.Length + 16, Allocator.Temp);
            writer.WriteValueSafe(bytes);
            _network.CustomMessagingManager.SendNamedMessage(VoiceMessage, client, writer, NetworkDelivery.Unreliable);
        }
    }
}
