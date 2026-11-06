using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using NativeWebView.Interop;

namespace NativeWebView.Core.Tests;

[Collection(nameof(DirectSocksForwarderCollection))]
public sealed class DirectSocksForwarderTests
{
    internal static void AssertEndpointReserved(int port)
    {
        using var competitor = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        competitor.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        Assert.Throws<SocketException>(() =>
        {
            competitor.Bind(new IPEndPoint(IPAddress.Loopback, port));
            competitor.Listen(1);
        });
    }

    private static async Task AssertClosedAsync(NetworkStream stream)
    {
        try { Assert.Equal(0, await stream.ReadAsync(new byte[1]).AsTask().WaitAsync(TimeSpan.FromSeconds(3))); }
        catch (IOException) { } // A prompt TCP reset is also a rejection.
    }

    [Fact]
    public async Task FailedService_ReservesEndpointCancelsTunnelsAndRejectsNewClients()
    {
        await using var proxy = new DirectSocksForwarder();
        using var origin = new TcpListener(IPAddress.Loopback, 0);
        origin.Start();
        using var client = await AuthenticateAsync(proxy);
        Assert.Equal(0, await ConnectAsync(client, "127.0.0.1", ((IPEndPoint)origin.LocalEndpoint).Port, 1));
        using var peer = await origin.AcceptTcpClientAsync();
        AssertEndpointReserved(proxy.Port);
        proxy.FailClosed();
        proxy.FailClosed();
        Assert.False(proxy.IsHealthy);
        AssertEndpointReserved(proxy.Port);
        await AssertClosedAsync(client.GetStream());
        await AssertClosedAsync(peer.GetStream());
        for (var attempt = 0; attempt < 8; attempt++)
        {
            using var rejected = new TcpClient();
            await rejected.ConnectAsync(IPAddress.Loopback, proxy.Port);
            await AssertClosedAsync(rejected.GetStream());
            AssertEndpointReserved(proxy.Port);
        }
    }

    [Fact]
    public async Task AcceptFailure_UsesSameBoundEndpointAndDoesNotResumeForwarding()
    {
        var failNextAccept = 0;
        await using var proxy = new DirectSocksForwarder(accept: (listener, token) =>
        {
            if (Interlocked.Exchange(ref failNextAccept, 0) != 0)
                throw new SocketException((int)SocketError.TooManyOpenSockets);
            return listener.AcceptTcpClientAsync(token);
        });
        Interlocked.Exchange(ref failNextAccept, 1);
        using var trigger = new TcpClient();
        await trigger.ConnectAsync(IPAddress.Loopback, proxy.Port);
        await AssertClosedAsync(trigger.GetStream());
        Assert.False(proxy.IsHealthy);
        AssertEndpointReserved(proxy.Port);
        using var rejected = new TcpClient();
        await rejected.ConnectAsync(IPAddress.Loopback, proxy.Port);
        await AssertClosedAsync(rejected.GetStream());
        AssertEndpointReserved(proxy.Port);
    }

    [Fact]
    public async Task PersistentAcceptFailure_BacksOffWhileKeepingEndpointReserved()
    {
        var attempts = 0;
        await using var proxy = new DirectSocksForwarder(accept: (_, _) =>
        {
            Interlocked.Increment(ref attempts);
            throw new SocketException((int)SocketError.TooManyOpenSockets);
        });
        Assert.False(proxy.IsHealthy);
        AssertEndpointReserved(proxy.Port);
        await Task.Delay(350);
        Assert.InRange(Volatile.Read(ref attempts), 1, 10);
        AssertEndpointReserved(proxy.Port);
        await proxy.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3));
    }

    [Fact]
    public async Task UnauthenticatedFlood_IsBoundedAndEstablishedTunnelRemainsUsable()
    {
        await using var proxy = new DirectSocksForwarder(maxConnections: 4,
            maxPendingHandshakes: 2, timeout: TimeSpan.FromSeconds(2));
        using var origin = new TcpListener(IPAddress.Loopback, 0);
        origin.Start();
        using var active = await AuthenticateAsync(proxy);
        Assert.Equal(0, await ConnectAsync(active, "127.0.0.1", ((IPEndPoint)origin.LocalEndpoint).Port, 1));
        using var peer = await origin.AcceptTcpClientAsync();
        using var pendingOne = new TcpClient();
        using var pendingTwo = new TcpClient();
        foreach (var pending in new[] { pendingOne, pendingTwo })
        {
            await pending.ConnectAsync(IPAddress.Loopback, proxy.Port);
            await pending.GetStream().WriteAsync(new byte[] { 5, 1, 2 });
            Assert.Equal(new byte[] { 5, 2 }, await ReadAsync(pending.GetStream(), 2));
        }
        for (var attempt = 0; attempt < 16; attempt++)
        {
            using var excess = new TcpClient();
            await excess.ConnectAsync(IPAddress.Loopback, proxy.Port);
            await AssertClosedAsync(excess.GetStream());
            await active.GetStream().WriteAsync(new byte[] { (byte)attempt });
            Assert.Equal(new byte[] { (byte)attempt }, await ReadAsync(peer.GetStream(), 1));
        }
        await AssertClosedAsync(pendingOne.GetStream());
        await AssertClosedAsync(pendingTwo.GetStream());
        using var recovered = await AuthenticateAsync(proxy);
        Assert.Equal(0, await ConnectAsync(recovered, "127.0.0.1", ((IPEndPoint)origin.LocalEndpoint).Port, 1));
        using var recoveredPeer = await origin.AcceptTcpClientAsync();
        Assert.True(proxy.IsHealthy);
    }

    [Fact]
    public async Task AuthenticatedConnections_ReleaseHandshakeCapacityButRespectTotalLimit()
    {
        await using var proxy = new DirectSocksForwarder(maxConnections: 2, maxPendingHandshakes: 1);
        using var origin = new TcpListener(IPAddress.Loopback, 0);
        origin.Start();
        using var first = await AuthenticateAsync(proxy);
        Assert.Equal(0, await ConnectAsync(first, "127.0.0.1", ((IPEndPoint)origin.LocalEndpoint).Port, 1));
        using var firstPeer = await origin.AcceptTcpClientAsync();
        using var second = await AuthenticateAsync(proxy);
        Assert.Equal(0, await ConnectAsync(second, "127.0.0.1", ((IPEndPoint)origin.LocalEndpoint).Port, 1));
        using var secondPeer = await origin.AcceptTcpClientAsync();
        using var excess = new TcpClient();
        await excess.ConnectAsync(IPAddress.Loopback, proxy.Port);
        await AssertClosedAsync(excess.GetStream());
    }

    [Fact]
    public async Task FailureAndDisposalRace_CompletesAndReleasesPort()
    {
        var proxy = new DirectSocksForwarder();
        var port = proxy.Port;
        await Task.WhenAll(Task.Run(proxy.FailClosed), Task.Run(async () => await proxy.DisposeAsync()))
            .WaitAsync(TimeSpan.FromSeconds(3));
        proxy.FailClosed();
        using var next = new TcpListener(IPAddress.Loopback, port);
        next.ExclusiveAddressUse = true;
        next.Start();
    }

    private static async Task<byte[]> ReadAsync(NetworkStream stream, int count)
    {
        var bytes = new byte[count];
        await stream.ReadExactlyAsync(bytes).AsTask().WaitAsync(TimeSpan.FromSeconds(10));
        return bytes;
    }

    private static async Task<TcpClient> AuthenticateAsync(DirectSocksForwarder forwarder, string? password = null, bool fragment = false)
    {
        var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, forwarder.Port);
        try
        {
            var stream = client.GetStream();
            await stream.WriteAsync(new byte[] { 5, 1, 2 });
            Assert.Equal(new byte[] { 5, 2 }, await ReadAsync(stream, 2));
            var user = Encoding.ASCII.GetBytes(forwarder.Username);
            var pass = Encoding.ASCII.GetBytes(password ?? forwarder.Password);
            byte[] auth = [1, (byte)user.Length, .. user, (byte)pass.Length, .. pass];
            if (fragment)
            {
                foreach (var value in auth)
                    await stream.WriteAsync(new byte[] { value });
            }
            else
                await stream.WriteAsync(auth);
            Assert.Equal(new byte[] { 1, password is null ? (byte)0 : (byte)1 }, await ReadAsync(stream, 2));
            return client;
        }
        catch { client.Dispose(); throw; }
    }

    private static async Task<byte> ConnectAsync(TcpClient client, string host, int port, byte addressType)
    {
        byte[] address = addressType == 3
            ? [(byte)host.Length, .. Encoding.ASCII.GetBytes(host)] : IPAddress.Parse(host).GetAddressBytes();
        await client.GetStream().WriteAsync(new byte[] { 5, 1, 0, addressType }.Concat(address).Concat(new byte[] { (byte)(port >> 8), (byte)port }).ToArray());
        var header = await ReadAsync(client.GetStream(), 4);
        Assert.Equal(5, header[0]);
        await ReadAsync(client.GetStream(), (header[3] == 4 ? 16 : 4) + 2);
        return header[1];
    }

    [Theory]
    [InlineData("127.0.0.1", 1)]
    [InlineData("localhost", 3)]
    [InlineData("::1", 4)]
    public async Task Connect_RelaysLargePayloadAndHalfClose(string host, byte type)
    {
        var address = type == 4 ? IPAddress.IPv6Loopback : IPAddress.Loopback;
        using var origin = new TcpListener(address, 0);
        origin.Start();
        await using var proxy = new DirectSocksForwarder();
        using var client = await AuthenticateAsync(proxy, fragment: true);
        var accept = origin.AcceptTcpClientAsync();
        Assert.Equal(0, await ConnectAsync(client, host, ((IPEndPoint)origin.LocalEndpoint).Port, type));
        using var peer = await accept.WaitAsync(TimeSpan.FromSeconds(10));
        var payload = RandomNumberGenerator.GetBytes(2 * 1024 * 1024);
        var originWork = Task.Run(async () =>
        {
            using var received = new MemoryStream();
            await peer.GetStream().CopyToAsync(received);
            Assert.Equal(payload, received.ToArray());
            await peer.GetStream().WriteAsync(SHA256.HashData(received.ToArray()));
            peer.Client.Shutdown(SocketShutdown.Send);
        });
        var stream = client.GetStream();
        await stream.WriteAsync(payload);
        client.Client.Shutdown(SocketShutdown.Send);
        Assert.Equal(SHA256.HashData(payload), await ReadAsync(stream, 32));
        Assert.Equal(0, await stream.ReadAsync(new byte[1]));
        await originWork.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task ImmediateClientHalfClose_AllowsOriginResponse()
    {
        await using var proxy = new DirectSocksForwarder();
        using var origin = new TcpListener(IPAddress.Loopback, 0);
        origin.Start();
        using var client = await AuthenticateAsync(proxy);
        var stream = client.GetStream();
        Assert.Equal(0, await ConnectAsync(client, "127.0.0.1", ((IPEndPoint)origin.LocalEndpoint).Port, 1));
        client.Client.Shutdown(SocketShutdown.Send);
        using var peer = await origin.AcceptTcpClientAsync();
        var peerStream = peer.GetStream();
        Assert.Equal(0, await peerStream.ReadAsync(new byte[1]).AsTask().WaitAsync(TimeSpan.FromSeconds(3)));
        await peerStream.WriteAsync(new byte[] { 17 });
        Assert.Equal(new byte[] { 17 }, await ReadAsync(stream, 1));
    }

    [Fact]
    public async Task Authentication_RejectsMissingAndWrongCredentials()
    {
        await using var proxy = new DirectSocksForwarder();
        using var unauthenticated = new TcpClient();
        await unauthenticated.ConnectAsync(IPAddress.Loopback, proxy.Port);
        await unauthenticated.GetStream().WriteAsync(new byte[] { 5, 1, 0 });
        Assert.Equal(new byte[] { 5, 255 }, await ReadAsync(unauthenticated.GetStream(), 2));
        using var wrong = await AuthenticateAsync(proxy, "incorrect");
        Assert.Equal(0, await wrong.GetStream().ReadAsync(new byte[1]));
    }

    [Theory]
    [InlineData(2, 1, 0, 7)]
    [InlineData(3, 1, 0, 7)]
    [InlineData(1, 9, 0, 8)]
    [InlineData(1, 1, 1, 1)]
    public async Task UnsupportedAndMalformedRequests_ReturnProtocolFailure(byte command, byte addressType, byte reserved, byte expected)
    {
        await using var proxy = new DirectSocksForwarder();
        using var client = await AuthenticateAsync(proxy);
        await client.GetStream().WriteAsync(new byte[] { 5, command, reserved, addressType });
        Assert.Equal(expected, (await ReadAsync(client.GetStream(), 10))[1]);
    }

    [Fact]
    public async Task OutboundFailures_ReturnReplies()
    {
        using var closed = new TcpListener(IPAddress.Loopback, 0);
        closed.Start();
        var port = ((IPEndPoint)closed.LocalEndpoint).Port;
        closed.Stop();
        await using var proxy = new DirectSocksForwarder();
        using var client = await AuthenticateAsync(proxy);
        Assert.Equal(5, await ConnectAsync(client, "127.0.0.1", port, 1));
        using var dns = await AuthenticateAsync(proxy);
        Assert.NotEqual(0, await ConnectAsync(dns, "nativewebview.invalid", 80, 3));
    }

    [Fact]
    public async Task HandshakeTimeoutAndCapacity_AreBounded()
    {
        await using var proxy = new DirectSocksForwarder(maxConnections: 1, timeout: TimeSpan.FromMilliseconds(300));
        using var first = new TcpClient();
        await first.ConnectAsync(IPAddress.Loopback, proxy.Port);
        await first.GetStream().WriteAsync(new byte[] { 5, 1, 2 });
        await ReadAsync(first.GetStream(), 2); // Slot is definitely occupied.
        using var excess = new TcpClient();
        await excess.ConnectAsync(IPAddress.Loopback, proxy.Port);
        Assert.Equal(0, await excess.GetStream().ReadAsync(new byte[1]).AsTask().WaitAsync(TimeSpan.FromSeconds(3)));
        Assert.Equal(0, await first.GetStream().ReadAsync(new byte[1]).AsTask().WaitAsync(TimeSpan.FromSeconds(3)));
    }

    [Fact]
    public async Task Disposal_CancelsActiveAndIncompleteConnections_AndIsIdempotent()
    {
        var proxy = new DirectSocksForwarder();
        using var origin = new TcpListener(IPAddress.Loopback, 0);
        origin.Start();
        using var active = await AuthenticateAsync(proxy);
        Assert.Equal(0, await ConnectAsync(active, "127.0.0.1", ((IPEndPoint)origin.LocalEndpoint).Port, 1));
        using var peer = await origin.AcceptTcpClientAsync();
        using var incomplete = new TcpClient();
        await incomplete.ConnectAsync(IPAddress.Loopback, proxy.Port);
        await Task.WhenAll(proxy.DisposeAsync().AsTask(), proxy.DisposeAsync().AsTask()).WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(0, await active.GetStream().ReadAsync(new byte[1]));
        Assert.Equal(0, await peer.GetStream().ReadAsync(new byte[1]));
    }

    [Fact]
    public async Task EstablishedTunnel_RemainsUsableBeyondPrototypeLifetime()
    {
        await using var proxy = new DirectSocksForwarder();
        using var origin = new TcpListener(IPAddress.Loopback, 0);
        origin.Start();
        using var client = await AuthenticateAsync(proxy);
        Assert.Equal(0, await ConnectAsync(client, "127.0.0.1", ((IPEndPoint)origin.LocalEndpoint).Port, 1));
        using var peer = await origin.AcceptTcpClientAsync();
        await Task.Delay(TimeSpan.FromSeconds(61));
        await client.GetStream().WriteAsync(new byte[] { 42 });
        Assert.Equal(new byte[] { 42 }, await ReadAsync(peer.GetStream(), 1));
    }
}

// Keep socket deadlines independent of concurrent native stress and full-GC tests.
[CollectionDefinition(nameof(DirectSocksForwarderCollection), DisableParallelization = true)]
public sealed class DirectSocksForwarderCollection
{
}
