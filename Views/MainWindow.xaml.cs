using System;
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
                    AddLog($"Starting LAN broadcast (IP: {localIp}, Port: {localPort})");
                };
                _udpBroadcaster.Start();
            };
            _tcpServer.ClientConnected += clientIp =>
            {
                AddLog($"Client {clientIp} has connected");
                _connectedClientsList.Add(clientIp);
            };
            _tcpServer.ClientDisconnected += ip =>
            {
                AddLog($"Client {ip} has disconnected");
                _connectedClientsList.Remove(ip);
            };
            _tcpServer.DataReceived += (clientIp, data) =>
            {
                AddLog($"Received data from {clientIp}: [{data}]");
                ProcessKeyData(data);
            };
            _tcpServer.HeartbeatTimeout += (ip) =>
            {
                AddLog($"Client {ip} heartbeat timeout");
            };

            // 启动UDP和TCP服务
            AddLog("Program started, waiting for connections...");
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
                    AddLog($"Invalid data format: {data}");
                    return;
                }

                string keyCode = parts[0].Trim();
                if (!ushort.TryParse(parts[1].Trim(), out ushort pressedFlag))
                {
                    AddLog($"Invalid key state: {parts[1]}");
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
                    AddLog($"Unknown key code: {keyCode}");
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
                AddLog($"Error processing key data: {ex.Message}");
            }
        }

        public void AddLog(string message)
        {
            string logEntry = $"[{DateTime.Now:HH:mm:ss}] {message}\n";
            LogText = logEntry + LogText;

            // 限制日志长度，防止内存溢出
            if (LogText.Length > 10000)
            {
                LogText = LogText.Substring(0, 10000);
            }
        }

        private void Hyperlink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = e.Uri.ToString(),
                UseShellExecute = true
            });
            e.Handled = true;
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            _cancellationTokenSource.Cancel();
            _tcpServer?.Stop();
            _udpBroadcaster?.Stop();
        }
    }
}
