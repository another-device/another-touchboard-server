using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using AnotherTouchboard.Input;
using AnotherTouchboard.Network;

namespace AnotherTouchboard.Views
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        private CancellationTokenSource _cancellationTokenSource = new();
        private ObservableCollection<string> _pressedKeys = [];
        private ObservableCollection<string> _connectedClientsList = [];
        private string _logText = "";

        private UdpBroadcaster _udpBroadcaster;
        private TcpServer _tcpServer;

        public ObservableCollection<string> PressedKeys { get => _pressedKeys; }
        public ObservableCollection<string> ConnectedClients { get => _connectedClientsList; }

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

            _tcpServer = new TcpServer();

            // 订阅TCP事件
            _tcpServer.ClientListening += clientPort =>
            {
                _udpBroadcaster = new UdpBroadcaster(clientPort);
                // 订阅UDP事件
                _udpBroadcaster.BoardcastSent += (localIp, localPort) =>
                {
                    AddLog($"开始在局域网广播 (IP: {localIp}, 端口: {localPort})");
                };
                _udpBroadcaster.Start();
            };
            _tcpServer.ClientConnected += clientIp =>
            {
                AddLog($"客户端 {clientIp} 已连接");
                _connectedClientsList.Add(clientIp);
            };
            _tcpServer.ClientDisconnected += ip =>
            {
                AddLog($"客户端 {ip} 已断开");
                _connectedClientsList.Remove(ip);
            };
            _tcpServer.DataReceived += (clientIp, data) =>
            {
                AddLog($"收到来自 {clientIp} 的数据: {data}");
                ProcessKeyData(data);
            };
            _tcpServer.HeartbeatTimeout += (ip) =>
            {
                AddLog($"客户端 {ip} 心跳超时");
            };

            // 启动UDP和TCP服务
            AddLog("程序已启动，等待连接...");
            _tcpServer.Start();
        }

        private void ProcessKeyData(string data)
        {
            try
            {
                // 数据格式: KeyCode,IsPressed (例如: 65,true 或 13,false)
                var parts = data.Split(',');
                if (parts.Length != 2)
                {
                    AddLog($"无效的数据格式: {data}");
                    return;
                }

                string keyCode = parts[0].Trim();
                if (!ushort.TryParse(parts[1].Trim(), out ushort pressedFlag))
                {
                    AddLog($"无效的按键状态: {parts[1]}");
                    return;
                }
                bool isPressed = pressedFlag == 1;

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
                KeySimulator.SimulateKey(virtualKeyCode, isPressed);

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

        public void AddLog(string message)
        {
            string logEntry = $"[{DateTime.Now:HH:mm:ss}] {message}\n";
            LogText = logEntry + LogText; // 新日志添加到顶部

            // 限制日志长度，防止内存溢出
            if (LogText.Length > 10000)
            {
                LogText = LogText.Substring(0, 10000);
            }
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            _cancellationTokenSource.Cancel();
            _tcpServer?.Stop();
            _udpBroadcaster?.Stop();
        }
    }
}
