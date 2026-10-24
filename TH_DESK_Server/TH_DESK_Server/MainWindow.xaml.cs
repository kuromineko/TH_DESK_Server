using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

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
    public partial class MainWindow : Window
    {
        private Process? _hbbsProc;
        private Process? _hbbrProc;
        private TcpListener? _tcpBridgeListener;
        private CancellationTokenSource? _bridgeCts;
        private bool _isRunning = false;
        private string _currentLang = "TH";
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

        // ==================== 1. ระบบพับเก็บลงมุมขวาล่าง (System Tray) ====================
        private void InitSystemTray()
        {
            _trayIcon = new WinForms.NotifyIcon
            {
                Text = "TH_DESK Server (x64)",
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
            menu.Items.Add(isTH ? "เปิดหน้าต่าง TH_DESK Server" : "Open TH_DESK Server", null, (s, e) => RestoreFromTray());
            menu.Items.Add(new WinForms.ToolStripSeparator());
            menu.Items.Add(isTH ? "ปิดระบบและออกจากโปรแกรม" : "Stop Server & Exit", null, (s, e) => ExitApplication());
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

        // ==================== 3. ระบบส่งออกไฟล์ตั้งค่าให้เครื่องลูกข่าย ====================
        private void BtnExportClient_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidatePorts(out _, out int idOut, out _, out int relayOut))
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
                $"nat_type = 2\r\n" +
                $"serial = 0\r\n\r\n" +
                $"[options]\r\n" +
                $"custom-rendezvous-server = '{ddns}:{idOut}'\r\n" +
                $"relay-server = '{ddns}:{relayOut}'\r\n" +
                $"key = '{pubKey}'\r\n" +
                $"direct-server = 'Y'\r\n" +
                $"force-always-relay = 'Y'\r\n";

            File.WriteAllText(Path.Combine(exportDir, "TH_DESK2.toml"), tomlContent, new UTF8Encoding(false));

            string batContent =
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
                "echo nat_type = 2\r\n" +
                "echo serial = 0\r\n" +
                "echo.\r\n" +
                "echo [options]\r\n" +
                $"echo custom-rendezvous-server = '{ddns}:{idOut}'\r\n" +
                $"echo relay-server = '{ddns}:{relayOut}'\r\n" +
                $"echo key = '{pubKey}'\r\n" +
                "echo direct-server = 'Y'\r\n" +
                "echo force-always-relay = 'Y'\r\n" +
                ") > \"%APPDATA%\\TH_DESK\\config\\TH_DESK2.toml\"\r\n" +
                "copy /Y \"%APPDATA%\\TH_DESK\\config\\TH_DESK2.toml\" \"%APPDATA%\\RustDesk\\config\\RustDesk2.toml\" >nul\r\n" +
                "echo.\r\n" +
                $"echo [สำเร็จ] ตั้งค่าเชื่อมต่อมายัง {ddns}:{idOut} เรียบร้อยแล้ว!\r\n" +
                "echo สามารถเปิดโปรแกรม TH_DESK ใช้งานได้ทันที\r\n" +
                "pause\r\n";

            File.WriteAllText(Path.Combine(exportDir, "Setup_TH_DESK_Client.bat"), batContent, new UTF8Encoding(false));

            Process.Start("explorer.exe", exportDir);

            lblStatus.Text = _currentLang == "TH"
                ? "ส่งออกไฟล์ตั้งค่าลูกข่ายไว้ที่โฟลเดอร์ Client_Config_Export เรียบร้อยแล้ว!"
                : "Exported client configuration files to Client_Config_Export folder!";
            lblStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#38BDF8"));
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
            if (File.Exists(outputPath))
                return;

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
            pageSettings.Visibility = page == "SET" ? Visibility.Visible : Visibility.Collapsed;
            pageAbout.Visibility = page == "ABOUT" ? Visibility.Visible : Visibility.Collapsed;

            var activeBg = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0071FF"));
            var normalBg = Brushes.Transparent;
            var activeFg = Brushes.White;
            var normalFg = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#94A3B8"));

            navDashboard.Background = page == "DASH" ? activeBg : normalBg;
            navDashboard.Foreground = page == "DASH" ? activeFg : normalFg;

            navSettings.Background = page == "SET" ? activeBg : normalBg;
            navSettings.Foreground = page == "SET" ? activeFg : normalFg;

            navAbout.Background = page == "ABOUT" ? activeBg : normalBg;
            navAbout.Foreground = page == "ABOUT" ? activeFg : normalFg;
        }

        private void NavDashboard_Click(object sender, RoutedEventArgs e) => SetActivePage("DASH");
        private void NavSettings_Click(object sender, RoutedEventArgs e) => SetActivePage("SET");
        private void NavAbout_Click(object sender, RoutedEventArgs e) => SetActivePage("ABOUT");

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
            navSettings.Content = isTH ? "⚙  ตั้งค่าระบบ" : "⚙  Settings";
            navAbout.Content = isTH ? "ℹ  เกี่ยวกับโปรแกรม" : "ℹ  About & Credits";

            lblDdnsTitle.Text = isTH ? "1. โดเมน DDNS หรือ Public IP ของคุณ" : "1. Your DDNS Domain or Public IP";
            lblDdnsSub.Text = isTH ? "ที่อยู่สำหรับให้เครื่องปลายทางเชื่อมต่อเข้ามาจากภายนอก" : "External host address for client connections";
            lblIdServerTitle.Text = "2. ID Server (hbbs + TCP Bridge)";
            lblIdInPort.Text = isTH ? "IN Port (ในเครื่อง):" : "IN Port (Local PC):";
            lblIdOutPort.Text = isTH ? "Out Port (พอร์ตนอก):" : "Out Port (External):";
            lblRelayServerTitle.Text = "3. Relay Server (hbbr)";
            lblRelayInPort.Text = isTH ? "IN Port (ในเครื่อง):" : "IN Port (Local PC):";
            lblRelayOutPort.Text = isTH ? "Out Port (พอร์ตนอก):" : "Out Port (External):";

            if (!_isRunning)
            {
                btnToggle.Content = isTH ? "▶  เปิดระบบ Server + เปิด Firewall" : "▶  Start Server & Open Firewall";
                lblStatus.Text = isTH
                    ? "พร้อมทำงาน — รองรับโหมด TCP Bridge ทะลุ THDDNS / CGNAT อัตโนมัติ"
                    : "Ready — Supports TCP Bridge mode for THDDNS / CGNAT";
            }
            else
            {
                btnToggle.Content = isTH ? "■  หยุดการทำงาน Server (Stop)" : "■  Stop Server Processes";
            }

            btnCheckPort.Content = isTH ? "⚡ เช็คพอร์ต" : "⚡ Test Ports";
            lblPubKeyTitle.Text = isTH ? "Public Key (สร้างอัตโนมัติจาก id_ed25519.pub)" : "Public Key (Auto-generated from id_ed25519.pub)";
            lblClientConfigTitle.Text = isTH ? "ข้อมูลสำหรับนำไปใส่ในโปรแกรม TH_DESK (ฝั่งผู้ใช้งาน)" : "Connection Parameters for TH_DESK Client";
            btnCopy.Content = isTH ? "📋 คัดลอกค่า" : "📋 Copy Text";
            btnExportClient.Content = isTH ? "📥 ส่งออกไฟล์ลูกข่าย" : "📥 Export Client File";

            lblSettingsHeader.Text = isTH ? "ตั้งค่าระบบ (System Settings)" : "System Settings";
            lblSettingsSub.Text = isTH ? "กำหนดภาษา พฤติกรรมการเปิดพอร์ต และการจัดการไฟล์ระบบของ TH_DESK Server" : "Configure interface language, startup behavior, and server key management";
            lblLangSettingTitle.Text = isTH ? "ภาษาที่แสดงผล (Display Language)" : "Display Language";
            lblLangSettingDesc.Text = isTH ? "สลับภาษาเมนูและข้อความแจ้งเตือนระหว่าง ภาษาไทย และ English" : "Switch user interface language between Thai and English";
            chkAutoStartBoot.Content = isTH ? "เปิดโปรแกรมและรันระบบ Server อัตโนมัติเมื่อเปิดเครื่อง Windows (Auto-Start on Boot)" : "Automatically start Server on Windows Boot (Task Scheduler)";
            chkMinimizeToTray.Content = isTH ? "พับเก็บลงมุมขวาล่างข้างนาฬิกา (System Tray) เมื่อกดปุ่มย่อหน้าต่าง" : "Minimize to System Tray instead of Taskbar";
            chkAutoFirewall.Content = isTH ? "เปิดพอร์ตใน Windows Firewall อัตโนมัติทุกครั้งที่กดเริ่มระบบ Server" : "Automatically open Windows Firewall ports when starting the server";
            chkSaveConfig.Content = isTH ? "บันทึกค่า DDNS และหมายเลข Port อัตโนมัติเมื่อกดเปิดระบบ" : "Automatically save DDNS and port configuration on start";
            chkKillOnExit.Content = isTH ? "ปิดโปรเซส hbbs.exe และ hbbr.exe อัตโนมัติเมื่อกดปิดโปรแกรมนี้" : "Stop hbbs.exe and hbbr.exe background processes when closing this window";
            lblFolderSettingTitle.Text = isTH ? "การจัดการไฟล์ Server และรหัสความปลอดภัย (Key Management)" : "Server Directory & KeyPair Management";
            btnOpenFolder.Content = isTH ? "📂 เปิดโฟลเดอร์เก็บไฟล์ Server" : "📂 Open Server Folder";
            btnResetKey.Content = isTH ? "🔑 ลบและสร้าง Public Key ใหม่" : "🔑 Regenerate KeyPair";

            lblAboutHeader.Text = isTH ? "เกี่ยวกับ TH_DESK (About & Open Source Credits)" : "About TH_DESK & Open Source Credits";
            lblAboutDesc.Text = isTH ? "โปรแกรมรีโมทเดสก์ท็อปและตัวจัดการเซิร์ฟเวอร์แบบโอเพนซอร์ส แจกจ่ายฟรีสำหรับทุกคน" : "Free & Open-Source P2P Remote Desktop and Self-Hosted Server Manager";
            lblCreatorTitle.Text = isTH ? "ผู้พัฒนาและดูแลโปรเจกต์ (Developer & Maintainer)" : "Developer & Project Maintainer";
            lblCreatorName.Text = isTH ? "• พัฒนาและดูแลโปรเจกต์โดย: kuromineko" : "• Developed & Maintained by: kuromineko";
            lblCreatorRepo.Text = "• GitHub Repository: https://github.com/kuromineko/TH_DESK";
            btnOpenMyGithub.Content = isTH ? "🔗 เปิดหน้า GitHub ของ TH_DESK (kuromineko/TH_DESK)" : "🔗 Visit TH_DESK GitHub (kuromineko/TH_DESK)";
            lblSourceTitle.Text = isTH ? "ที่มาของซอร์สโค้ดต้นฉบับและสัญญาอนุญาต (Upstream & License)" : "Original Upstream Project & License Notice";
            lblLicenseNotice.Text = isTH
                ? "โปรแกรม TH_DESK พัฒนาต่อยอดจากโปรเจกต์โอเพนซอร์ส RustDesk และทำงานร่วมกับคอร์เซิร์ฟเวอร์ RustDesk Server OSS (hbbs / hbbr) เผยแพร่ภายใต้สัญญาอนุญาตสาธารณะ GNU Affero General Public License v3.0 (AGPL-3.0) โดยเปิดเผยซอร์สโค้ดและแจกจ่ายให้ใช้งานได้ฟรี"
                : "TH_DESK is derived from the open-source RustDesk project and utilizes RustDesk Server OSS (hbbs / hbbr) binaries. Distributed free of charge under the GNU Affero General Public License v3.0 (AGPL-3.0) with full source code availability.";
            btnOpenRustDeskGithub.Content = isTH ? "🌐 โค้ดต้นฉบับ RustDesk Official" : "🌐 Upstream RustDesk GitHub";
            btnOpenLicense.Content = isTH ? "📜 อ่านสัญญาอนุญาต AGPL-3.0" : "📜 View AGPL-3.0 License";
        }

        // ==================== 7. ระบบควบคุม Server & TCP-to-UDP Bridge ====================
        private async void BtnToggle_Click(object sender, RoutedEventArgs e)
        {
            bool isTH = (_currentLang == "TH");

            if (_isRunning)
            {
                StopServerProcesses();
                _isRunning = false;
                btnToggle.Content = isTH ? "▶  เปิดระบบ Server + เปิด Firewall" : "▶  Start Server & Open Firewall";
                btnToggle.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981"));
                dotStatus.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#64748B"));
                txtHeaderStatus.Text = "Offline";
                txtHeaderStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#94A3B8"));
                lblStatus.Text = isTH ? "หยุดการทำงานของระบบ Server เรียบร้อยแล้ว" : "Server processes stopped.";
                lblStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#94A3B8"));
                return;
            }

            await StartServerCoreAsync();
        }

        private async Task StartServerCoreAsync()
        {
            bool isTH = (_currentLang == "TH");

            if (!ValidatePorts(out int idIn, out int idOut, out int relayIn, out int relayOut))
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
                    isTH ? "ไม่สามารถดึงไฟล์ hbbs.exe หรือ hbbr.exe ออกมาได้!\nกรุณาตรวจสอบว่าตั้งค่า Build Action เป็น 'Embedded Resource' แล้ว"
                         : "Could not extract hbbs.exe or hbbr.exe! Please ensure their Build Action is set to 'Embedded Resource'.",
                    "TH_DESK Server", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string ddns = txtDdns.Text.Trim();
            bool openFw = (chkAutoFirewall.IsChecked == true);

            // คำนวณพอร์ตภายในสำหรับ hbbs.exe เพื่อไม่ให้ชนกับพอร์ต TCP Bridge หน้าบ้าน
            int internalHbbsPort = (idIn < 50000) ? (idIn + 10000) : (idIn - 10000);

            lblStatus.Text = isTH ? "กำลังเริ่มระบบ Server พร้อมสะพานแปลงสัญญาณ TCP-to-UDP Bridge..." : "Starting Server with TCP-to-UDP Bridge...";
            lblStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#38BDF8"));

            await Task.Run(() =>
            {
                StopServerProcesses();

                if (openFw)
                {
                    AllowProgramInFirewall("TH_DESK_SERVER_MANAGER", Process.GetCurrentProcess().MainModule?.FileName ?? "");
                    AllowProgramInFirewall("TH_DESK_HBBS_APP", hbbsPath);
                    AllowProgramInFirewall("TH_DESK_HBBR_APP", hbbrPath);

                    for (int p = idIn - 1; p <= idIn + 2; p++)
                    {
                        if (p >= 1 && p <= 65535)
                        {
                            OpenFirewall($"TH_DESK_ID_TCP_{p}", p, "TCP");
                            OpenFirewall($"TH_DESK_ID_UDP_{p}", p, "UDP");
                        }
                    }
                    OpenFirewall($"TH_DESK_RELAY_TCP_{relayIn}", relayIn, "TCP");
                }

                // 1. รัน hbbr.exe (Relay Server) ที่พอร์ต relayIn ตามปกติ (เพราะเป็น TCP อยู่แล้ว)
                _hbbrProc = StartHiddenProcess(hbbrPath, $"-p {relayIn}");

                // 2. รัน hbbs.exe ที่พอร์ตหลังบ้าน (internalHbbsPort)
                _hbbsProc = StartHiddenProcess(hbbsPath, $"-p {internalHbbsPort} -r {ddns}:{relayOut}");

                // 3. เปิดสะพาน TCP-to-UDP Bridge ที่พอร์ตหน้าบ้าน (idIn)
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
                await Task.Delay(400);
            }

            txtPubKey.Text = string.IsNullOrEmpty(pubKey) ? "Generating..." : pubKey;
            txtClientConfig.Text =
                $"ID Server   : {ddns}:{idOut}  (ใน LAN ใช้ :{idIn})\r\n" +
                $"Relay Server: {ddns}:{relayOut}  (ใน LAN ใช้ :{relayIn})\r\n" +
                $"Public Key  : {pubKey}";

            _isRunning = true;
            btnToggle.Content = isTH ? "■  หยุดการทำงาน Server (Stop)" : "■  Stop Server Processes";
            btnToggle.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444"));
            dotStatus.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981"));
            txtHeaderStatus.Text = "Online";
            txtHeaderStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981"));
            lblStatus.Text = isTH
                ? $"ทำงานอยู่ (TCP Bridge Mode) — รับพอร์ต {idIn}/{relayIn} ➔ ออกสู่ {ddns}:{idOut}/{relayOut}"
                : $"Online (TCP Bridge Mode) — Local {idIn}/{relayIn} ➔ External {ddns}:{idOut}/{relayOut}";
            lblStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981"));
        }

        // ==================== สะพานแปลงสัญญาณ TCP <-> UDP (หัวใจหลักทะลุ THDDNS) ====================
        private void StartTcpBridge(int publicPort, int internalHbbsPort)
        {
            _bridgeCts = new CancellationTokenSource();
            var token = _bridgeCts.Token;

            // เปิดรับทั้ง TCP (สำหรับทะลุ THDDNS) และ UDP (สำหรับเครื่องที่ต่อ UDP ปกติ)
            _tcpBridgeListener = new TcpListener(IPAddress.Any, publicPort);
            _tcpBridgeListener.Start();

            // Task 1: รับสาย TCP จากภายนอก แล้วแยกแยะว่าเป็นคำสั่ง UDP-over-TCP (0xFE) หรือ TCP ปกติ
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

            // Task 2: ส่งต่อ UDP หน้าบ้าน (เผื่อใช้งานในวง LAN หรือลูกข่ายเดิม) ไปยัง internalHbbsPort
            Task.Run(async () =>
            {
                using var udpListener = new UdpClient(publicPort);
                var clientMap = new System.Collections.Concurrent.ConcurrentDictionary<IPEndPoint, UdpClient>();
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        var received = await udpListener.ReceiveAsync(token);
                        var senderEp = received.RemoteEndPoint;

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
            using (client)
            {
                client.NoDelay = true;
                using var stream = client.GetStream();

                byte[] firstByte = new byte[1];
                int r = await stream.ReadAsync(firstByte, 0, 1, token);
                if (r == 0) return;

                // ถ้ารหัสไบต์แรกเป็น 0xFE แปลว่าเป็นโหมด "UDP-over-TCP Tunnel" จากลูกข่าย TH_DESK!
                if (firstByte[0] == 0xFE)
                {
                    using var udp = new UdpClient();
                    udp.Connect("127.0.0.1", internalHbbsPort);

                    // ขาอ่านจาก UDP (hbbs) -> ส่งกลับเข้าท่อ TCP (ลูกข่าย)
                    var udpToTcp = Task.Run(async () =>
                    {
                        try
                        {
                            while (!token.IsCancellationRequested)
                            {
                                var res = await udp.ReceiveAsync(token);
                                byte[] lenBuf = BitConverter.GetBytes((ushort)res.Buffer.Length);
                                await stream.WriteAsync(lenBuf, 0, 2, token);
                                await stream.WriteAsync(res.Buffer, 0, res.Buffer.Length, token);
                                await stream.FlushAsync(token);
                            }
                        }
                        catch { }
                    }, token);

                    // ขาอ่านจากท่อ TCP (ลูกข่าย) -> ยิงเข้า UDP (hbbs)
                    byte[] lenBytes = new byte[2];
                    while (!token.IsCancellationRequested)
                    {
                        if (!await ReadExactAsync(stream, lenBytes, 2, token)) break;
                        int packetLen = BitConverter.ToUInt16(lenBytes, 0);
                        if (packetLen <= 0 || packetLen > 65500) break;

                        byte[] payload = new byte[packetLen];
                        if (!await ReadExactAsync(stream, payload, packetLen, token)) break;

                        await udp.SendAsync(payload, packetLen);
                    }
                }
                else
                {
                    // ถ้าไม่ใช่ 0xFE แปลว่าเป็นคำสั่ง TCP ปกติของ RustDesk (เช่น PunchHoleRequest / RequestRelay)
                    using var hbbsTcp = new TcpClient();
                    hbbsTcp.NoDelay = true;
                    await hbbsTcp.ConnectAsync("127.0.0.1", internalHbbsPort, token);
                    using var hbbsStream = hbbsTcp.GetStream();

                    // ส่งไบต์แรกที่อ่านมาแล้วเข้าไปก่อน แล้วเชื่อมท่อถึงกัน
                    await hbbsStream.WriteAsync(firstByte, 0, 1, token);

                    var t1 = stream.CopyToAsync(hbbsStream, token);
                    var t2 = hbbsStream.CopyToAsync(stream, token);
                    await Task.WhenAny(t1, t2);
                }
            }
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

        private async void BtnCheckPort_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidatePorts(out int idIn, out int idOut, out int relayIn, out int relayOut))
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
                    ? $"ผ่านทั้งหมด! ท่อ TCP ทั้ง ID ({idOut}) และ Relay ({relayOut}) ทะลุเร้าเตอร์พร้อมใช้งาน 100%"
                    : $"Success! Both ID ({idOut}) and Relay ({relayOut}) TCP ports are OPEN.";
                lblStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981"));
            }
            else
            {
                string failMsg = isTH
                    ? $"พอร์ตในเครื่องทำงานแล้ว แต่พอร์ตนอกยังติด: ID({idOut})={(extIdTcpOk ? "ผ่าน" : "ไม่ผ่าน")}, Relay({relayOut})={(extRelayTcpOk ? "ผ่าน" : "ไม่ผ่าน")}"
                    : $"Local OK, External status: ID({idOut})={(extIdTcpOk ? "OPEN" : "CLOSED")}, Relay({relayOut})={(extRelayTcpOk ? "OPEN" : "CLOSED")}";
                lblStatus.Text = failMsg;
                lblStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B"));
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

        // ==================== 8. ปุ่มในหน้า Settings และ About ====================
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
                MessageBox.Show(isTH ? "ลบ Key เดิมเรียบร้อยแล้ว กดเปิด Server ที่หน้าหลักเพื่อสร้าง Key ใหม่ได้เลยครับ" : "Old KeyPair deleted. Start the server to generate a new one.");
            }
        }

        private void BtnOpenMyGithub_Click(object sender, RoutedEventArgs e) => OpenUrl("https://github.com/kuromineko/TH_DESK");
        private void BtnOpenRustDesk_Click(object sender, RoutedEventArgs e) => OpenUrl("https://github.com/rustdesk/rustdesk");
        private void BtnOpenLicense_Click(object sender, RoutedEventArgs e) => OpenUrl("https://www.gnu.org/licenses/agpl-3.0.html");

        private void OpenUrl(string url)
        {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
        }

        // ==================== 9. บันทึกและโหลดค่า Config ====================
        private void SaveConfig()
        {
            try
            {
                string minTray = (chkMinimizeToTray.IsChecked == true) ? "1" : "0";
                string data = $"{_currentLang}|{txtDdns.Text}|{txtIdIn.Text}|{txtIdOut.Text}|{txtRelayIn.Text}|{txtRelayOut.Text}|{minTray}";
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
                    if (parts.Length >= 7)
                    {
                        chkMinimizeToTray.IsChecked = (parts[6] == "1");
                    }
                }
            }
            catch { }
        }

        // ==================== 10. Helper Functions & Window Events ====================
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

        private bool ValidatePorts(out int idIn, out int idOut, out int relayIn, out int relayOut)
        {
            idIn = idOut = relayIn = relayOut = 0;
            bool valid = int.TryParse(txtIdIn.Text, out idIn) && idIn >= 1 && idIn <= 65535 &&
                         int.TryParse(txtIdOut.Text, out idOut) && idOut >= 1 && idOut <= 65535 &&
                         int.TryParse(txtRelayIn.Text, out relayIn) && relayIn >= 1 && relayIn <= 65535 &&
                         int.TryParse(txtRelayOut.Text, out relayOut) && relayOut >= 1 && relayOut <= 65535;

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