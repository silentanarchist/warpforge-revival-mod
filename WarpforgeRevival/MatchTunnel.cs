using System;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace WarpforgeRevival
{
    /// <summary>
    /// Encrypts match connections.
    ///
    /// The game's match client can only speak plain TCP. So, when the server is reached over https
    /// and says it takes encrypted matches, the match client is pointed at a small listener inside
    /// the game on this computer or phone (127.0.0.1, never reachable from outside), and every
    /// connection it makes there is carried on to the server's game port inside an encrypted
    /// connection, checked against the server's certificate the same way as everything else the
    /// mod sends. The server unwraps it and hands the inside to its match service as usual.
    /// </summary>
    internal static class MatchTunnel
    {
        private static readonly object Gate = new object();
        private static TcpListener listener;
        private static volatile string targetHost;
        private static volatile int targetPort;
        private static Task<bool?> probe;           // true / false: the server's answer; null: no answer
        private static string probedFor;
        private static volatile string problem;
        private static DateTime problemAt;

        /// <summary>
        /// Why the last match connection could not be encrypted, while it is recent (null otherwise).
        /// The game's own "Error connecting to server" popup shows this instead (see PopupNotes).
        /// </summary>
        internal static string Problem => problem != null && DateTime.UtcNow - problemAt < TimeSpan.FromMinutes(2) ? problem : null;

        private static void Trouble(string why)
        {
            problem = why;
            problemAt = DateTime.UtcNow;
            RevivalMod.Log.Warning("[match] " + why);
        }

        /// <summary>
        /// Starts asking the server whether it takes encrypted matches. A real answer is kept for
        /// this server address; when the question itself failed (no answer), it is asked again the
        /// next time, so one network hiccup does not decide the whole session. Only an https server
        /// is asked; a plain one never gets them.
        /// </summary>
        internal static Task<bool?> Probe()
        {
            lock (Gate)
            {
                string url = RevivalMod.Config.ServerUrl;
                if (!RevivalMod.Config.Secure) return Task.FromResult<bool?>(false);
                if (probe != null && probedFor == url && (!probe.IsCompleted || probe.Result.HasValue)) return probe;
                probedFor = url;
                return probe = Task.Run(() => Ask(url));
            }
        }

        private static async Task<bool?> Ask(string url)
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    using var http = Net.Client(TimeSpan.FromSeconds(8));
                    using var doc = System.Text.Json.JsonDocument.Parse(await http.GetStringAsync(url + "/api/v1/status"));
                    bool yes = doc.RootElement.TryGetProperty("encryptedMatches", out var v) && v.ValueKind == System.Text.Json.JsonValueKind.True;
                    if (!yes) RevivalMod.Log.Msg("[match] this server does not take encrypted matches; matches are not encrypted");
                    return yes;
                }
                catch (Exception e)
                {
                    if (attempt == 2) RevivalMod.Log.Warning("[match] could not ask the server about encrypted matches (" + e.Message + "); it is asked again on the next connection");
                    else await Task.Delay(1000);
                }
            }
            return null;
        }

        /// <summary>
        /// Where the match client should connect: the local end of the tunnel when matches are
        /// encrypted, otherwise the server itself. wait: how long the answer from the server may be
        /// waited for (the first connection waits; reconnects use what is already known).
        /// </summary>
        internal static void Route(ref string host, ref int port, TimeSpan wait)
        {
            var asked = Probe();
            if (!asked.IsCompleted && wait > TimeSpan.Zero) asked.Wait(wait);
            bool? answer = asked.IsCompleted ? asked.Result : null;
            if (answer == false) return;                         // the server does not take them
            if (answer == null)
            {
                // No answer from the server yet: this connection goes as it is (a server that
                // requires encrypted matches will refuse it), and the next one asks again.
                Trouble(asked.IsCompleted
                    ? "the server could not be asked about encrypted matches, so this match connection is not encrypted - check the connection and try again"
                    : "the server has not answered yet about encrypted matches, so this match connection is not encrypted - try again in a moment");
                return;
            }
            try
            {
                int local = Start(host, port);
                host = "127.0.0.1";
                port = local;
                problem = null;
            }
            catch (Exception e)
            {
                Trouble("the encrypted match connection could not be started on this device (" + e.Message + "); restart the game");
            }
        }

        private static int Start(string host, int port)
        {
            lock (Gate)
            {
                targetHost = host;
                targetPort = port;
                if (listener == null)
                {
                    var l = new TcpListener(IPAddress.Loopback, 0);
                    l.Start();
                    listener = l;
                    var t = new Thread(AcceptLoop) { IsBackground = true, Name = "Revival match tunnel" };
                    t.Start();
                    RevivalMod.Log.Msg("[match] matches are encrypted (through https to " + host + ":" + port + ")");
                }
                return ((IPEndPoint)listener.LocalEndpoint).Port;
            }
        }

        private static void AcceptLoop()
        {
            while (true)
            {
                TcpClient local;
                try { local = listener.AcceptTcpClient(); }
                catch (Exception e) { RevivalMod.Log.Warning("[match] the match tunnel stopped: " + e.Message); lock (Gate) listener = null; return; }
                // only this computer or phone may use it (the listener is on 127.0.0.1 anyway)
                if (!(local.Client.RemoteEndPoint is IPEndPoint ep) || !IPAddress.IsLoopback(ep.Address)) { local.Close(); continue; }
                _ = Task.Run(() => Carry(local, targetHost, targetPort));
            }
        }

        private static async Task Carry(TcpClient local, string host, int port)
        {
            TcpClient remote = null;
            SslStream tls = null;
            try
            {
                local.NoDelay = true;
                remote = new TcpClient { NoDelay = true };
                using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15)))
                    await remote.ConnectAsync(host, port, cts.Token);
                tls = new SslStream(remote.GetStream(), false, Net.Verify);
                using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15)))
                    await tls.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = host }, cts.Token);
                problem = null;                     // this one worked
                var plain = local.GetStream();
                var up = Pipe(plain, tls);
                var down = Pipe(tls, plain);
                await Task.WhenAny(up, down);       // either side closing ends the connection
            }
            catch (Exception e)
            {
                Trouble("the encrypted match connection to the server failed (" + (e.InnerException?.Message ?? e.Message) + ")");
            }
            finally
            {
                try { tls?.Dispose(); } catch { }
                try { remote?.Close(); } catch { }
                try { local.Close(); } catch { }
            }
        }

        private static async Task Pipe(System.IO.Stream from, System.IO.Stream to)
        {
            var buffer = new byte[16384];
            try
            {
                int n;
                while ((n = await from.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    await to.WriteAsync(buffer, 0, n);
                    await to.FlushAsync();
                }
            }
            catch (Exception) { }
        }
    }
}
