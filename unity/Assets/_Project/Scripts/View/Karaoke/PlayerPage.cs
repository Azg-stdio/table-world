using System;
using System.Net;
using System.Text;
using System.Threading;
using UnityEngine;

namespace TableWorld.View.Karaoke
{
    /// <summary>
    /// The page the karaoke screen shows (ADR 0013 §6, T12.9): served by the game on 127.0.0.1, it holds YouTube's
    /// official IFrame player. YouTube refuses an embed that carries no identification (Error 153, "Video player
    /// configuration error"), and a page served over http gives the player an origin and a referrer, as on any website.
    /// The page reports the player's state in its title, so the game reads it without anything registered.
    /// </summary>
    public sealed class PlayerPage : IDisposable
    {
        private readonly HttpListener _listener = new HttpListener();
        private readonly Thread _thread;
        private volatile bool _running = true;

        /// <summary>The page's address, e.g. <c>http://127.0.0.1:52811/player.html</c>.</summary>
        public string Url { get; }

        public int Port { get; }

        public PlayerPage()
        {
            Port = FreePort();
            Url = $"http://127.0.0.1:{Port}/player.html";
            _listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
            _listener.Start();
            _thread = new Thread(Serve) { IsBackground = true, Name = "Karaoke player page" };
            _thread.Start();
        }

        /// <summary>The page for a video: its player starts it as soon as it can.</summary>
        public string PageFor(string videoId) => $"{Url}?v={Uri.EscapeDataString(videoId)}";

        private static int FreePort()
        {
            var probe = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            var port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            return port;
        }

        private void Serve()
        {
            while (_running)
            {
                HttpListenerContext context;
                try
                {
                    context = _listener.GetContext();
                }
                catch (Exception) when (!_running)
                {
                    return;
                }
                catch (HttpListenerException)
                {
                    return;
                }
                try
                {
                    var found = context.Request.Url.AbsolutePath == "/player.html";
                    var body = Encoding.UTF8.GetBytes(found ? Html(Port) : "Not found");
                    context.Response.StatusCode = found ? 200 : 404;
                    context.Response.ContentType = found ? "text/html; charset=utf-8" : "text/plain";
                    // YouTube's player needs the referrer it recommends.
                    context.Response.AddHeader("Referrer-Policy", "strict-origin-when-cross-origin");
                    context.Response.OutputStream.Write(body, 0, body.Length);
                    context.Response.Close();
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[PlayerPage] {e.Message}");
                }
            }
        }

        /// <summary>
        /// The page: one YouTube IFrame player filling it, black around it. <c>window.tw</c> takes the game's commands
        /// (load, play, pause, seek); the title reports <c>state|time|duration|video</c> every quarter second, with
        /// <c>error|code</c> when the player fails (a video that will not embed, or is blocked here).
        /// </summary>
        private static string Html(int port) => @"<!doctype html>
<html><head><meta charset=""utf-8""><meta name=""referrer"" content=""strict-origin-when-cross-origin"">
<style>html,body{margin:0;height:100%;background:#000;overflow:hidden}#p{width:100%;height:100%}</style></head>
<body><div id=""p""></div>
<script>
var params=new URLSearchParams(location.search);var player=null;var lastError='';
var tag=document.createElement('script');tag.src='https://www.youtube.com/iframe_api';document.head.appendChild(tag);
function onYouTubeIframeAPIReady(){
  player=new YT.Player('p',{width:'100%',height:'100%',videoId:params.get('v')||'',
    playerVars:{autoplay:1,playsinline:1,rel:0,origin:'http://127.0.0.1:" + port + @"'},
    events:{onError:function(e){lastError='error|'+e.data;}}});
}
window.tw={
  load:function(id,at){lastError='';if(player)player.loadVideoById({videoId:id,startSeconds:at||0});},
  play:function(){if(player)player.playVideo();},
  pause:function(){if(player)player.pauseVideo();},
  seek:function(t){if(player)player.seekTo(t,true);}
};
setInterval(function(){
  if(lastError){document.title=lastError;return;}
  if(!player||!player.getPlayerState){document.title='loading';return;}
  var d=player.getVideoData?player.getVideoData():{};
  document.title=player.getPlayerState()+'|'+(player.getCurrentTime()||0).toFixed(2)+'|'+(player.getDuration()||0).toFixed(2)+'|'+(d.video_id||'');
},250);
</script></body></html>";

        public void Dispose()
        {
            _running = false;
            try
            {
                _listener.Stop();
                _listener.Close();
            }
            catch (ObjectDisposedException)
            {
            }
        }
    }
}
