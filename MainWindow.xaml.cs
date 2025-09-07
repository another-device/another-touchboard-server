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
        private Dictionary<string, TcpClient> _connectedClients = new Dictionary<string, TcpClient>();
        private ObservableCollection<string> _pressedKeys = new ObservableCollection<string>();
        private ObservableCollection<string> _connectedClientsList = new ObservableCollection<string>();
        private string _logText = "";

        // 按键映射表，将字符串映射到VirtualKeyCode
        private Dictionary<string, ushort> _keyCodeMap = new Dictionary<string, ushort>
        {
            {"A", 0x41}, {"B", 0x42}, {"C", 0x43}, {"D", 0x44}, {"E", 0x45}, {"F", 0x46},
            {"G", 0x47}, {"H", 0x48}, {"I", 0x49}, {"J", 0x4A}, {"K", 0x4B}, {"L", 0x4C},
            {"M", 0x4D}, {"N", 0x4E}, {"O", 0x4F}, {"P", 0x50}, {"Q", 0x51}, {"R", 0x52},
            {"S", 0x53}, {"T", 0x54}, {"U", 0x55}, {"V", 0x56}, {"W", 0x57}, {"X", 0x58},
            {"Y", 0x59}, {"Z", 0x5A},
            {"0", 0x30}, {"1", 0x31}, {"2", 0x32}, {"3", 0x33}, {"4", 0x34},
            {"5", 0x35}, {"6", 0x36}, {"7", 0x37}, {"8", 0x38}, {"9", 0x39},
            {"F1", 0x70}, {"F2", 0x71}, {"F3", 0x72}, {"F4", 0x73}, {"F5", 0x74},
            {"F6", 0x75}, {"F7", 0x76}, {"F8", 0x77}, {"F9", 0x78}, {"F10", 0x79},
            {"F11", 0x7A}, {"F12", 0x7B},
            {"ENTER", 0x0D}, {"BACKSPACE", 0x08}, {"TAB", 0x09}, {"ESCAPE", 0x1B},
            {"SPACE", 0x20}, {"SHIFT", 0x10}, {"CONTROL", 0x11}, {"ALT", 0x12},
            {"CAPSLOCK", 0x14}, {"NUMLOCK", 0x90}, {"SCROLLLOCK", 0x91},
            {"UP", 0x26}, {"DOWN", 0x28}, {"LEFT", 0x25}, {"RIGHT", 0x27},
            {"HOME", 0x24}, {"END", 0x23}, {"PAGEUP", 0x21}, {"PAGEDOWN", 0x22},
            {"INSERT", 0x2D}, {"DELETE", 0x2E},
            {"PLUS", 0xBB}, {"MINUS", 0xBD}, {"MULTIPLY", 0x6A}, {"DIVIDE", 0x6F},
            {"COMMA", 0xBC}, {"PERIOD", 0xBE}, {"SLASH", 0xBF}, {"SEMICOLON", 0xBA},
            {"EQUALS", 0xBB}, {"LEFTBRACKET", 0xDB}, {"RIGHTBRACKET", 0xDD},
            {"BACKQUOTE", 0xC0}, {"APOSTROPHE", 0xDE}, {"BACKSLASH", 0xDC}
        };

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
                        var clientIp = ((IPEndPoint)client.Client.RemoteEndPoint).Address.ToString();

                        Dispatcher.Invoke(() =>
                        {
                            AddLog($"客户端 {clientIp} 已连接");
                            _connectedClients[clientIp] = client;
                            _connectedClientsList.Add(clientIp);
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

                // 查找对应的虚拟键码
                if (!_keyCodeMap.TryGetValue(keyCode, out ushort virtualKeyCode))
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
                    IPEndPoint broadcastEndPoint = new IPEndPoint(IPAddress.Broadcast, 8889);

                    // 获取本机IP地址
                    var localIp = GetLocalIPAddress();
                    if (string.IsNullOrEmpty(localIp))
                    {
                        Dispatcher.Invoke(() => AddLog("无法获取本机IP地址"));
                        return;
                    }

                    string message = $"AnotherTouchboardServer;{localIp};8889";
                    byte[] data = Encoding.UTF8.GetBytes(message);

                    Dispatcher.Invoke(() => AddLog($"开始在局域网广播 (IP: {localIp}, 端口: 8889)"));

                    // 每5秒广播一次
                    while (!_cancellationTokenSource.Token.IsCancellationRequested)
                    {
                        await _udpClient.SendAsync(data, data.Length, broadcastEndPoint);
                        await Task.Delay(5000, _cancellationTokenSource.Token);
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

        private string GetLocalIPAddress()
        {
            var host = Dns.GetHostEntry(Dns.GetHostName());
            foreach (var ip in host.AddressList)
            {
                if (ip.AddressFamily == AddressFamily.InterNetwork)
                {
                    return ip.ToString();
                }
            }
            return null;
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
