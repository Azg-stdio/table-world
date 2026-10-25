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
                _voice.Online = () => _network != null && _network.IsListening && Synced && _table.LocalSeat != null;
            }
        }

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
            var bytes = VoiceCodec.Encode(packet);
            if (IsHost)
                foreach (var client in _network.ConnectedClientsIds.Where(c => c != NetworkManager.ServerClientId).ToList())
                    SendVoiceTo(client, bytes);
            else
                SendVoiceTo(NetworkManager.ServerClientId, bytes);
        }

        private void OnHostVoice(ulong sender, FastBufferReader reader)
        {
            reader.ReadValueSafe(out byte[] bytes);
            var packet = VoiceCodec.Decode(bytes);
            // A client speaks only for the seat it was given.
            if (packet == null || !_seatOf.TryGetValue(sender, out var seat) || seat == null)
                return;
            packet.Seat = seat;
            _voice?.Receive(packet);
            var relayed = VoiceCodec.Encode(packet);
            foreach (var client in _network.ConnectedClientsIds.Where(c => c != NetworkManager.ServerClientId && c != sender).ToList())
                SendVoiceTo(client, relayed);
        }

        private void OnClientVoice(ulong sender, FastBufferReader reader)
        {
            reader.ReadValueSafe(out byte[] bytes);
            if (VoiceCodec.Decode(bytes) is { } packet && packet.Seat != _table.LocalSeat)
                _voice?.Receive(packet);
        }

        private void SendVoiceTo(ulong client, byte[] bytes)
        {
            using var writer = new FastBufferWriter(bytes.Length + 16, Allocator.Temp);
            writer.WriteValueSafe(bytes);
            _network.CustomMessagingManager.SendNamedMessage(VoiceMessage, client, writer, NetworkDelivery.Unreliable);
        }
    }
}
