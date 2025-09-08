using System.Net;
using System.Net.Sockets;
using System.Text;

namespace AnotherTouchboard.Network;

public class UdpBroadcaster
{
    private const int BROADCAST_PORT = 47126;

    private readonly UdpClient _udpClient;
    private readonly CancellationTokenSource _cts;
    private readonly string _localIp;
    private readonly int _tcpPort;

    private bool isServerConnected = false;

    public event Action<string, string> BoardcastSent;

    public UdpBroadcaster(int tcpPort = 8888, string broadcastIp = "192.168.0.255", int broadcastPort = BROADCAST_PORT)
    {
        _udpClient = new UdpClient();
        _udpClient.EnableBroadcast = true;
        _cts = new CancellationTokenSource();
        _localIp = GetLocalIpAddress() ?? throw new InvalidOperationException("无法获取本地IP");
        _tcpPort = tcpPort;
        BroadcastEndPoint = new IPEndPoint(IPAddress.Parse(broadcastIp), broadcastPort);
    }

    public IPEndPoint BroadcastEndPoint { get; }

    public void Start()
    {
        _ = BroadcastLoopAsync();
    }

    private async Task BroadcastLoopAsync()
    {
        string message = $"a-touchboard-server;{_localIp};{_tcpPort}";
        var data = Encoding.UTF8.GetBytes(message);

        BoardcastSent?.Invoke(_localIp, _tcpPort.ToString());

        while (!_cts.Token.IsCancellationRequested)
        {
            if (isServerConnected)
            {
                await Task.Delay(5000, _cts.Token);
                continue;
            }
            await _udpClient.SendAsync(data, data.Length, BroadcastEndPoint);
            await Task.Delay(2000, _cts.Token); // 每2秒广播一次
        }
    }

    private static string GetLocalIpAddress()
    {
        var host = Dns.GetHostEntry(Dns.GetHostName());
        foreach (var ip in host.AddressList)
        {
            if (ip.AddressFamily == AddressFamily.InterNetwork)
                return ip.ToString();
        }
        return string.Empty;
    }

    public void Stop()
    {
        _cts.Cancel();
        _udpClient.Close();
    }
}
