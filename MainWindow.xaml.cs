using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;

namespace AnotherTouchboard
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        private TcpListener _tcpListener;
        private UdpClient _udpClient;
        private CancellationTokenSource _cancellationTokenSource;
        private Dictionary<string, TcpClient> _connectedClients = [];
        private Dictionary<string, DateTime> _clientLastHeartbeat = [];
        private ObservableCollection<string> _pressedKeys = [];
        private ObservableCollection<string> _connectedClientsList = [];
        private string _logText = "";

        public ObservableCollection<string> PressedKeys => _pressedKeys;
        public ObservableCollection<string> ConnectedClients => _connectedClientsList;

        public string LogText
        {
            get => _logText;
            set
            {
                _logText = value;
                OnPropertyChanged(nameof(LogText));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public MainWindow()
        {
            InitializeComponent();
            DataContext = this;
            _cancellationTokenSource = new CancellationTokenSource();

            // 启动服务
            StartTcpServer();
            StartUdpBroadcaster();

            AddLog("程序已启动，等待连接...");

            // 启动心跳检测定时器
            StartHeartbeatChecker();
        }

        private void StartTcpServer()
        {
            Task.Run(async () =>
            {
                try
                {
                    _tcpListener = new TcpListener(IPAddress.Any, 8888);
                    _tcpListener.Start();

                    Dispatcher.Invoke(() => AddLog("TCP服务器已启动，监听端口 8888"));

                    while (!_cancellationTokenSource.Token.IsCancellationRequested)
                    {
                        var client = await _tcpListener.AcceptTcpClientAsync(_cancellationTokenSource.Token);
                        var clientIp = ((IPEndPoint)client.Client.RemoteEndPoint!).Address.ToString();

                        Dispatcher.Invoke(() =>
                        {
                            AddLog($"客户端 {clientIp} 已连接");
                            _connectedClients[clientIp] = client;
                            _connectedClientsList.Add(clientIp);
                            _clientLastHeartbeat[clientIp] = DateTime.Now;
                        });

                        // 处理客户端消息
                        HandleClient(client, clientIp);
                    }
                }
                catch (OperationCanceledException)
                {
                    // 正常退出
                }
                catch (Exception ex)
                {
                    Dispatcher.Invoke(() => AddLog($"TCP服务器错误: {ex.Message}"));
                }
            }, _cancellationTokenSource.Token);
        }

        private async void HandleClient(TcpClient client, string clientIp)
        {
            try
            {
                using (var stream = client.GetStream())
                {
                    byte[] buffer = new byte[1024];
                    int bytesRead;

                    while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, _cancellationTokenSource.Token)) > 0)
                    {
                        string data = Encoding.UTF8.GetString(buffer, 0, bytesRead).Trim();
                        Dispatcher.Invoke(() => AddLog($"收到来自 {clientIp} 的数据: {data}"));

                        // 心跳包处理
                        if (data == "<3")
                        {
                            _clientLastHeartbeat[clientIp] = DateTime.Now;
                            continue;
                        }

                        // 解析数据并处理按键
                        ProcessKeyData(data);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // 正常退出
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() => AddLog($"客户端 {clientIp} 错误: {ex.Message}"));
            }
            finally
            {
                Dispatcher.Invoke(() =>
                {
                    AddLog($"客户端 {clientIp} 已断开连接");
                    _connectedClients.Remove(clientIp);
                    _connectedClientsList.Remove(clientIp);
                    _clientLastHeartbeat.Remove(clientIp);
                });
                client.Close();
            }
        }

        private void ProcessKeyData(string data)
        {
            try
            {
                // 数据格式: KeyCode,IsPressed (例如: A,True 或 ENTER,False)
                var parts = data.Split(',');
                if (parts.Length != 2)
                {
                    AddLog($"无效的数据格式: {data}");
                    return;
                }

                string keyCode = parts[0].Trim();
                if (!bool.TryParse(parts[1].Trim(), out bool isPressed))
                {
                    AddLog($"无效的按键状态: {parts[1]}");
                    return;
                }

                ushort virtualKeyCode = 0;
                if (ushort.TryParse(keyCode, out ushort vk))
                {
                    virtualKeyCode = vk;
                }
                else
                {
                    AddLog($"未知的按键代码: {keyCode}");
                    return;
                }

                // 模拟按键
                SimulateKey(virtualKeyCode, isPressed);

                // 更新UI
                Dispatcher.Invoke(() =>
                {
                    if (isPressed && !_pressedKeys.Contains(keyCode))
                    {
                        _pressedKeys.Add(keyCode);
                    }
                    else if (!isPressed && _pressedKeys.Contains(keyCode))
                    {
                        _pressedKeys.Remove(keyCode);
                    }
                });
            }
            catch (Exception ex)
            {
                AddLog($"处理按键数据错误: {ex.Message}");
            }
        }

        private void StartUdpBroadcaster()
        {
            Task.Run(async () =>
            {
                try
                {
                    _udpClient = new UdpClient();
                    _udpClient.EnableBroadcast = true;
                    IPEndPoint broadcastEndPoint = new(IPAddress.Parse("192.168.0.255"), 8889);

                    // 获取本机IP地址
                    var localIp = GetLocalIPAddress();
                    if (string.IsNullOrEmpty(localIp))
                    {
                        Dispatcher.Invoke(() => AddLog("无法获取本机IP地址"));
                        return;
                    }

                    string message = $"AnotherTouchboardServer;{localIp};8888";
                    byte[] data = Encoding.UTF8.GetBytes(message);

                    Dispatcher.Invoke(() => AddLog($"开始在局域网广播 (IP: {localIp}, 端口: 8889)"));

                    // 每2秒广播一次
                    while (!_cancellationTokenSource.Token.IsCancellationRequested)
                    {
                        if (_connectedClients.Count > 0)
                        {
                            // 有客户端连接时不广播
                            await Task.Delay(10000, _cancellationTokenSource.Token);
                        }
                        else
                        {
                            await _udpClient.SendAsync(data, data.Length, broadcastEndPoint);
                            await Task.Delay(2000, _cancellationTokenSource.Token);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    // 正常退出
                }
                catch (Exception ex)
                {
                    Dispatcher.Invoke(() => AddLog($"UDP广播错误: {ex.Message}"));
                }
            }, _cancellationTokenSource.Token);
        }

        private static string GetLocalIPAddress()
        {
            var host = Dns.GetHostEntry(Dns.GetHostName());
            foreach (var ip in host.AddressList)
            {
                if (ip.AddressFamily == AddressFamily.InterNetwork)
                {
                    return ip.ToString();
                }
            }
            return string.Empty;
        }

        private void AddLog(string message)
        {
            string logEntry = $"[{DateTime.Now:HH:mm:ss}] {message}\n";
            LogText = logEntry + LogText; // 新日志添加到顶部

            // 限制日志长度，防止内存溢出
            if (LogText.Length > 10000)
            {
                LogText = LogText.Substring(0, 10000);
            }
        }

        #region 按键模拟 (使用SendInput)

        // 心跳检测定时器
        private void StartHeartbeatChecker()
        {
            Task.Run(async () =>
            {
                while (!_cancellationTokenSource.Token.IsCancellationRequested)
                {
                    List<string> toRemove = [];
                    var now = DateTime.Now;
                    foreach (var kvp in _clientLastHeartbeat)
                    {
                        if ((now - kvp.Value).TotalSeconds > 60)
                        {
                            toRemove.Add(kvp.Key);
                        }
                    }
                    foreach (var clientIp in toRemove)
                    {
                        Dispatcher.Invoke(() =>
                        {
                            AddLog($"客户端 {clientIp} 心跳超时，主动断开");
                            if (_connectedClients.TryGetValue(clientIp, out var client))
                            {
                                client.Close();
                            }
                            _connectedClients.Remove(clientIp);
                            _connectedClientsList.Remove(clientIp);
                            _clientLastHeartbeat.Remove(clientIp);
                        });
                    }
                    await Task.Delay(2000, _cancellationTokenSource.Token);
                }
            }, _cancellationTokenSource.Token);
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT
        {
            public uint type;
            public InputUnion U;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct InputUnion
        {
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public KEYBDINPUT ki;
            [FieldOffset(0)] public HARDWAREINPUT hi;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct HARDWAREINPUT
        {
            public uint uMsg;
            public ushort wParamL;
            public ushort wParamH;
        }

        private const uint INPUT_KEYBOARD = 1;
        private const uint KEYEVENTF_KEYDOWN = 0;
        private const uint KEYEVENTF_KEYUP = 2;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        private void SimulateKey(ushort keyCode, bool isPressed)
        {
            INPUT[] input = new INPUT[1];
            input[0].type = INPUT_KEYBOARD;
            input[0].U.ki.wVk = keyCode;
            input[0].U.ki.dwFlags = isPressed ? KEYEVENTF_KEYDOWN : KEYEVENTF_KEYUP;

            uint result = SendInput(1, input, Marshal.SizeOf(typeof(INPUT)));
            if (result == 0)
            {
                AddLog($"模拟按键失败，错误代码: {Marshal.GetLastWin32Error()}");
            }
        }
        #endregion

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            _cancellationTokenSource.Cancel();
            _tcpListener?.Stop();
            _udpClient?.Close();

            foreach (var client in _connectedClients.Values)
            {
                client.Close();
            }
        }
    }
}
