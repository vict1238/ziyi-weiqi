using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace GoDesk
{
    internal abstract class OnlineSession : IDisposable
    {
        public abstract OnlineRoomState State { get; }
        public abstract int Seat { get; }
        public virtual string Invitation { get { return ""; } }
        public abstract string LastError { get; }
        public event Action Changed;
        public abstract void Send(OnlineCommand command);
        public abstract void Dispose();

        public static OnlineSession Host(IPAddress address, int port, OnlineRoomMode mode, Stone hostColor, GoGame initial, string name)
        {
            if (!OnlineWire.UsableAddress(address)) throw new ArgumentException("请选择实际的局域网或 Tailscale IPv4 地址。");
            if (port < 0 || port > 65535) throw new ArgumentOutOfRangeException("port");
            return new OnlineHost(address, port, new OnlineAuthority(mode, hostColor, initial, name));
        }

        public static Task<OnlineSession> JoinAsync(string invitation, string name)
        {
            return Task.Run<OnlineSession>(delegate
            {
                var client = new OnlineGuest(OnlineInvite.Parse(invitation), OnlineAuthority.PlayerName(name, "棋友"));
                try { client.Start(); return client; }
                catch { client.Dispose(); throw; }
            });
        }

        protected void Notify()
        {
            Action changed = Changed;
            if (changed == null) return;
            try { changed(); }
            catch (Exception error) { Trace.TraceError("Online room observer: " + error.GetType().Name); }
        }
    }

    internal sealed class OnlineInvite
    {
        private const string Prefix = "ZIYI2-";
        public int Version { get; set; }
        public string Address { get; set; }
        public int Port { get; set; }
        public string Pin { get; set; }
        public string Secret { get; set; }

        internal string Format()
        {
            return Prefix + Convert.ToBase64String(OnlineWire.Encode(this)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        internal static OnlineInvite Parse(string text)
        {
            text = (text ?? "").Trim();
            if (text.Length > 1024 || !text.StartsWith(Prefix, StringComparison.Ordinal))
                throw new FormatException("请输入梓懿围棋 2.0 的完整邀请。");
            string encoded = text.Substring(Prefix.Length).Replace('-', '+').Replace('_', '/');
            encoded = encoded.PadRight((encoded.Length + 3) / 4 * 4, '=');
            OnlineInvite invite;
            try { invite = OnlineWire.Decode<OnlineInvite>(Convert.FromBase64String(encoded)); }
            catch (Exception error)
            {
                if (!OnlineWire.DataFailure(error)) throw;
                throw new FormatException("邀请内容无效，请重新复制。");
            }
            IPAddress address;
            if (invite == null || invite.Version != OnlineWire.Version ||
                !IPAddress.TryParse(invite.Address, out address) || !OnlineWire.UsableAddress(address) ||
                invite.Port < 1 || invite.Port > 65535 || !OnlineWire.IsHex(invite.Pin, 64) ||
                !OnlineWire.IsHex(invite.Secret, 48))
                throw new FormatException("邀请地址或协议版本无效，请确认双方使用兼容版本。");
            return invite;
        }
    }

    internal sealed class OnlinePacket
    {
        public string Type { get; set; }
        public int Version { get; set; }
        public string Name { get; set; }
        public string Secret { get; set; }
        public string Resume { get; set; }
        public string Id { get; set; }
        public string Error { get; set; }
        public OnlineCommand Command { get; set; }
        public OnlineRoomState State { get; set; }
    }

    internal static class OnlineWire
    {
        internal const int Version = 1;
        private const int MaxFrame = 512 * 1024;
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);

        internal static bool DataFailure(Exception error)
        {
            return error is InvalidOperationException || error is ArgumentException || error is FormatException;
        }

        internal static bool LinkFailure(Exception error)
        {
            return error is IOException || error is SocketException || error is AuthenticationException ||
                error is TimeoutException || error is CryptographicException || DataFailure(error);
        }

        internal static bool UsableAddress(IPAddress address)
        {
            if (address == null || address.AddressFamily != AddressFamily.InterNetwork) return false;
            byte[] ip = address.GetAddressBytes();
            return ip[0] == 10 || ip[0] == 127 || (ip[0] == 172 && ip[1] >= 16 && ip[1] <= 31) ||
                (ip[0] == 192 && ip[1] == 168) || (ip[0] == 100 && ip[1] >= 64 && ip[1] <= 127) ||
                (ip[0] == 169 && ip[1] == 254);
        }

        internal static bool IsHex(string value, int length)
        {
            if (value == null || value.Length != length) return false;
            foreach (char c in value) if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'))) return false;
            return true;
        }

        private static string Hex(byte[] bytes) { return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant(); }

        internal static string Token()
        {
            byte[] bytes = new byte[24];
            using (RandomNumberGenerator random = RandomNumberGenerator.Create()) random.GetBytes(bytes);
            return Hex(bytes);
        }

        internal static string Pin(X509Certificate certificate)
        {
            using (SHA256 hash = SHA256.Create()) return Hex(hash.ComputeHash(certificate.GetRawCertData()));
        }

        internal static bool SameSecret(string left, string right)
        {
            if (left == null || right == null || left.Length != right.Length) return false;
            int difference = 0;
            for (int i = 0; i < left.Length; i++) difference |= left[i] ^ right[i];
            return difference == 0;
        }

        internal static X509Certificate2 CreateCertificate()
        {
            using (RSA key = RSA.Create())
            {
                key.KeySize = 2048;
                var request = new CertificateRequest("CN=ZiYi Weiqi Room", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
                var usages = new OidCollection();
                usages.Add(new Oid("1.3.6.1.5.5.7.3.1"));
                request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(usages, false));
                DateTimeOffset now = DateTimeOffset.UtcNow;
                using (X509Certificate2 temporary = request.CreateSelfSigned(now.AddMinutes(-5), now.AddDays(2)))
                {
                    string password = Token();
                    byte[] pfx = temporary.Export(X509ContentType.Pfx, password);
                    try
                    {
                        // Schannel needs a key container it can acquire from the
                        // certificate. The default key is removed with disposal.
                        return new X509Certificate2(pfx, password, X509KeyStorageFlags.UserKeySet);
                    }
                    finally { Array.Clear(pfx, 0, pfx.Length); }
                }
            }
        }

        internal static byte[] Encode(object value)
        {
            return Utf8.GetBytes(Serializer().Serialize(value));
        }

        internal static T Decode<T>(byte[] bytes)
        {
            return Serializer().Deserialize<T>(Utf8.GetString(bytes));
        }

        private static JavaScriptSerializer Serializer()
        {
            return new JavaScriptSerializer { MaxJsonLength = MaxFrame, RecursionLimit = 16 };
        }

        internal static OnlinePacket Read(Stream stream, int maximumLength = MaxFrame)
        {
            byte[] header = new byte[4];
            ReadFull(stream, header);
            int length = (header[0] << 24) | (header[1] << 16) | (header[2] << 8) | header[3];
            if (length <= 0 || length > maximumLength) throw new IOException("联机消息大小超限。");
            byte[] bytes = new byte[length];
            ReadFull(stream, bytes);
            OnlinePacket packet = Decode<OnlinePacket>(bytes);
            if (packet == null || packet.Type == null) throw new IOException("联机消息格式无效。");
            return packet;
        }

        internal static void Write(Stream stream, OnlinePacket packet)
        {
            byte[] bytes = Encode(packet);
            int length = bytes.Length;
            if (length == 0 || length > MaxFrame) throw new IOException("联机消息大小超限。");
            byte[] header = { (byte)(length >> 24), (byte)(length >> 16), (byte)(length >> 8), (byte)length };
            stream.Write(header, 0, header.Length);
            stream.Write(bytes, 0, length);
        }

        private static void ReadFull(Stream stream, byte[] data)
        {
            int offset = 0;
            while (offset < data.Length)
            {
                int count = stream.Read(data, offset, data.Length - offset);
                if (count == 0) throw new EndOfStreamException();
                offset += count;
            }
        }

        internal static void Configure(TcpClient tcp, int timeout)
        {
            tcp.ReceiveTimeout = timeout;
            tcp.SendTimeout = timeout;
            tcp.NoDelay = true;
        }
    }

    // One reader and one writer per connection, with bounded outgoing storage.
    internal sealed class OnlinePeer : IDisposable
    {
        private readonly TcpClient tcp;
        private readonly SslStream stream;
        private readonly BlockingCollection<OnlinePacket> output = new BlockingCollection<OnlinePacket>(16);
        private int closed;
        internal bool Closed { get { return Volatile.Read(ref closed) != 0; } }

        internal OnlinePeer(TcpClient tcp, SslStream stream) { this.tcp = tcp; this.stream = stream; }
        internal void StartWriter()
        {
            Task.Run(delegate
            {
                try { foreach (OnlinePacket packet in output.GetConsumingEnumerable()) OnlineWire.Write(stream, packet); }
                catch (Exception error) { if (!OnlineWire.LinkFailure(error)) throw; }
                finally { Dispose(); }
            });
        }

        internal bool Queue(OnlinePacket packet)
        {
            try { if (!Closed && output.TryAdd(packet)) return true; }
            catch (InvalidOperationException) { }
            Dispose();
            return false;
        }

        internal OnlinePacket Read() { return OnlineWire.Read(stream); }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref closed, 1) != 0) return;
            output.CompleteAdding();
            tcp.Close();
            stream.Dispose();
        }
    }

    internal sealed class OnlineHost : OnlineSession
    {
        private readonly object gate = new object();
        private readonly OnlineAuthority authority;
        private readonly TcpListener listener;
        private readonly X509Certificate2 certificate;
        private readonly string invitation;
        private readonly string secret = OnlineWire.Token();
        private readonly HashSet<string> commandIds = new HashSet<string>();
        private readonly Queue<string> commandOrder = new Queue<string>();
        private OnlinePeer peer;
        private TcpClient handshake;
        private string resume;
        private string error;
        private bool closed;

        internal OnlineHost(IPAddress address, int port, OnlineAuthority authority)
        {
            this.authority = authority;
            certificate = OnlineWire.CreateCertificate();
            listener = new TcpListener(address, port);
            try
            {
                listener.Start(4);
                invitation = new OnlineInvite
                {
                    Version = OnlineWire.Version, Address = address.ToString(),
                    Port = ((IPEndPoint)listener.LocalEndpoint).Port,
                    Pin = OnlineWire.Pin(certificate), Secret = secret
                }.Format();
                Task.Run((Action)AcceptLoop);
            }
            catch { listener.Stop(); certificate.Dispose(); throw; }
        }

        public override int Seat { get { return 0; } }
        public override string Invitation { get { return invitation; } }
        public override OnlineRoomState State { get { lock (gate) return authority.State; } }
        public override string LastError { get { lock (gate) return error; } }

        public override void Send(OnlineCommand command)
        {
            lock (gate)
            {
                if (closed) return;
                try
                {
                    long revision = command != null && command.Revision != 0 ? command.Revision : authority.State.Revision;
                    authority.Apply(0, revision, command);
                    error = null;
                    Broadcast();
                }
                catch (Exception failure)
                {
                    if (!OnlineWire.DataFailure(failure)) throw;
                    error = failure.Message;
                }
            }
            Notify();
        }

        private void AcceptLoop()
        {
            while (true)
            {
                TcpClient accepted;
                try { accepted = listener.AcceptTcpClient(); }
                catch (SocketException) { return; }
                catch (InvalidOperationException) { return; }
                lock (gate)
                {
                    if (closed) { accepted.Close(); return; }
                    if (peer != null && !peer.Closed) { accepted.Close(); continue; }
                    handshake = accepted;
                }
                try { Accept(accepted); }
                catch (Exception failure)
                {
                    accepted.Close();
                    if (!OnlineWire.LinkFailure(failure)) throw;
                }
                finally { lock (gate) if (handshake == accepted) handshake = null; }
            }
        }

        private void Accept(TcpClient tcp)
        {
            OnlineWire.Configure(tcp, 7000);
            var stream = new SslStream(tcp.GetStream(), false);
            OnlinePeer connected = null;
            try
            {
                OnlinePacket hello;
                // Bound the whole handshake, including a peer that sends one
                // byte at a time before proving it has the invitation.
                using (var deadline = new Timer(delegate { tcp.Close(); }, null, 10000, Timeout.Infinite))
                {
                    stream.AuthenticateAsServer(certificate, false, SslProtocols.Tls12, false);
                    hello = OnlineWire.Read(stream, 4096);
                }
                string name = OnlineAuthority.PlayerName(hello.Name, "棋友");
                lock (gate)
                {
                    if (closed) return;
                    if (hello.Type != "join" || hello.Version != OnlineWire.Version ||
                        !OnlineWire.IsHex(hello.Resume, 48) ||
                        (resume == null ? !OnlineWire.SameSecret(secret, hello.Secret) : !OnlineWire.SameSecret(resume, hello.Resume)))
                        return;
                    // Resume tokens are generated by the guest before its first
                    // handshake, so a lost welcome does not consume its seat.
                    resume = hello.Resume;
                    if (authority.State.GuestName != null) name = authority.State.GuestName;
                    OnlineWire.Configure(tcp, 18000);
                    connected = new OnlinePeer(tcp, stream);
                    peer = connected;
                    authority.Connect(name);
                    error = null;
                    connected.Queue(new OnlinePacket { Type = "welcome", State = authority.State });
                    connected.StartWriter();
                }
                OnlinePeer reader = connected;
                Task.Run(delegate { ReadGuest(reader); });
                Notify();
            }
            finally { if (connected == null) { stream.Dispose(); tcp.Close(); } }
        }

        private void ReadGuest(OnlinePeer from)
        {
            try
            {
                while (!from.Closed)
                {
                    OnlinePacket packet = from.Read();
                    if (packet.Type == "ping") { from.Queue(new OnlinePacket { Type = "pong" }); continue; }
                    if (packet.Type != "command" || packet.Command == null || !OnlineWire.IsHex(packet.Id, 48))
                        throw new IOException("无效的房间命令。");
                    lock (gate)
                    {
                        if (closed || peer != from) return;
                        if (!commandIds.Add(packet.Id)) { Broadcast(); continue; }
                        commandOrder.Enqueue(packet.Id);
                        if (commandOrder.Count > 2048) commandIds.Remove(commandOrder.Dequeue());
                        try
                        {
                            authority.Apply(1, packet.Command.Revision, packet.Command);
                            Broadcast();
                        }
                        catch (Exception failure)
                        {
                            if (!OnlineWire.DataFailure(failure)) throw;
                            from.Queue(new OnlinePacket { Type = "error", Error = failure.Message, State = authority.State });
                        }
                    }
                    Notify();
                }
            }
            catch (Exception failure) { if (!OnlineWire.LinkFailure(failure)) throw; }
            finally
            {
                from.Dispose();
                lock (gate)
                {
                    if (peer == from)
                    {
                        peer = null;
                        authority.Disconnect();
                    }
                }
                Notify();
            }
        }

        private void Broadcast()
        {
            if (peer != null) peer.Queue(new OnlinePacket { Type = "state", State = authority.State });
        }

        public override void Dispose()
        {
            lock (gate)
            {
                if (closed) return;
                closed = true;
                listener.Stop();
                if (handshake != null) handshake.Close();
                if (peer != null) peer.Dispose();
                certificate.Dispose();
            }
        }
    }

    internal sealed class OnlineGuest : OnlineSession
    {
        private readonly object gate = new object();
        private readonly OnlineInvite invite;
        private readonly string name;
        private readonly string resume = OnlineWire.Token();
        private readonly CancellationTokenSource cancel = new CancellationTokenSource();
        private OnlineRoomState state;
        private OnlinePeer peer;
        private TcpClient connecting;
        private string error;
        private bool closed;
        private Task workers;

        internal OnlineGuest(OnlineInvite invite, string name) { this.invite = invite; this.name = name; }
        public override int Seat { get { return 1; } }
        public override OnlineRoomState State { get { lock (gate) return state == null ? null : state.Copy(); } }
        public override string LastError { get { lock (gate) return error; } }

        internal void Start()
        {
            try { Connect(); }
            catch (Exception failure)
            {
                if (!OnlineWire.LinkFailure(failure)) throw;
                throw new InvalidOperationException("无法加入房间。请检查邀请、网络连接和房主的防火墙设置。", failure);
            }
            CancellationToken token = cancel.Token;
            workers = Task.WhenAll(Task.Run(delegate { ReadLoop(token); }), Task.Run(delegate { KeepAlive(token); }));
        }

        private void Connect()
        {
            var tcp = new TcpClient(AddressFamily.InterNetwork);
            SslStream stream = null;
            bool accepted = false;
            try
            {
                lock (gate)
                {
                    if (closed) throw new OperationCanceledException();
                    connecting = tcp;
                }
                OnlineWire.Configure(tcp, 7000);
                IAsyncResult attempt = tcp.BeginConnect(IPAddress.Parse(invite.Address), invite.Port, null, null);
                using (WaitHandle completed = attempt.AsyncWaitHandle)
                {
                    if (!completed.WaitOne(5000)) throw new TimeoutException();
                    tcp.EndConnect(attempt);
                }
                stream = new SslStream(tcp.GetStream(), false, delegate(object sender, X509Certificate cert, X509Chain chain, SslPolicyErrors policy)
                {
                    // Pin the exact per-room certificate carried by the invitation.
                    return cert != null && OnlineWire.SameSecret(invite.Pin, OnlineWire.Pin(cert));
                });
                OnlinePacket welcome;
                using (var deadline = new Timer(delegate { tcp.Close(); }, null, 10000, Timeout.Infinite))
                {
                    stream.AuthenticateAsClient("ZiYi Weiqi Room", null, SslProtocols.Tls12, false);
                    OnlineWire.Write(stream, new OnlinePacket { Type = "join", Version = OnlineWire.Version, Name = name, Secret = invite.Secret, Resume = resume });
                    welcome = OnlineWire.Read(stream);
                }
                if (welcome.Type != "welcome" || welcome.State == null || !welcome.State.Connected)
                    throw new IOException("房间没有接受邀请。");
                OnlineWire.Configure(tcp, 18000);
                lock (gate)
                {
                    if (closed) throw new OperationCanceledException();
                    state = welcome.State;
                    peer = new OnlinePeer(tcp, stream);
                    peer.StartWriter();
                    error = null;
                    accepted = true;
                }
            }
            finally
            {
                lock (gate) if (connecting == tcp) connecting = null;
                if (!accepted)
                {
                    tcp.Close();
                    if (stream != null) stream.Dispose();
                }
            }
        }

        private void KeepAlive(CancellationToken token)
        {
            while (!token.WaitHandle.WaitOne(4000))
            {
                OnlinePeer current;
                lock (gate) current = peer;
                if (current != null) current.Queue(new OnlinePacket { Type = "ping" });
            }
        }

        private void ReadLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                OnlinePeer current;
                lock (gate) current = peer;
                if (current == null) return;
                try
                {
                    while (!current.Closed)
                    {
                        OnlinePacket packet = current.Read();
                        if (packet.Type == "pong") continue;
                        if ((packet.Type != "state" && packet.Type != "error") || packet.State == null)
                            throw new IOException("房间状态无效。");
                        lock (gate)
                        {
                            if (closed) return;
                            if (packet.State.Revision < state.Revision) continue;
                            state = packet.State;
                            error = packet.Type == "error" ? packet.Error : null;
                        }
                        Notify();
                    }
                }
                catch (Exception failure) { if (!OnlineWire.LinkFailure(failure)) throw; }
                finally
                {
                    current.Dispose();
                    lock (gate)
                    {
                        peer = null;
                        state = state.Copy();
                        state.Connected = false;
                        error = "连接中断，正在尝试恢复房间…";
                    }
                }
                if (token.IsCancellationRequested) return;
                Notify();
                Stopwatch deadline = Stopwatch.StartNew();
                bool reconnected = false;
                while (deadline.Elapsed < TimeSpan.FromMinutes(2) && !token.WaitHandle.WaitOne(1200))
                {
                    try { Connect(); reconnected = true; break; }
                    catch (OperationCanceledException) { return; }
                    catch (Exception failure) { if (!OnlineWire.LinkFailure(failure)) throw; }
                }
                if (!reconnected)
                {
                    lock (gate) error = "未能恢复房间。可以保存最后同步的棋谱。";
                    if (!token.IsCancellationRequested) Notify();
                    return;
                }
                Notify();
            }
        }

        public override void Send(OnlineCommand command)
        {
            bool failed;
            lock (gate)
            {
                if (closed || command == null) return;
                if (command.Revision == 0) command.Revision = state.Revision;
                failed = peer == null || !peer.Queue(new OnlinePacket { Type = "command", Id = OnlineWire.Token(), Command = command });
                error = failed ? "等待连接恢复后再操作。" : null;
            }
            if (failed) Notify();
        }

        public override void Dispose()
        {
            lock (gate)
            {
                if (closed) return;
                closed = true;
                cancel.Cancel();
                if (connecting != null) connecting.Close();
                if (peer != null) peer.Dispose();
            }
            if (workers == null) cancel.Dispose();
            else workers.ContinueWith(delegate { cancel.Dispose(); }, TaskScheduler.Default);
        }
    }
}
