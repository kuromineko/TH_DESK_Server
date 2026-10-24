using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

using WinForms = System.Windows.Forms;
using Drawing = System.Drawing;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;
using Clipboard = System.Windows.Clipboard;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using Brushes = System.Windows.Media.Brushes;

namespace TH_DESK_Server
{
    public class PeerSessionInfo
    {
        public string PeerId { get; set; } = "-";
        public string IpAddress { get; set; } = "-";
        public string Mode { get; set; } = "UDP Direct";
        public DateTime FirstSeen { get; set; } = DateTime.Now;
        public DateTime LastSeen { get; set; } = DateTime.Now;
        public long BytesTransferred { get; set; } = 0;

        public bool IsOnline => (DateTime.Now - LastSeen).TotalSeconds < 65;
        public string StatusText => IsOnline ? "🟢 Online" : "⚪ Offline";
        public string LastSeenText => $"{LastSeen:HH:mm:ss} ({(int)(DateTime.Now - LastSeen).TotalSeconds}s ago)";
    }

    public partial class MainWindow : Window
    {
        private Process? _hbbsProc;
        private Process? _hbbrProc;
        private TcpListener? _tcpBridgeListener;
        private TcpListener? _webConsoleListener;
        private CancellationTokenSource? _bridgeCts;
        private DispatcherTimer? _telemetryTimer;

        private bool _isRunning = false;
        private string _currentLang = "TH";
        private DateTime _serverStartTime;
        private int _activeTcpTunnels = 0;
        private long _totalBytesRouted = 0;
        private int _lastInternalHbbsPort = 14997;
        private int _lastRelayInPort = 4998;
        private string _lastDdns = "";
        private int _lastRelayOutPort = 4998;

        private readonly ConcurrentDictionary<string, PeerSessionInfo> _peers = new();
        private readonly ConcurrentQueue<string> _eventLogs = new();
        private readonly string _configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "th_desk_server.cfg");
        private WinForms.NotifyIcon? _trayIcon;
        private const string AutoStartTaskName = "TH_DESK_Server_AutoStart";

        public MainWindow()
        {
            InitializeComponent();
            InitSystemTray();
            LoadSavedConfig();
            CheckAutoStartStatus();
            ApplyLanguage(_currentLang);
            InitTelemetryTimer();

            AddServerLog("เริ่มต้นโปรแกรม TH_DESK Server Pro Console พร้อมใช้งาน");

            string[] args = Environment.GetCommandLineArgs();
            if (args.Any(a => a.Equals("--autostart", StringComparison.OrdinalIgnoreCase)))
            {
                this.Loaded += async (s, e) =>
                {
                    await StartServerCoreAsync();
                    MinimizeToSystemTray(showTip: false);
                };
            }
        }

        // ==================== 0. ระบบ Telemetry & Watchdog Timer ====================
        private void InitTelemetryTimer()
        {
            _telemetryTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _telemetryTimer.Tick += (s, e) =>
            {
                int onlineCount = _peers.Values.Count(p => p.IsOnline);
                txtStatOnlinePeers.Text = onlineCount.ToString();
                txtStatActiveTunnels.Text = Math.Max(0, _activeTcpTunnels).ToString();
                txtStatTraffic.Text = FormatBytes(Interlocked.Read(ref _totalBytesRouted));

                if (_isRunning)
                {
                    TimeSpan up = DateTime.Now - _serverStartTime;
                    txtStatUptime.Text = $"{(int)up.TotalHours:D2}:{up.Minutes:D2}:{up.Seconds:D2}";

                    if (chkAutoRestartWatchdog.IsChecked == true && DateTime.Now.Second % 4 == 0)
                    {
                        EnsureProcessesAlive();
                    }
                }
                else
                {
                    txtStatUptime.Text = "00:00:00";
                }

                if (pageMonitor.Visibility == Visibility.Visible)
                {
                    gridPeers.ItemsSource = _peers.Values
                        .OrderByDescending(p => p.IsOnline)
                        .ThenByDescending(p => p.LastSeen)
                        .ToList();
                }
            };
            _telemetryTimer.Start();
        }

        private void EnsureProcessesAlive()
        {
            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                if (_hbbrProc == null || _hbbrProc.HasExited)
                {
                    string hbbrPath = Path.Combine(baseDir, "hbbr.exe");
                    if (File.Exists(hbbrPath))
                    {
                        _hbbrProc = StartHiddenProcess(hbbrPath, $"-p {_lastRelayInPort}");
                        AddServerLog($"[Watchdog] ตรวจพบ hbbr.exe หยุดทำงาน -> สั่งรันใหม่อัตโนมัติที่พอร์ต {_lastRelayInPort}");
                    }
                }
                if (_hbbsProc == null || _hbbsProc.HasExited)
                {
                    string hbbsPath = Path.Combine(baseDir, "hbbs.exe");
                    if (File.Exists(hbbsPath))
                    {
                        _hbbsProc = StartHiddenProcess(hbbsPath, $"-p {_lastInternalHbbsPort} -r {_lastDdns}:{_lastRelayOutPort}");
                        AddServerLog($"[Watchdog] ตรวจพบ hbbs.exe หยุดทำงาน -> สั่งรันใหม่อัตโนมัติที่พอร์ต {_lastInternalHbbsPort}");
                    }
                }
            }
            catch { }
        }

        private void AddServerLog(string message)
        {
            string line = $"[{DateTime.Now:HH:mm:ss}] {message}";
            _eventLogs.Enqueue(line);
            while (_eventLogs.Count > 120) _eventLogs.TryDequeue(out _);

            Dispatcher.BeginInvoke(() =>
            {
                txtServerLogs.Text = string.Join("\r\n", _eventLogs.Reverse());
            });
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024.0):F2} MB";
            return $"{bytes / (1024.0 * 1024.0 * 1024.0):F2} GB";
        }

        // ==================== 1. ระบบพับเก็บลงมุมขวาล่าง (System Tray) ====================
        private void InitSystemTray()
        {
            _trayIcon = new WinForms.NotifyIcon
            {
                Text = "TH_DESK Server Pro (x64)",
                Icon = CreateAppTrayIcon(),
                Visible = true
            };

            _trayIcon.DoubleClick += (s, e) => RestoreFromTray();
            UpdateTrayMenuText();
        }

        private void UpdateTrayMenuText()
        {
            if (_trayIcon == null) return;
            bool isTH = (_currentLang == "TH");

            var menu = new WinForms.ContextMenuStrip();
            menu.Items.Add(isTH ? "🖥 เปิดหน้าต่าง TH_DESK Server" : "🖥 Open TH_DESK Server", null, (s, e) => RestoreFromTray());
            menu.Items.Add(isTH ? "🌐 เปิดหน้า Web Console บนเบราว์เซอร์" : "🌐 Open Web Console", null, (s, e) => BtnOpenWeb_Click(this, new RoutedEventArgs()));
            menu.Items.Add(new WinForms.ToolStripSeparator());
            menu.Items.Add(isTH ? "✕ ปิดระบบและออกจากโปรแกรม" : "✕ Stop Server & Exit", null, (s, e) => ExitApplication());
            _trayIcon.ContextMenuStrip = menu;
        }

        private Drawing.Icon CreateAppTrayIcon()
        {
            try
            {
                string? exePath = Process.GetCurrentProcess().MainModule?.FileName;
                if (!string.IsNullOrEmpty(exePath))
                {
                    var exeIcon = Drawing.Icon.ExtractAssociatedIcon(exePath);
                    if (exeIcon != null)
                    {
                        this.Icon = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                            exeIcon.Handle,
                            Int32Rect.Empty,
                            System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
                        return exeIcon;
                    }
                }
            }
            catch { }

            using var bmp = new Drawing.Bitmap(32, 32);
            using (var g = Drawing.Graphics.FromImage(bmp))
            {
                g.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using var bgBrush = new Drawing.SolidBrush(Drawing.Color.FromArgb(0, 113, 255));
                g.FillEllipse(bgBrush, 2, 2, 28, 28);
                using var font = new Drawing.Font("Segoe UI", 10, Drawing.FontStyle.Bold);
                g.DrawString("TH", font, Drawing.Brushes.White, new Drawing.PointF(4, 7));
            }
            return Drawing.Icon.FromHandle(bmp.GetHicon());
        }

        private void MinimizeToSystemTray(bool showTip = true)
        {
            this.Hide();
            this.ShowInTaskbar = false;

            if (showTip)
            {
                _trayIcon?.ShowBalloonTip(
                    2000,
                    "TH_DESK Server",
                    _currentLang == "TH"
                        ? "พับเก็บลง System Tray แล้ว (ดับเบิลคลิกไอคอนมุมขวาล่างเพื่อเปิดหน้าต่าง)"
                        : "Minimized to System Tray (Double-click tray icon to restore).",
                    WinForms.ToolTipIcon.Info);
            }
        }

        private void RestoreFromTray()
        {
            this.Show();
            this.ShowInTaskbar = true;
            this.WindowState = WindowState.Normal;
            this.Activate();
        }

        // ==================== 2. ระบบเปิด Server อัตโนมัติเมื่อเปิดเครื่อง ====================
        private void CheckAutoStartStatus()
        {
            try
            {
                var psi = new ProcessStartInfo("schtasks.exe", $"/Query /TN \"{AutoStartTaskName}\"")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                using var proc = Process.Start(psi);
                proc?.WaitForExit(1500);
                chkAutoStartBoot.IsChecked = (proc?.ExitCode == 0);
            }
            catch
            {
                chkAutoStartBoot.IsChecked = false;
            }
        }

        private void ChkAutoStartBoot_Click(object sender, RoutedEventArgs e)
        {
            bool enable = (chkAutoStartBoot.IsChecked == true);
            string exePath = Process.GetCurrentProcess().MainModule?.FileName
                             ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TH_DESK_Server.exe");

            if (enable)
            {
                SaveConfig();
                string args = $"/Create /TN \"{AutoStartTaskName}\" /TR \"\\\"{exePath}\\\" --autostart\" /SC ONLOGON /RL HIGHEST /F";
                StartHiddenProcess("schtasks.exe", args)?.WaitForExit();
                MessageBox.Show(
                    _currentLang == "TH"
                        ? "ตั้งค่าเปิดระบบ Server อัตโนมัติเมื่อเปิดเครื่อง Windows เรียบร้อยแล้ว!"
                        : "Auto-Start on Windows Boot enabled!",
                    "TH_DESK Server", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                StartHiddenProcess("schtasks.exe", $"/Delete /TN \"{AutoStartTaskName}\" /F")?.WaitForExit();
                MessageBox.Show(
                    _currentLang == "TH"
                        ? "ยกเลิกการเปิดอัตโนมัติเมื่อเปิดเครื่องแล้ว"
                        : "Auto-Start on Windows Boot disabled.",
                    "TH_DESK Server", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        // ==================== 3. ระบบส่งออกไฟล์ตั้งค่าให้เครื่องลูกข่าย (ไม่บังคับ Relay เพื่อให้วิ่ง P2P UDP ก่อน) ====================
        private string GenerateClientBatScript(string ddns, int idOut, int relayOut, string pubKey)
        {
            return
                "@echo off\r\n" +
                "chcp 65001 >nul\r\n" +
                "echo ======================================================\r\n" +
                "echo   กำลังติดตั้งค่าเซิร์ฟเวอร์ TH_DESK อัตโนมัติ (ไม่ต้องกรอก IP/Key)\r\n" +
                "echo ======================================================\r\n" +
                "taskkill /F /IM TH_DESK.exe >nul 2>&1\r\n" +
                "taskkill /F /IM rustdesk.exe >nul 2>&1\r\n" +
                "if not exist \"%APPDATA%\\TH_DESK\\config\" mkdir \"%APPDATA%\\TH_DESK\\config\"\r\n" +
                "if not exist \"%APPDATA%\\RustDesk\\config\" mkdir \"%APPDATA%\\RustDesk\\config\"\r\n" +
                "(\r\n" +
                $"echo rendezvous_server = '{ddns}:{idOut}'\r\n" +
                "echo nat_type = 1\r\n" +
                "echo serial = 0\r\n" +
                "echo.\r\n" +
                "echo [options]\r\n" +
                $"echo custom-rendezvous-server = '{ddns}:{idOut}'\r\n" +
                $"echo relay-server = '{ddns}:{relayOut}'\r\n" +
                $"echo key = '{pubKey}'\r\n" +
                "echo direct-server = 'Y'\r\n" +
                ") > \"%APPDATA%\\TH_DESK\\config\\TH_DESK2.toml\"\r\n" +
                "copy /Y \"%APPDATA%\\TH_DESK\\config\\TH_DESK2.toml\" \"%APPDATA%\\RustDesk\\config\\RustDesk2.toml\" >nul\r\n" +
                "echo.\r\n" +
                $"echo [สำเร็จ] ตั้งค่าเชื่อมต่อมายัง {ddns}:{idOut} เรียบร้อยแล้ว!\r\n" +
                "echo สามารถเปิดโปรแกรม TH_DESK ใช้งานได้ทันที\r\n" +
                "pause\r\n";
        }

        private void BtnExportClient_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidatePorts(out _, out int idOut, out _, out int relayOut, out _))
                return;

            string ddns = txtDdns.Text.Trim();
            string pubKey = txtPubKey.Text.Trim();

            if (string.IsNullOrEmpty(pubKey) || pubKey == "-" || pubKey == "Generating...")
            {
                MessageBox.Show(
                    _currentLang == "TH"
                        ? "กรุณากดปุ่ม 'เปิดระบบ Server' ให้ระบบสร้าง Public Key ก่อนส่งออกไฟล์ครับ"
                        : "Please start the server first to generate the Public Key.",
                    "TH_DESK Server", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string exportDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Client_Config_Export");
            Directory.CreateDirectory(exportDir);

            string tomlContent =
                $"rendezvous_server = '{ddns}:{idOut}'\r\n" +
                $"nat_type = 1\r\n" +
                $"serial = 0\r\n\r\n" +
                $"[options]\r\n" +
                $"custom-rendezvous-server = '{ddns}:{idOut}'\r\n" +
                $"relay-server = '{ddns}:{relayOut}'\r\n" +
                $"key = '{pubKey}'\r\n" +
                $"direct-server = 'Y'\r\n";

            File.WriteAllText(Path.Combine(exportDir, "TH_DESK2.toml"), tomlContent, new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(exportDir, "Setup_TH_DESK_Client.bat"), GenerateClientBatScript(ddns, idOut, relayOut, pubKey), new UTF8Encoding(false));

            Process.Start("explorer.exe", exportDir);
            AddServerLog("ส่งออกไฟล์ตั้งค่าลูกข่าย Setup_TH_DESK_Client.bat เรียบร้อยแล้ว");
        }

        // ==================== 4. ดึงไฟล์ hbbs.exe / hbbr.exe ที่ฝังใน EXE ====================
        private void ExtractEmbeddedServerFiles()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            ExtractSingleFile("hbbs.exe", Path.Combine(baseDir, "hbbs.exe"));
            ExtractSingleFile("hbbr.exe", Path.Combine(baseDir, "hbbr.exe"));
        }

        private void ExtractSingleFile(string fileName, string outputPath)
        {
            if (File.Exists(outputPath)) return;
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                string? resourceName = assembly.GetManifestResourceNames()
                    .FirstOrDefault(r => r.EndsWith(fileName, StringComparison.OrdinalIgnoreCase));

                if (resourceName != null)
                {
                    using Stream? stream = assembly.GetManifestResourceStream(resourceName);
                    if (stream != null)
                    {
                        using FileStream fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write);
                        stream.CopyTo(fs);
                        return;
                    }
                }

                var uri = new Uri($"pack://application:,,,/{fileName}", UriKind.Absolute);
                var resInfo = Application.GetResourceStream(uri);
                if (resInfo?.Stream != null)
                {
                    using FileStream fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write);
                    resInfo.Stream.CopyTo(fs);
                }
            }
            catch { }
        }

        // ==================== 5. ระบบสลับหน้าเมนู (Navigation) ====================
        private void SetActivePage(string page)
        {
            pageDashboard.Visibility = page == "DASH" ? Visibility.Visible : Visibility.Collapsed;
            pageMonitor.Visibility = page == "MON" ? Visibility.Visible : Visibility.Collapsed;
            pageSettings.Visibility = page == "SET" ? Visibility.Visible : Visibility.Collapsed;
            pageAbout.Visibility = page == "ABOUT" ? Visibility.Visible : Visibility.Collapsed;

            var activeBg = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0071FF"));
            var normalBg = Brushes.Transparent;
            var activeFg = Brushes.White;
            var normalFg = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#94A3B8"));

            navDashboard.Background = page == "DASH" ? activeBg : normalBg;
            navDashboard.Foreground = page == "DASH" ? activeFg : normalFg;

            navMonitor.Background = page == "MON" ? activeBg : normalBg;
            navMonitor.Foreground = page == "MON" ? activeFg : normalFg;

            navSettings.Background = page == "SET" ? activeBg : normalBg;
            navSettings.Foreground = page == "SET" ? activeFg : normalFg;

            navAbout.Background = page == "ABOUT" ? activeBg : normalBg;
            navAbout.Foreground = page == "ABOUT" ? activeFg : normalFg;
        }

        private void NavDashboard_Click(object sender, RoutedEventArgs e) => SetActivePage("DASH");
        private void NavMonitor_Click(object sender, RoutedEventArgs e) => SetActivePage("MON");
        private void NavSettings_Click(object sender, RoutedEventArgs e) => SetActivePage("SET");
        private void NavAbout_Click(object sender, RoutedEventArgs e) => SetActivePage("ABOUT");

        private void BtnClearPeers_Click(object sender, RoutedEventArgs e)
        {
            foreach (var kv in _peers.Where(x => !x.Value.IsOnline).ToList())
            {
                _peers.TryRemove(kv.Key, out _);
            }
            gridPeers.ItemsSource = _peers.Values.ToList();
        }

        // ==================== 6. ระบบสลับ 2 ภาษา (TH / EN) ====================
        private void BtnLangToggle_Click(object sender, RoutedEventArgs e)
        {
            ApplyLanguage(_currentLang == "TH" ? "EN" : "TH");
            SaveConfig();
        }

        private void BtnSetLangTH_Click(object sender, RoutedEventArgs e)
        {
            ApplyLanguage("TH");
            SaveConfig();
        }

        private void BtnSetLangEN_Click(object sender, RoutedEventArgs e)
        {
            ApplyLanguage("EN");
            SaveConfig();
        }

        private void ApplyLanguage(string lang)
        {
            _currentLang = lang;
            bool isTH = (lang == "TH");

            var activeBtn = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0071FF"));
            var inactiveBtn = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#262F45"));
            btnLangTH.Background = isTH ? activeBtn : inactiveBtn;
            btnLangEN.Background = !isTH ? activeBtn : inactiveBtn;
            btnLangToggle.Content = isTH ? "🌐 ภาษา: TH" : "🌐 Lang: EN";

            UpdateTrayMenuText();

            navDashboard.Content = isTH ? "🖥  หน้าหลัก Server" : "🖥  Dashboard";
            navMonitor.Content = isTH ? "📡  เครื่องที่เชื่อมต่อ (Live)" : "📡  Live Peers & Logs";
            navSettings.Content = isTH ? "⚙  ตั้งค่าระบบ" : "⚙  Settings";
            navAbout.Content = isTH ? "ℹ  เกี่ยวกับโปรแกรม" : "ℹ  About & Credits";

            lblStatPeersTitle.Text = isTH ? "🟢 เครื่องที่ออนไลน์" : "🟢 Online Peers";
            lblStatTunnelsTitle.Text = isTH ? "⚡ ท่อที่กำลังเชื่อม" : "⚡ Active Tunnels";
            lblStatTrafficTitle.Text = isTH ? "📊 ข้อมูลสะสม (Traffic)" : "📊 Server Traffic";
            lblStatUptimeTitle.Text = isTH ? "⏱ เวลาทำงาน (Uptime)" : "⏱ Server Uptime";

            lblDdnsTitle.Text = isTH ? "1. โดเมน DDNS หรือ Public IP ของคุณ" : "1. Your DDNS Domain or Public IP";
            lblDdnsSub.Text = isTH ? "ที่อยู่สำหรับให้เครื่องปลายทางเชื่อมต่อและเปิดหน้า Web Console" : "External host address for client connections & Web Console";

            if (!_isRunning)
            {
                btnToggle.Content = isTH ? "▶  เปิดระบบ Server + Web Console + เปิด Firewall" : "▶  Start Server + Web Console & Firewall";
            }
            else
            {
                btnToggle.Content = isTH ? "■  หยุดการทำงาน Server (Stop)" : "■  Stop Server Processes";
            }

            btnCheckPort.Content = isTH ? "⚡ เช็คพอร์ต" : "⚡ Test Ports";
            btnCopy.Content = isTH ? "📋 คัดลอกค่า" : "📋 Copy Text";
            btnExportClient.Content = isTH ? "📥 ส่งออกไฟล์ลูกข่าย" : "📥 Export Client File";
        }

        // ==================== 7. ระบบควบคุม Server, UDP/TCP Proxy & Web Dashboard ====================
        private async void BtnToggle_Click(object sender, RoutedEventArgs e)
        {
            bool isTH = (_currentLang == "TH");

            if (_isRunning)
            {
                StopServerProcesses();
                _isRunning = false;
                btnToggle.Content = isTH ? "▶  เปิดระบบ Server + Web Console + เปิด Firewall" : "▶  Start Server + Web Console & Firewall";
                btnToggle.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981"));
                dotStatus.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#64748B"));
                txtHeaderStatus.Text = "Offline";
                txtHeaderStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#94A3B8"));
                lblStatus.Text = isTH ? "หยุดการทำงานของระบบ Server เรียบร้อยแล้ว" : "Server processes stopped.";
                lblStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#94A3B8"));
                AddServerLog("หยุดการทำงานระบบ Server และปิดพอร์ตเชื่อมต่อแล้ว");
                return;
            }

            await StartServerCoreAsync();
        }

        private async Task StartServerCoreAsync()
        {
            bool isTH = (_currentLang == "TH");

            if (!ValidatePorts(out int idIn, out int idOut, out int relayIn, out int relayOut, out int webPort))
                return;

            if (chkSaveConfig.IsChecked == true)
                SaveConfig();

            ExtractEmbeddedServerFiles();

            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string hbbsPath = Path.Combine(baseDir, "hbbs.exe");
            string hbbrPath = Path.Combine(baseDir, "hbbr.exe");

            if (!File.Exists(hbbsPath) || !File.Exists(hbbrPath))
            {
                MessageBox.Show(
                    isTH ? "ไม่พบไฟล์ hbbs.exe หรือ hbbr.exe!" : "Could not find hbbs.exe or hbbr.exe!",
                    "TH_DESK Server", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string ddns = txtDdns.Text.Trim();
            bool openFw = (chkAutoFirewall.IsChecked == true);
            bool enableWeb = (chkEnableWebConsole.IsChecked == true);

            int internalHbbsPort = (idIn < 50000) ? (idIn + 10000) : (idIn - 10000);
            _lastInternalHbbsPort = internalHbbsPort;
            _lastRelayInPort = relayIn;
            _lastDdns = ddns;
            _lastRelayOutPort = relayOut;

            lblStatus.Text = isTH ? "กำลังเริ่มระบบ Server + Live Monitor + Web Console..." : "Starting Server + Live Monitor + Web Console...";
            lblStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#38BDF8"));

            await Task.Run(() =>
            {
                StopServerProcesses();

                if (openFw)
                {
                    AllowProgramInFirewall("TH_DESK_SERVER_MANAGER", Process.GetCurrentProcess().MainModule?.FileName ?? "");
                    AllowProgramInFirewall("TH_DESK_HBBS_APP", hbbsPath);
                    AllowProgramInFirewall("TH_DESK_HBBR_APP", hbbrPath);

                    for (int p = idIn - 2; p <= idIn + 3; p++)
                    {
                        if (p > 0)
                        {
                            OpenFirewall($"TH_DESK_ID_TCP_{p}", p, "TCP");
                            OpenFirewall($"TH_DESK_ID_UDP_{p}", p, "UDP");
                        }
                    }
                    OpenFirewall($"TH_DESK_RELAY_TCP_{relayIn}", relayIn, "TCP");
                    if (enableWeb) OpenFirewall($"TH_DESK_WEB_{webPort}", webPort, "TCP");
                }

                _hbbrProc = StartHiddenProcess(hbbrPath, $"-p {relayIn}");
                _hbbsProc = StartHiddenProcess(hbbsPath, $"-p {internalHbbsPort} -r {ddns}:{relayOut}");

                StartTcpBridge(idIn, internalHbbsPort);
            });

            string pubKeyFile = Path.Combine(baseDir, "id_ed25519.pub");
            string pubKey = "";
            for (int i = 0; i < 10; i++)
            {
                if (File.Exists(pubKeyFile))
                {
                    pubKey = File.ReadAllText(pubKeyFile).Trim();
                    break;
                }
                await Task.Delay(350);
            }

            txtPubKey.Text = string.IsNullOrEmpty(pubKey) ? "Generating..." : pubKey;
            txtClientConfig.Text =
                $"ID Server   : {ddns}:{idOut}  (ใน LAN ใช้ :{idIn})\r\n" +
                $"Relay Server: {ddns}:{relayOut}  (ใน LAN ใช้ :{relayIn})\r\n" +
                $"Public Key  : {pubKey}";

            if (enableWeb)
            {
                StartWebConsoleServer(webPort, ddns, idIn, idOut, relayIn, relayOut, pubKey);
                btnOpenWebTop.Content = $"🌐 Web Console :{webPort}";
                lblWebUrlSidebar.Text = $"http://localhost:{webPort}";
            }

            _serverStartTime = DateTime.Now;
            _isRunning = true;
            btnToggle.Content = isTH ? "■  หยุดการทำงาน Server (Stop)" : "■  Stop Server Processes";
            btnToggle.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444"));
            dotStatus.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981"));
            txtHeaderStatus.Text = "Online";
            txtHeaderStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981"));
            lblStatus.Text = isTH
                ? $"ออนไลน์ (ID :{idIn} [UDP+TCP] | Relay :{relayIn} | Web Console :http://localhost:{webPort})"
                : $"Online (ID :{idIn} [UDP+TCP] | Relay :{relayIn} | Web Console :http://localhost:{webPort})";
            lblStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981"));

            AddServerLog($"เปิดระบบสำเร็จ! ID Server :{idIn} (UDP+TCP), Relay :{relayIn}, Web Console :{webPort}");
        }

        // ==================== 8. ระบบรับส่ง UDP Direct + TCP Bridge พร้อมดักจับรายชื่อเครื่องลูกข่ายอัตโนมัติ ====================
        private void StartTcpBridge(int publicPort, int internalHbbsPort)
        {
            _bridgeCts = new CancellationTokenSource();
            var token = _bridgeCts.Token;

            _tcpBridgeListener = new TcpListener(IPAddress.Any, publicPort);
            _tcpBridgeListener.Start();

            // รับสาย TCP (รองรับทั้งโหมดมาตรฐานและโหมด 0xFE Bridge)
            Task.Run(async () =>
            {
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        var client = await _tcpBridgeListener.AcceptTcpClientAsync(token);
                        _ = HandleBridgeClientAsync(client, internalHbbsPort, token);
                    }
                    catch { break; }
                }
            }, token);

            // รับสาย UDP มาตรฐาน (ดักจับ Peer ID ขึ้นตาราง Live Monitor + ส่งต่อเข้า hbbs)
            Task.Run(async () =>
            {
                using var udpListener = new UdpClient(publicPort);
                var clientMap = new ConcurrentDictionary<IPEndPoint, UdpClient>();
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        var received = await udpListener.ReceiveAsync(token);
                        var senderEp = received.RemoteEndPoint;
                        Interlocked.Add(ref _totalBytesRouted, received.Buffer.Length);
                        InspectPacketForPeerId(received.Buffer, senderEp.Address.ToString(), "UDP Direct (Standard)");

                        var relayUdp = clientMap.GetOrAdd(senderEp, ep =>
                        {
                            var u = new UdpClient();
                            u.Connect("127.0.0.1", internalHbbsPort);
                            Task.Run(async () =>
                            {
                                try
                                {
                                    while (!token.IsCancellationRequested)
                                    {
                                        var resp = await u.ReceiveAsync(token);
                                        Interlocked.Add(ref _totalBytesRouted, resp.Buffer.Length);
                                        await udpListener.SendAsync(resp.Buffer, resp.Buffer.Length, ep);
                                    }
                                }
                                catch { }
                            }, token);
                            return u;
                        });

                        await relayUdp.SendAsync(received.Buffer, received.Buffer.Length);
                    }
                    catch { if (token.IsCancellationRequested) break; }
                }
            }, token);
        }

        private async Task HandleBridgeClientAsync(TcpClient client, int internalHbbsPort, CancellationToken token)
        {
            Interlocked.Increment(ref _activeTcpTunnels);
            string remoteIp = (client.Client.RemoteEndPoint as IPEndPoint)?.Address.ToString() ?? "Unknown";

            try
            {
                using (client)
                {
                    client.NoDelay = true;
                    using var stream = client.GetStream();

                    byte[] firstByte = new byte[1];
                    int r = await stream.ReadAsync(firstByte, 0, 1, token);
                    if (r == 0) return;

                    if (firstByte[0] == 0xFE)
                    {
                        using var udp = new UdpClient();
                        udp.Connect("127.0.0.1", internalHbbsPort);

                        var udpToTcp = Task.Run(async () =>
                        {
                            try
                            {
                                while (!token.IsCancellationRequested)
                                {
                                    var res = await udp.ReceiveAsync(token);
                                    Interlocked.Add(ref _totalBytesRouted, res.Buffer.Length);
                                    byte[] lenBuf = BitConverter.GetBytes((ushort)res.Buffer.Length);
                                    await stream.WriteAsync(lenBuf, 0, 2, token);
                                    await stream.WriteAsync(res.Buffer, 0, res.Buffer.Length, token);
                                    await stream.FlushAsync(token);
                                }
                            }
                            catch { }
                        }, token);

                        byte[] lenBytes = new byte[2];
                        while (!token.IsCancellationRequested)
                        {
                            if (!await ReadExactAsync(stream, lenBytes, 2, token)) break;
                            int packetLen = BitConverter.ToUInt16(lenBytes, 0);
                            if (packetLen <= 0 || packetLen > 65500) break;

                            byte[] payload = new byte[packetLen];
                            if (!await ReadExactAsync(stream, payload, packetLen, token)) break;

                            Interlocked.Add(ref _totalBytesRouted, packetLen);
                            InspectPacketForPeerId(payload, remoteIp, "TCP Bridge (0xFE)");

                            await udp.SendAsync(payload, packetLen);
                        }
                    }
                    else
                    {
                        using var hbbsTcp = new TcpClient();
                        hbbsTcp.NoDelay = true;
                        await hbbsTcp.ConnectAsync("127.0.0.1", internalHbbsPort, token);
                        using var hbbsStream = hbbsTcp.GetStream();

                        await hbbsStream.WriteAsync(firstByte, 0, 1, token);

                        var t1 = stream.CopyToAsync(hbbsStream, token);
                        var t2 = hbbsStream.CopyToAsync(stream, token);
                        await Task.WhenAny(t1, t2);
                    }
                }
            }
            catch { }
            finally
            {
                Interlocked.Decrement(ref _activeTcpTunnels);
            }
        }

        private void InspectPacketForPeerId(byte[] payload, string ip, string mode)
        {
            try
            {
                string ascii = Encoding.ASCII.GetString(payload);
                var match = Regex.Match(ascii, @"\b([1-9][0-9]{7,10})\b");
                if (match.Success)
                {
                    string peerId = match.Groups[1].Value;
                    bool isNew = !_peers.ContainsKey(peerId);

                    _peers.AddOrUpdate(peerId,
                        id => new PeerSessionInfo
                        {
                            PeerId = id,
                            IpAddress = ip,
                            Mode = mode,
                            FirstSeen = DateTime.Now,
                            LastSeen = DateTime.Now,
                            BytesTransferred = payload.Length
                        },
                        (id, existing) =>
                        {
                            existing.IpAddress = ip;
                            existing.Mode = mode;
                            existing.LastSeen = DateTime.Now;
                            existing.BytesTransferred += payload.Length;
                            return existing;
                        });

                    if (isNew)
                    {
                        AddServerLog($"เครื่องลูกข่าย ID: {peerId} เชื่อมต่อออนไลน์เข้ามาจาก IP: {ip} ({mode})");
                    }
                }
            }
            catch { }
        }

        private async Task<bool> ReadExactAsync(NetworkStream stream, byte[] buffer, int count, CancellationToken token)
        {
            int offset = 0;
            while (offset < count)
            {
                int read = await stream.ReadAsync(buffer, offset, count - offset, token);
                if (read == 0) return false;
                offset += read;
            }
            return true;
        }

        // ==================== 9. BUILT-IN WEB CONSOLE SERVER ====================
        private void StartWebConsoleServer(int webPort, string ddns, int idIn, int idOut, int relayIn, int relayOut, string pubKey)
        {
            if (_bridgeCts == null) return;
            var token = _bridgeCts.Token;

            try
            {
                _webConsoleListener = new TcpListener(IPAddress.Any, webPort);
                _webConsoleListener.Start();

                Task.Run(async () =>
                {
                    while (!token.IsCancellationRequested)
                    {
                        try
                        {
                            var client = await _webConsoleListener.AcceptTcpClientAsync(token);
                            _ = HandleWebHttpRequestAsync(client, ddns, idIn, idOut, relayIn, relayOut, pubKey, token);
                        }
                        catch { break; }
                    }
                }, token);
            }
            catch (Exception ex)
            {
                AddServerLog($"[คำเตือน] ไม่สามารถเปิดพอร์ต Web Console :{webPort} ได้ ({ex.Message})");
            }
        }

        private async Task HandleWebHttpRequestAsync(TcpClient client, string ddns, int idIn, int idOut, int relayIn, int relayOut, string pubKey, CancellationToken token)
        {
            using (client)
            {
                try
                {
                    using var stream = client.GetStream();
                    byte[] buf = new byte[4096];
                    int read = await stream.ReadAsync(buf, 0, buf.Length, token);
                    if (read <= 0) return;

                    string req = Encoding.UTF8.GetString(buf, 0, read);
                    string firstLine = req.Split('\r', '\n')[0];
                    string path = "/";
                    var parts = firstLine.Split(' ');
                    if (parts.Length >= 2) path = parts[1];

                    if (path.StartsWith("/api/status"))
                    {
                        TimeSpan up = _isRunning ? (DateTime.Now - _serverStartTime) : TimeSpan.Zero;
                        var peerList = _peers.Values
                            .OrderByDescending(p => p.IsOnline)
                            .ThenByDescending(p => p.LastSeen)
                            .Select(p => new
                            {
                                id = p.PeerId,
                                ip = p.IpAddress,
                                mode = p.Mode,
                                online = p.IsOnline,
                                lastSeen = p.LastSeen.ToString("HH:mm:ss"),
                                ago = $"{(int)(DateTime.Now - p.LastSeen).TotalSeconds}s ago",
                                traffic = FormatBytes(p.BytesTransferred)
                            }).ToList();

                        var payload = new
                        {
                            online = _isRunning,
                            ddns = $"{ddns}:{idOut}",
                            relay = $"{ddns}:{relayOut}",
                            pubKey = pubKey,
                            uptime = $"{(int)up.TotalHours:D2}:{up.Minutes:D2}:{up.Seconds:D2}",
                            onlinePeers = _peers.Values.Count(p => p.IsOnline),
                            totalPeers = _peers.Count,
                            activeTunnels = Math.Max(0, _activeTcpTunnels),
                            totalTraffic = FormatBytes(Interlocked.Read(ref _totalBytesRouted)),
                            peers = peerList,
                            logs = _eventLogs.Reverse().Take(30).ToList()
                        };

                        string json = JsonSerializer.Serialize(payload);
                        await SendHttpResponseAsync(stream, "200 OK", "application/json; charset=utf-8", Encoding.UTF8.GetBytes(json));
                    }
                    else if (path.StartsWith("/download-bat"))
                    {
                        string bat = GenerateClientBatScript(ddns, idOut, relayOut, pubKey);
                        byte[] data = Encoding.UTF8.GetBytes(bat);
                        string header =
                            "HTTP/1.1 200 OK\r\n" +
                            "Content-Type: application/octet-stream\r\n" +
                            "Content-Disposition: attachment; filename=\"Setup_TH_DESK_Client.bat\"\r\n" +
                            $"Content-Length: {data.Length}\r\n" +
                            "Connection: close\r\n\r\n";
                        byte[] hBytes = Encoding.UTF8.GetBytes(header);
                        await stream.WriteAsync(hBytes, 0, hBytes.Length, token);
                        await stream.WriteAsync(data, 0, data.Length, token);
                    }
                    else
                    {
                        string html = GetWebDashboardHtml();
                        await SendHttpResponseAsync(stream, "200 OK", "text/html; charset=utf-8", Encoding.UTF8.GetBytes(html));
                    }
                }
                catch { }
            }
        }

        private async Task SendHttpResponseAsync(NetworkStream stream, string status, string contentType, byte[] body)
        {
            string header =
                $"HTTP/1.1 {status}\r\n" +
                $"Content-Type: {contentType}\r\n" +
                $"Content-Length: {body.Length}\r\n" +
                "Cache-Control: no-cache\r\n" +
                "Connection: close\r\n\r\n";
            byte[] headerBytes = Encoding.UTF8.GetBytes(header);
            await stream.WriteAsync(headerBytes, 0, headerBytes.Length);
            await stream.WriteAsync(body, 0, body.Length);
            await stream.FlushAsync();
        }

        private string GetWebDashboardHtml()
        {
            return @"<!DOCTYPE html>
<html lang='th'>
<head>
<meta charset='utf-8'>
<meta name='viewport' content='width=device-width, initial-scale=1'>
<title>TH_DESK Server Pro — Web Console</title>
<style>
  :root { --bg:#0B0E14; --card:#141926; --border:#222B3E; --blue:#0071FF; --cyan:#38BDF8; --green:#10B981; --text:#F8FAFC; --muted:#94A3B8; }
  * { box-sizing:border-box; font-family:'Segoe UI',system-ui,sans-serif; }
  body { margin:0; background:var(--bg); color:var(--text); padding:24px; }
  .container { max-width:1100px; margin:0 auto; }
  .header { display:flex; justify-content:space-between; align-items:center; background:var(--card); border:1px solid var(--border); padding:16px 22px; border-radius:14px; margin-bottom:20px; flex-wrap:wrap; gap:12px; }
  .brand { display:flex; align-items:center; gap:12px; }
  .logo { width:38px; height:38px; border-radius:10px; background:var(--blue); display:flex; align-items:center; justify-content:center; font-size:20px; font-weight:bold; }
  .badge { background:#102A25; color:var(--green); border:1px solid var(--green); padding:6px 14px; border-radius:20px; font-size:13px; font-weight:bold; }
  .grid-4 { display:grid; grid-template-columns:repeat(auto-fit,minmax(220px,1fr)); gap:14px; margin-bottom:20px; }
  .card { background:var(--card); border:1px solid var(--border); border-radius:12px; padding:18px; }
  .stat-label { color:var(--muted); font-size:13px; }
  .stat-val { font-size:28px; font-weight:bold; margin-top:6px; }
  .btn { background:var(--blue); color:#fff; border:none; padding:10px 18px; border-radius:8px; font-weight:600; cursor:pointer; text-decoration:none; display:inline-block; font-size:13.5px; }
  .btn:hover { opacity:0.9; }
  table { width:100%; border-collapse:collapse; margin-top:12px; }
  th, td { text-align:left; padding:12px; border-bottom:1px solid var(--border); font-size:13.5px; }
  th { color:var(--cyan); background:#10141F; }
  .pill-on { color:var(--green); font-weight:bold; }
  .pill-off { color:var(--muted); }
  .log-box { background:#080A0F; border:1px solid var(--border); border-radius:10px; padding:14px; font-family:Consolas,monospace; font-size:12px; color:var(--muted); height:200px; overflow-y:auto; white-space:pre-wrap; }
  .cfg-box { background:#0C0F17; border:1px solid var(--border); padding:12px; border-radius:8px; font-family:Consolas,monospace; font-size:13px; color:var(--cyan); word-break:break-all; margin:10px 0; }
</style>
</head>
<body>
<div class='container'>
  <div class='header'>
    <div class='brand'>
      <div class='logo'>⚡</div>
      <div>
        <div style='font-size:18px;font-weight:bold;'>TH_DESK SERVER <span style='color:var(--cyan);font-size:13px;'>WEB CONSOLE</span></div>
        <div style='color:var(--muted);font-size:12px;'>ระบบตรวจสอบสถานะเครื่องลูกข่ายและดาวน์โหลดไฟล์ตั้งค่าอัตโนมัติ (Real-time Dashboard)</div>
      </div>
    </div>
    <div style='display:flex;gap:10px;align-items:center;'>
      <a class='btn' href='/download-bat'>📥 ดาวน์โหลดตัวตั้งค่าลูกข่าย (.BAT)</a>
      <span id='statusBadge' class='badge'>● Online</span>
    </div>
  </div>

  <div class='grid-4'>
    <div class='card'><div class='stat-label'>🟢 เครื่องที่ออนไลน์ (Online Peers)</div><div id='stOnline' class='stat-val' style='color:var(--green)'>0</div></div>
    <div class='card'><div class='stat-label'>⚡ ท่อที่กำลังเชื่อมอยู่</div><div id='stTunnels' class='stat-val' style='color:var(--cyan)'>0</div></div>
    <div class='card'><div class='stat-label'>📊 ปริมาณข้อมูลสะสม (Traffic)</div><div id='stTraffic' class='stat-val' style='color:#A78BFA'>0 KB</div></div>
    <div class='card'><div class='stat-label'>⏱ เวลาเปิดเซิร์ฟเวอร์ (Uptime)</div><div id='stUptime' class='stat-val'>00:00:00</div></div>
  </div>

  <div class='card' style='margin-bottom:20px;'>
    <div style='font-weight:bold;font-size:15px;color:var(--cyan);'>🔑 ข้อมูลสำหรับเชื่อมต่อเซิร์ฟเวอร์ (Server Endpoint & Public Key)</div>
    <div id='cfgInfo' class='cfg-box'>Loading...</div>
  </div>

  <div class='card' style='margin-bottom:20px;'>
    <div style='font-weight:bold;font-size:15px;'>📡 รายการเครื่องลูกข่ายที่เชื่อมต่อเข้ามา (Connected Client Peers)</div>
    <table>
      <thead><tr><th>สถานะ</th><th>หมายเลข ID (Peer ID)</th><th>IP Address</th><th>โหมดการเชื่อมต่อ</th><th>รับส่งข้อมูล</th><th>อัปเดตล่าสุด</th></tr></thead>
      <tbody id='peerTable'><tr><td colspan='6' style='color:var(--muted);text-align:center;'>ยังไม่มีเครื่องเชื่อมต่อเข้ามา...</td></tr></tbody>
    </table>
  </div>

  <div class='card'>
    <div style='font-weight:bold;font-size:15px;margin-bottom:10px;color:var(--cyan);'>📋 บันทึกเหตุการณ์เซิร์ฟเวอร์ล่าสุด (Live Event Logs)</div>
    <div id='logBox' class='log-box'>Loading...</div>
  </div>
</div>

<script>
async function refreshData() {
  try {
    const r = await fetch('/api/status');
    const d = await r.json();
    document.getElementById('stOnline').innerText = d.onlinePeers + ' / ' + d.totalPeers;
    document.getElementById('stTunnels').innerText = d.activeTunnels;
    document.getElementById('stTraffic').innerText = d.totalTraffic;
    document.getElementById('stUptime').innerText = d.uptime;
    document.getElementById('cfgInfo').innerText = 'ID Server: ' + d.ddns + '   |   Relay Server: ' + d.relay + '\nPublic Key: ' + d.pubKey;

    const tb = document.getElementById('peerTable');
    if (!d.peers || d.peers.length === 0) {
      tb.innerHTML = '<tr><td colspan=""6"" style=""color:#94A3B8;text-align:center;"">กำลังรอเครื่องลูกข่ายเชื่อมต่อเข้ามา...</td></tr>';
    } else {
      tb.innerHTML = d.peers.map(p => `<tr>
        <td class='${p.online ? ""pill-on"" : ""pill-off""}'>${p.online ? ""🟢 Online"" : ""⚪ Offline""}</td>
        <td style='font-weight:bold;color:#38BDF8;font-size:15px;'>${p.id}</td>
        <td>${p.ip}</td>
        <td>${p.mode}</td>
        <td>${p.traffic}</td>
        <td>${p.lastSeen} (${p.ago})</td>
      </tr>`).join('');
    }
    document.getElementById('logBox').innerText = (d.logs || []).join('\n');
  } catch (e) {}
}
refreshData();
setInterval(refreshData, 1500);
</script>
</body>
</html>";
        }

        private void BtnOpenWeb_Click(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse(txtWebPort.Text.Trim(), out int webPort)) webPort = 8080;
            if (!_isRunning)
            {
                MessageBox.Show(
                    _currentLang == "TH"
                        ? "กรุณากดปุ่ม '▶ เปิดระบบ Server' ก่อนเปิดหน้า Web Console ครับ"
                        : "Please start the server first before opening the Web Console.",
                    "TH_DESK Server", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            OpenUrl($"http://localhost:{webPort}");
        }

        private async void BtnCheckPort_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidatePorts(out int idIn, out int idOut, out int relayIn, out int relayOut, out int webPort))
                return;

            bool isTH = (_currentLang == "TH");
            string ddns = txtDdns.Text.Trim();
            lblStatus.Text = isTH
                ? $"กำลังตรวจสอบพอร์ต ID ({idOut}) และ Relay ({relayOut}) บน {ddns}..."
                : $"Checking ID ({idOut}) and Relay ({relayOut}) on {ddns}...";
            lblStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B"));

            bool localIdOk = await TestTcpPortAsync("127.0.0.1", idIn, 1200);
            bool localRelayOk = await TestTcpPortAsync("127.0.0.1", relayIn, 1200);

            bool extIdTcpOk = await TestTcpPortAsync(ddns, idOut, 2500);
            bool extRelayTcpOk = await TestTcpPortAsync(ddns, relayOut, 2500);

            if (!localIdOk || !localRelayOk)
            {
                lblStatus.Text = isTH
                    ? $"พอร์ตในเครื่อง ({idIn}/{relayIn}) ยังไม่ทำงาน! กรุณากดปุ่ม 'เปิดระบบ Server' ก่อนครับ"
                    : $"Local ports ({idIn}/{relayIn}) are not listening. Please start the server first.";
                lblStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444"));
                return;
            }

            if (extIdTcpOk && extRelayTcpOk)
            {
                lblStatus.Text = isTH
                    ? $"ผ่านทั้งหมด! พอร์ตทั้ง ID ({idOut}) และ Relay ({relayOut}) ทะลุเร้าเตอร์พร้อมใช้งาน 100%"
                    : $"Success! Both ID ({idOut}) and Relay ({relayOut}) ports are OPEN.";
                lblStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981"));
                AddServerLog($"ทดสอบพอร์ตภายนอก {ddns}:{idOut} และ :{relayOut} -> ผ่าน 100%");
            }
            else
            {
                string failMsg = isTH
                    ? $"พอร์ตในเครื่องทำงานแล้ว แต่พอร์ตนอกยังติด: ID({idOut})={(extIdTcpOk ? "ผ่าน" : "ไม่ผ่าน")}, Relay({relayOut})={(extRelayTcpOk ? "ผ่าน" : "ไม่ผ่าน")}"
                    : $"Local OK, External status: ID({idOut})={(extIdTcpOk ? "OPEN" : "CLOSED")}, Relay({relayOut})={(extRelayTcpOk ? "OPEN" : "CLOSED")}";
                lblStatus.Text = failMsg;
                lblStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B"));
                AddServerLog(failMsg);
            }
        }

        private async Task<bool> TestTcpPortAsync(string host, int port, int timeoutMs)
        {
            return await Task.Run(() =>
            {
                try
                {
                    using var client = new TcpClient();
                    var res = client.BeginConnect(host, port, null, null);
                    if (!res.AsyncWaitHandle.WaitOne(TimeSpan.FromMilliseconds(timeoutMs))) return false;
                    client.EndConnect(res);
                    return true;
                }
                catch { return false; }
            });
        }

        // ==================== 10. ปุ่มในหน้า Settings และ About ====================
        private void BtnOpenFolder_Click(object sender, RoutedEventArgs e)
        {
            Process.Start("explorer.exe", AppDomain.CurrentDomain.BaseDirectory);
        }

        private void BtnResetKey_Click(object sender, RoutedEventArgs e)
        {
            bool isTH = (_currentLang == "TH");
            var confirm = MessageBox.Show(
                isTH ? "ต้องการลบไฟล์ Key เดิมเพื่อสร้างรหัส Public Key ใหม่ใช่หรือไม่?\n(เครื่องลูกข่ายจะต้องอัปเดต Key ใหม่ด้วย)"
                     : "Are you sure you want to delete and regenerate the Server KeyPair?",
                "TH_DESK", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (confirm == MessageBoxResult.Yes)
            {
                StopServerProcesses();
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                try { File.Delete(Path.Combine(baseDir, "id_ed25519")); } catch { }
                try { File.Delete(Path.Combine(baseDir, "id_ed25519.pub")); } catch { }
                txtPubKey.Text = "-";
                txtClientConfig.Text = "";
                AddServerLog("ลบไฟล์ Key เดิมแล้ว พร้อมสร้างใหม่เมื่อกดเปิด Server");
            }
        }

        private void BtnOpenMyGithub_Click(object sender, RoutedEventArgs e) => OpenUrl("https://github.com/kuromineko/TH_DESK");
        private void BtnOpenRustDesk_Click(object sender, RoutedEventArgs e) => OpenUrl("https://github.com/rustdesk/rustdesk");
        private void BtnOpenLicense_Click(object sender, RoutedEventArgs e) => OpenUrl("https://www.gnu.org/licenses/agpl-3.0.html");

        private void OpenUrl(string url)
        {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
        }

        // ==================== 11. บันทึกและโหลดค่า Config ====================
        private void SaveConfig()
        {
            try
            {
                string minTray = (chkMinimizeToTray.IsChecked == true) ? "1" : "0";
                string data = $"{_currentLang}|{txtDdns.Text}|{txtIdIn.Text}|{txtIdOut.Text}|{txtRelayIn.Text}|{txtRelayOut.Text}|{minTray}|{txtWebPort.Text}";
                File.WriteAllText(_configPath, data);
            }
            catch { }
        }

        private void LoadSavedConfig()
        {
            try
            {
                if (File.Exists(_configPath))
                {
                    string[] parts = File.ReadAllText(_configPath).Split('|');
                    if (parts.Length >= 6)
                    {
                        _currentLang = parts[0];
                        txtDdns.Text = parts[1];
                        txtIdIn.Text = parts[2];
                        txtIdOut.Text = parts[3];
                        txtRelayIn.Text = parts[4];
                        txtRelayOut.Text = parts[5];
                    }
                    if (parts.Length >= 7) chkMinimizeToTray.IsChecked = (parts[6] == "1");
                    if (parts.Length >= 8) txtWebPort.Text = parts[7];
                }
            }
            catch { }
        }

        // ==================== 12. Helper Functions & Window Events ====================
        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left) this.DragMove();
        }

        private void BtnMinimize_Click(object sender, RoutedEventArgs e)
        {
            if (chkMinimizeToTray.IsChecked == true)
                MinimizeToSystemTray(showTip: true);
            else
                this.WindowState = WindowState.Minimized;
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            ExitApplication();
        }

        private void ExitApplication()
        {
            if (_trayIcon != null)
            {
                _trayIcon.Visible = false;
                _trayIcon.Dispose();
            }
            if (chkKillOnExit.IsChecked == true)
                StopServerProcesses();

            SaveConfig();
            Application.Current.Shutdown();
        }

        private void BtnCopy_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(txtClientConfig.Text))
            {
                Clipboard.SetText(txtClientConfig.Text);
                lblStatus.Text = _currentLang == "TH" ? "คัดลอกข้อมูลการตั้งค่าลง Clipboard เรียบร้อยแล้ว!" : "Configuration copied to Clipboard!";
                lblStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#38BDF8"));
            }
        }

        private bool ValidatePorts(out int idIn, out int idOut, out int relayIn, out int relayOut, out int webPort)
        {
            idIn = idOut = relayIn = relayOut = 0;
            webPort = 8080;
            bool valid = int.TryParse(txtIdIn.Text, out idIn) && idIn >= 1 && idIn <= 65535 &&
                         int.TryParse(txtIdOut.Text, out idOut) && idOut >= 1 && idOut <= 65535 &&
                         int.TryParse(txtRelayIn.Text, out relayIn) && relayIn >= 1 && relayIn <= 65535 &&
                         int.TryParse(txtRelayOut.Text, out relayOut) && relayOut >= 1 && relayOut <= 65535 &&
                         int.TryParse(txtWebPort.Text, out webPort) && webPort >= 1 && webPort <= 65535;

            if (!valid)
                MessageBox.Show(_currentLang == "TH" ? "หมายเลขพอร์ตต้องอยู่ระหว่าง 1 ถึง 65535 เท่านั้น" : "Ports must be between 1 and 65535.", "TH_DESK", MessageBoxButton.OK, MessageBoxImage.Warning);

            return valid;
        }

        private void AllowProgramInFirewall(string ruleName, string exePath)
        {
            if (string.IsNullOrEmpty(exePath)) return;
            string cmd = $"/C netsh advfirewall firewall delete rule name=\"{ruleName}\" >nul 2>&1 & " +
                         $"netsh advfirewall firewall add rule name=\"{ruleName}\" dir=in action=allow program=\"{exePath}\" enable=yes >nul 2>&1";
            StartHiddenProcess("cmd.exe", cmd)?.WaitForExit();
        }

        private void OpenFirewall(string ruleName, int port, string proto)
        {
            string cmd = $"/C netsh advfirewall firewall delete rule name=\"{ruleName}\" >nul 2>&1 & " +
                         $"netsh advfirewall firewall add rule name=\"{ruleName}\" dir=in action=allow protocol={proto} localport={port} >nul 2>&1";
            StartHiddenProcess("cmd.exe", cmd)?.WaitForExit();
        }

        private Process? StartHiddenProcess(string exe, string args)
        {
            return Process.Start(new ProcessStartInfo
            {
                FileName = exe,
                Arguments = args,
                WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory,
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden
            });
        }

        private void StopServerProcesses()
        {
            try { _bridgeCts?.Cancel(); } catch { }
            try { _tcpBridgeListener?.Stop(); } catch { }
            try { _webConsoleListener?.Stop(); } catch { }

            try { if (_hbbsProc != null && !_hbbsProc.HasExited) _hbbsProc.Kill(); } catch { }
            try { if (_hbbrProc != null && !_hbbrProc.HasExited) _hbbrProc.Kill(); } catch { }

            foreach (var p in Process.GetProcessesByName("hbbs"))
            {
                try { p.Kill(); p.WaitForExit(1000); } catch { }
            }
            foreach (var p in Process.GetProcessesByName("hbbr"))
            {
                try { p.Kill(); p.WaitForExit(1000); } catch { }
            }
        }
    }
}