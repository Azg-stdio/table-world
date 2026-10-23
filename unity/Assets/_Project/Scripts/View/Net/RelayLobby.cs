using System;
using System.Threading;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;

namespace TableWorld.View.Net
{
    /// <summary>
    /// Join codes over Unity Relay (T3.3): anonymous sign-in, a private session whose code the host shares, join by code.
    /// The session only carries the Relay connection; <see cref="NetworkTableSession"/> still starts Netcode itself
    /// through the <see cref="INetworkHandler"/> it passes here. Needs the Unity project linked to a Unity Gaming Services
    /// project (Edit → Project Settings → Services); without it every call fails with a message saying so.
    /// </summary>
    public static class RelayLobby
    {
        public const int MaxPlayers = 8;

        /// <summary>Authentication profile; two processes on one machine need different ones (<c>-tw-profile</c>).</summary>
        public static string Profile { get; set; }

        public static async Task SignInAsync()
        {
            if (UnityServices.State == ServicesInitializationState.Uninitialized)
            {
                var options = new InitializationOptions();
                if (!string.IsNullOrEmpty(Profile))
                    options.SetProfile(Profile);
                await UnityServices.InitializeAsync(options);
            }
            if (!AuthenticationService.Instance.IsSignedIn)
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
        }

        public static async Task<IHostSession> HostAsync(INetworkHandler network, int maxPlayers)
        {
            await SignInAsync();
            var options = new SessionOptions { MaxPlayers = maxPlayers, IsPrivate = true }.WithRelayNetwork().WithNetworkHandler(network);
            return await MultiplayerService.Instance.CreateSessionAsync(options);
        }

        public static async Task<ISession> JoinAsync(string code, INetworkHandler network)
        {
            await SignInAsync();
            return await MultiplayerService.Instance.JoinSessionByCodeAsync(code.Trim().ToUpperInvariant(), new JoinSessionOptions().WithNetworkHandler(network));
        }

        /// <summary>True for text that looks like a join code rather than an address (no dots or colons).</summary>
        public static bool LooksLikeCode(string text) =>
            !string.IsNullOrWhiteSpace(text) && text.Trim().Length >= 4 && text.IndexOf('.') < 0 && text.IndexOf(':') < 0;

        /// <summary>One line for the HUD: the common setup problem gets a hint, anything else its message.</summary>
        public static string Explain(Exception e)
        {
            var text = (e.InnerException ?? e).Message;
            if (text.IndexOf("project", StringComparison.OrdinalIgnoreCase) >= 0 && text.IndexOf("id", StringComparison.OrdinalIgnoreCase) >= 0)
                return "online play needs the project linked to Unity Gaming Services";
            return text.Length > 120 ? text.Substring(0, 120) + "…" : text;
        }

        /// <summary>Hands the Relay connection to the table session, which starts Netcode with its own handlers.</summary>
        internal sealed class Handler : INetworkHandler
        {
            private readonly Func<NetworkConfiguration, bool> _start;
            private readonly Action _stop;

            /// <summary>Set when the table session moved on: a late stop from the service must not shut down a newer network.</summary>
            public bool Detached { get; set; }

            public Handler(Func<NetworkConfiguration, bool> start, Action stop)
            {
                _start = start;
                _stop = stop;
            }

            public Task StartAsync(NetworkConfiguration configuration) => StartAsync(configuration, CancellationToken.None);

            public Task StartAsync(NetworkConfiguration configuration, CancellationToken cancellationToken)
            {
                if (!_start(configuration))
                    throw new InvalidOperationException("Netcode did not start over Relay.");
                return Task.CompletedTask;
            }

            public Task StopAsync()
            {
                if (!Detached)
                    _stop();
                return Task.CompletedTask;
            }
        }
    }
}
