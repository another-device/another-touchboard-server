using System.Net;
using System.Net.Sockets;
using System.Text;

namespace AnotherTouchboard.Network;

public class TcpServer
{
    private readonly TcpListener _tcpListener;
    private readonly CancellationTokenSource _cts = new();
    private readonly Dictionary<string, TcpClient> _connectedClients = [];
    private readonly Dictionary<string, DateTime> _clientHeartbeats = [];

    // 事件：客户端连接/断开/接收数据
    public event Action<int> ClientListening;
    public event Action<string> ClientConnected;
    public event Action<string> ClientDisconnected;
    public event Action<string, string> DataReceived; // (clientIp, data)
    public event Action<string> HeartbeatTimeout;     // (clientIp)

    public TcpServer()
    {
        _tcpListener = new TcpListener(IPAddress.Any, 0);
    }

    public void Start()
    {
        _tcpListener.Start();
        ClientListening?.Invoke(((IPEndPoint)_tcpListener.LocalEndpoint).Port);
        _ = AcceptClientsAsync();
        _ = CheckHeartbeatsAsync();
    }

    private async Task AcceptClientsAsync()
    {
        try
        {
            while (!_cts.Token.IsCancellationRequested)
            {
                var client = await _tcpListener.AcceptTcpClientAsync(_cts.Token);
                var clientIp = ((IPEndPoint)client.Client.RemoteEndPoint!).Address.ToString();

                lock (_connectedClients)
                {
                    _connectedClients[clientIp] = client;
                    _clientHeartbeats[clientIp] = DateTime.Now;
                }
                ClientConnected?.Invoke(clientIp);

                // 处理客户端消息
                _ = HandleClientAsync(client, clientIp);
            }
        }
        catch (OperationCanceledException)
        {
            // 正常退出
        }
    }

    private async Task HandleClientAsync(TcpClient client, string clientIp)
    {
        try
        {
            using var stream = client.GetStream();
            var buffer = new byte[1024];
            int bytesRead;

            while ((bytesRead = await stream.ReadAsync(buffer, _cts.Token)) > 0)
            {
                var data = Encoding.UTF8.GetString(buffer, 0, bytesRead).Trim();

                // 心跳包处理
                if (data == "<3")
                {
                    lock (_clientHeartbeats)
                        _clientHeartbeats[clientIp] = DateTime.Now;
                    continue;
                }

                if (!string.IsNullOrEmpty(data))
                {
                    var split_data = data.TrimEnd('/').Split('/');
                    foreach (string s in split_data)
                    {
                        DataReceived?.Invoke(clientIp, s);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 正常退出
        }
        finally
        {
            lock (_connectedClients)
            {
                _connectedClients.Remove(clientIp);
                _clientHeartbeats.Remove(clientIp);
            }
            ClientDisconnected?.Invoke(clientIp);
            client.Close();
        }
    }

    // 心跳检测（60秒超时）
    private async Task CheckHeartbeatsAsync()
    {
        while (!_cts.Token.IsCancellationRequested)
        {
            var now = DateTime.Now;
            var timeoutClients = new List<string>();

            lock (_clientHeartbeats)
            {
                foreach (var (ip, lastBeat) in _clientHeartbeats)
                {
                    if ((now - lastBeat).TotalSeconds > 60)
                    {
                        timeoutClients.Add(ip);
                    }
                }
            }

            foreach (var ip in timeoutClients)
            {
                HeartbeatTimeout?.Invoke(ip);
                lock (_connectedClients)
                {
                    if (_connectedClients.TryGetValue(ip, out var client))
                    {
                        client.Close();
                    }
                    _connectedClients.Remove(ip);
                    _clientHeartbeats.Remove(ip);
                }
            }

            await Task.Delay(2000, _cts.Token);
        }
    }

    public void Stop()
    {
        _cts.Cancel();
        _tcpListener.Stop();
        lock (_connectedClients)
        {
            foreach (var client in _connectedClients.Values)
                client.Close();
            _connectedClients.Clear();
            _clientHeartbeats.Clear();
        }
    }
}
