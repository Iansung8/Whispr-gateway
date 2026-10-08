// WhisprGateway.exe - tray controller + supervisor for whisper-gateway.js.
// Built with the C# 5 compiler that ships with .NET Framework (see build.ps1), so: no string
// interpolation, no ?. operators, no expression-bodied members.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;

namespace WhisprGateway
{
    static class Program
    {
        [DllImport("user32.dll")]
        static extern bool SetProcessDPIAware();

        [STAThread]
        static int Main(string[] args)
        {
            foreach (string a in args)
            {
                if (a == "--enable-autostart") { Autostart.Set(true); return 0; }
                if (a == "--disable-autostart") { Autostart.Set(false); return 0; }
            }
            int lang = Array.IndexOf(args, "--lang");
            L.Set(lang >= 0 && lang + 1 < args.Length ? args[lang + 1] : "auto");

            // Debug aid: render the info window to a PNG (checks layout at the current display scale).
            int render = Array.IndexOf(args, "--render-info");
            if (render >= 0 && render + 1 < args.Length)
            {
                SetProcessDPIAware();
                Application.EnableVisualStyles();
                List<string> hosts = new List<string>();
                hosts.Add("192.168.1.23|" + L.T("乙太網路", "Ethernet")); // placeholder addresses, only for this layout check
                hosts.Add("10.0.0.5|ZeroTier One [0123456789abcdef]");
                using (InfoForm form = new InfoForm(8790, hosts, "Qwen3-ASR-1.7B", Array.IndexOf(args, "--remote-off") < 0))
                    SaveForm(form, args[render + 1]);
                return 0;
            }

            // Debug aid: render the VRAM window to a PNG, with the running gateway's data, or with sample
            // data when it does not answer (or with --sample).
            int renderGuard = Array.IndexOf(args, "--render-guard");
            if (renderGuard >= 0 && renderGuard + 1 < args.Length)
            {
                SetProcessDPIAware();
                Application.EnableVisualStyles();
                bool sample = Array.IndexOf(args, "--sample") >= 0;
                Func<string, string, Dictionary<string, object>> call = delegate(string method, string path)
                {
                    Dictionary<string, object> real = sample ? null : Gateway.Call(method, path);
                    if (real != null || method != "GET") return real;
                    return new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(GuardForm.SampleJson);
                };
                using (GuardForm form = new GuardForm(call)) SaveForm(form, args[renderGuard + 1]);
                return 0;
            }

            // Debug aid: render the tray icons (large and at tray size) to a PNG, in the order of IconColors.
            int renderIcons = Array.IndexOf(args, "--render-icons");
            if (renderIcons >= 0 && renderIcons + 1 < args.Length)
            {
                using (Bitmap bmp = new Bitmap(TrayApp.IconColors.Length * 56, 64))
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.FromArgb(32, 32, 32));
                    for (int i = 0; i < TrayApp.IconColors.Length; i++)
                        using (Icon icon = TrayApp.MakeIcon(TrayApp.IconColors[i].Value))
                        {
                            g.DrawIcon(icon, new Rectangle(i * 56 + 4, 4, 32, 32));
                            g.DrawIcon(icon, new Rectangle(i * 56 + 12, 42, 16, 16));
                        }
                    bmp.Save(args[renderIcons + 1], System.Drawing.Imaging.ImageFormat.Png);
                }
                return 0;
            }

            bool createdNew;
            using (Mutex mutex = new Mutex(true, "Local\\WhisprGatewayTray", out createdNew))
            {
                if (!createdNew) return 0;
                SetProcessDPIAware();
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                bool quiet = Array.IndexOf(args, "--autostart") >= 0;
                Application.Run(new TrayApp(quiet));
            }
            return 0;
        }

        static void SaveForm(Form form, string png)
        {
            form.Show();
            Application.DoEvents();
            using (Bitmap bmp = new Bitmap(form.Width, form.Height))
            {
                form.DrawToBitmap(bmp, new Rectangle(0, 0, form.Width, form.Height));
                bmp.Save(png, System.Drawing.Imaging.ImageFormat.Png);
            }
        }
    }

    // UI language: Traditional Chinese or English. "auto" follows the Windows display language. Every
    // text is written as T("中文", "English") where it is used, so the two versions stay side by side.
    static class L
    {
        public static bool En;
        public static string Setting = "auto";

        public static void Set(string setting)
        {
            Setting = setting == "en" || setting == "zh-TW" ? setting : "auto";
            En = Setting == "auto"
                ? !CultureInfo.CurrentUICulture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase)
                : Setting == "en";
        }

        public static string Code { get { return En ? "en" : "zh-TW"; } }
        public static string T(string zh, string en) { return En ? en : zh; }
        public static string FontName { get { return En ? "Segoe UI" : "Microsoft JhengHei UI"; } }
    }

    // The gateway's loopback control API (/control/*).
    static class Gateway
    {
        public static int Port = 8790;
        static readonly JavaScriptSerializer json = new JavaScriptSerializer();

        public static Dictionary<string, object> Call(string method, string path)
        {
            try
            {
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create("http://127.0.0.1:" + Port + path);
                req.Method = method;
                req.Proxy = null;
                req.Timeout = 1500;
                req.ReadWriteTimeout = 1500;
                if (method == "POST") req.ContentLength = 0;
                using (WebResponse resp = req.GetResponse())
                using (StreamReader reader = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                {
                    return json.Deserialize<Dictionary<string, object>>(reader.ReadToEnd());
                }
            }
            catch { return null; }
        }

        // Small readers for the deserialized JSON (numbers arrive as int, long or decimal).
        public static double Num(Dictionary<string, object> d, string key)
        {
            return d != null && d.ContainsKey(key) && d[key] != null ? Convert.ToDouble(d[key]) : 0;
        }
        public static string Str(Dictionary<string, object> d, string key)
        {
            return d != null && d.ContainsKey(key) && d[key] != null ? Convert.ToString(d[key]) : null;
        }
        public static bool Bool(Dictionary<string, object> d, string key)
        {
            return d != null && d.ContainsKey(key) && d[key] != null && Convert.ToBoolean(d[key]);
        }
        public static Dictionary<string, object> Obj(Dictionary<string, object> d, string key)
        {
            return d != null && d.ContainsKey(key) ? d[key] as Dictionary<string, object> : null;
        }
        public static IEnumerable List(Dictionary<string, object> d, string key)
        {
            return d != null && d.ContainsKey(key) && d[key] is IEnumerable && !(d[key] is string) ? (IEnumerable)d[key] : new object[0];
        }
        public static string Gb(double mib)
        {
            return (mib / 1024.0).ToString("0.0") + " GB";
        }
    }

    // "Start with Windows" = a per-user Task Scheduler task with a logon trigger (no admin needed).
    // It replaced an HKCU Run entry: on the development machine Explorer ran every other Run entry at
    // logon but silently skipped this one, and a Run entry cannot be test-fired. A task can
    // (`schtasks /Run /TN WhisprGateway`), gets a short delay so the VPN adapter and GPU driver are
    // up, and is re-registered automatically when the folder has moved.
    static class Autostart
    {
        const string TaskName = "WhisprGateway";
        const string LegacyRunKey = "Software\\Microsoft\\Windows\\CurrentVersion\\Run";

        static int Schtasks(string args, out string output)
        {
            ProcessStartInfo psi = new ProcessStartInfo("schtasks.exe", args);
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            using (Process p = Process.Start(psi))
            {
                output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
                p.WaitForExit(15000);
                return p.ExitCode;
            }
        }

        static bool TaskExists(out bool pointsHere)
        {
            string xml;
            pointsHere = false;
            if (Schtasks("/Query /TN \"" + TaskName + "\" /XML", out xml) != 0) return false;
            // "Up to date" = points at this exe AND has the watchdog repetition (older versions lacked it).
            pointsHere = xml.IndexOf(System.Security.SecurityElement.Escape(Application.ExecutablePath), StringComparison.OrdinalIgnoreCase) >= 0
                && xml.IndexOf("<Repetition>", StringComparison.OrdinalIgnoreCase) >= 0;
            return true;
        }

        public static bool IsEnabled()
        {
            bool pointsHere;
            return TaskExists(out pointsHere);
        }

        public static bool Set(bool enabled)
        {
            RemoveLegacyRunEntry();
            string output;
            if (!enabled) return Schtasks("/Delete /TN \"" + TaskName + "\" /F", out output) == 0;

            string user = System.Security.Principal.WindowsIdentity.GetCurrent().Name;
            string exe = Application.ExecutablePath;
            Func<string, string> esc = System.Security.SecurityElement.Escape;
            string xml =
                "<?xml version=\"1.0\" encoding=\"UTF-16\"?>\r\n" +
                "<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">\r\n" +
                "  <RegistrationInfo><Description>Starts the WhisprGateway tray app (on-demand speech-to-text gateway for OpenWhispr) at logon.</Description></RegistrationInfo>\r\n" +
                "  <Triggers>\r\n" +
                "    <LogonTrigger><Enabled>true</Enabled><UserId>" + esc(user) + "</UserId><Delay>PT20S</Delay></LogonTrigger>\r\n" +
                // Watchdog: every 5 minutes. While the tray runs as this task's instance, IgnoreNew makes
                // the tick a no-op (a manually started copy holds the mutex, so the new one exits at once);
                // if the tray was killed or crashed, the next tick brings it back.
                "    <TimeTrigger><Enabled>true</Enabled><StartBoundary>" + DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss") + "</StartBoundary>" +
                "<Repetition><Interval>PT5M</Interval><StopAtDurationEnd>false</StopAtDurationEnd></Repetition></TimeTrigger>\r\n" +
                "  </Triggers>\r\n" +
                "  <Principals><Principal id=\"Author\"><UserId>" + esc(user) + "</UserId><LogonType>InteractiveToken</LogonType><RunLevel>LeastPrivilege</RunLevel></Principal></Principals>\r\n" +
                "  <Settings>\r\n" +
                "    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>\r\n" +
                "    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>\r\n" +
                "    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>\r\n" +
                "    <StartWhenAvailable>true</StartWhenAvailable>\r\n" +
                "    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>\r\n" +
                "    <Enabled>true</Enabled>\r\n" +
                "  </Settings>\r\n" +
                "  <Actions Context=\"Author\"><Exec><Command>" + esc(exe) + "</Command><Arguments>--autostart</Arguments><WorkingDirectory>" + esc(Path.GetDirectoryName(exe)) + "</WorkingDirectory></Exec></Actions>\r\n" +
                "</Task>\r\n";
            string tmp = Path.Combine(Path.GetTempPath(), "whisprgateway-task.xml");
            File.WriteAllText(tmp, xml, Encoding.Unicode); // UTF-16 with BOM, as the declaration says
            try { return Schtasks("/Create /TN \"" + TaskName + "\" /XML \"" + tmp + "\" /F", out output) == 0; }
            finally { try { File.Delete(tmp); } catch { } }
        }

        // Called at startup: move an old Run entry over to the task, and re-point the task after the
        // folder has been moved.
        public static void Repair()
        {
            bool pointsHere;
            bool exists = TaskExists(out pointsHere);
            if (RemoveLegacyRunEntry() && !exists) { Set(true); return; }
            if (exists && !pointsHere) Set(true);
        }

        static bool RemoveLegacyRunEntry()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(LegacyRunKey, true))
                {
                    if (key == null || key.GetValue(TaskName) == null) return false;
                    key.DeleteValue(TaskName, false);
                    return true;
                }
            }
            catch { return false; }
        }
    }

    class TrayApp : ApplicationContext
    {
        readonly string hostDir;
        readonly string gatewayJs;
        readonly string logFile;
        readonly string modelsDir;
        readonly int port;
        readonly List<string> remoteHosts = new List<string>();
        readonly JavaScriptSerializer json = new JavaScriptSerializer();

        readonly NotifyIcon notify = new NotifyIcon();
        readonly ContextMenuStrip menu = new ContextMenuStrip();
        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        readonly Dictionary<string, Icon> icons = new Dictionary<string, Icon>();

        ToolStripMenuItem miStatus, miDetail, miProblem, miLoad, miUnload, miIdle, miModel, miDevice, miPunct, miConvert, miVocab,
            miGuardMenu, miGuard, miRemote, miAutostart, miLang;
        readonly int[] idleChoices = new int[] { 5, 15, 30, 60, 0 };

        Process nodeProc;
        DateTime nodeStartedAt = DateTime.MinValue;
        DateTime restartNotBefore = DateTime.MinValue;
        bool exitHandled = true;
        bool quitting;
        volatile bool autostartOn; // cached: asking Task Scheduler takes a few hundred ms
        Dictionary<string, object> lastStatus;
        InfoForm infoForm;
        GuardForm guardForm;

        public TrayApp(bool quiet)
        {
            hostDir = Path.GetDirectoryName(Application.ExecutablePath);
            gatewayJs = Path.Combine(hostDir, "whisper-gateway.js");
            logFile = Path.Combine(hostDir, "gateway.log");

            // First run: the live config is created from the shipped defaults (the live file is not in git).
            string configPath = Path.Combine(hostDir, "gateway.config.json");
            // The config and the log live next to the exe, so the folder must be writable (not Program Files).
            try { if (!File.Exists(configPath)) File.Copy(Path.Combine(hostDir, "gateway.config.default.json"), configPath); }
            catch (Exception e)
            {
                MessageBox.Show(L.T("無法在這個資料夾建立設定檔：\r\n" + hostDir + "\r\n\r\n請把 WhisprGateway 放到你有寫入權限的資料夾，例如 %LOCALAPPDATA%\\WhisprGateway（不要放在 Program Files）。\r\n\r\n" + e.Message,
                    "Cannot create the config file in this folder:\r\n" + hostDir + "\r\n\r\nPut WhisprGateway into a folder you can write to, e.g. %LOCALAPPDATA%\\WhisprGateway (not Program Files).\r\n\r\n" + e.Message),
                    "WhisprGateway", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Environment.Exit(1);
            }
            Dictionary<string, object> cfg = json.Deserialize<Dictionary<string, object>>(
                File.ReadAllText(configPath, Encoding.UTF8));
            port = Convert.ToInt32(cfg["port"]);
            Gateway.Port = port;
            L.Set(cfg.ContainsKey("uiLanguage") ? Convert.ToString(cfg["uiLanguage"]) : "auto");
            // Same rule as the gateway: relative paths are relative to the app folder.
            string models = Environment.ExpandEnvironmentVariables(cfg.ContainsKey("modelsDir") ? Convert.ToString(cfg["modelsDir"]) : "models");
            modelsDir = Path.IsPathRooted(models) ? models : Path.Combine(hostDir, models);

            foreach (KeyValuePair<string, Color> c in IconColors) icons[c.Key] = MakeIcon(c.Value);

            BuildMenu();
            menu.Opening += delegate { Refresh(); RebuildModelMenu(); RebuildDeviceMenu(); miAutostart.Checked = autostartOn; };
            notify.Icon = icons["down"];
            notify.Text = L.T("語音辨識閘道：啟動中", "WhisprGateway: starting");
            notify.ContextMenuStrip = menu;
            notify.Visible = true;
            notify.MouseUp += delegate(object s, MouseEventArgs e)
            {
                if (e.Button != MouseButtons.Left) return;
                MethodInfo show = typeof(NotifyIcon).GetMethod("ShowContextMenu", BindingFlags.Instance | BindingFlags.NonPublic);
                if (show != null) show.Invoke(notify, null);
            };
            notify.BalloonTipClicked += delegate { ShowInfo(); };

            // Off the UI thread: migrate an old Run entry / re-point a moved folder, then read the state.
            ThreadPool.QueueUserWorkItem(delegate
            {
                try { Autostart.Repair(); autostartOn = Autostart.IsEnabled(); } catch { }
            });

            timer.Interval = 3000;
            timer.Tick += delegate { Tick(); };
            timer.Start();
            Tick();

            if (!quiet)
            {
                notify.ShowBalloonTip(6000, L.T("語音辨識閘道已啟動", "WhisprGateway is running"),
                    L.T("點這裡或圖示選單的「OpenWhispr 要填什麼」查看端點網址。",
                        "Click here, or \"OpenWhispr settings\" in the tray menu, for the endpoint URL."), ToolTipIcon.Info);
            }
        }

        // ---------------------------------------------------------------- menu
        void BuildMenu()
        {
            miStatus = new ToolStripMenuItem(L.T("狀態：啟動中", "Status: starting")); miStatus.Enabled = false;
            miDetail = new ToolStripMenuItem(" "); miDetail.Enabled = false;
            miProblem = new ToolStripMenuItem(" "); miProblem.ForeColor = Color.FromArgb(190, 60, 40); miProblem.Visible = false;
            miProblem.Click += delegate
            {
                string readme = Path.Combine(hostDir, L.En ? "README.md" : "README.zh-TW.md");
                if (!File.Exists(readme)) readme = Path.Combine(hostDir, "README.md");
                if (File.Exists(readme)) Process.Start("notepad.exe", "\"" + readme + "\"");
            };
            miDevice = new ToolStripMenuItem(L.T("運算裝置", "Device"));
            miPunct = new ToolStripMenuItem(L.T("自動補標點（依停頓切句，whisper 模型）", "Add punctuation from pauses (whisper models)"));
            miPunct.Click += delegate
            {
                Gateway.Call("POST", "/control/punctuation?enabled=" + (miPunct.Checked ? "false" : "true"));
                Refresh();
            };
            miConvert = new ToolStripMenuItem(L.T("簡體輸出轉台灣繁體（Qwen3-ASR 系列）", "Convert Simplified output to Taiwan Traditional (Qwen3-ASR family)"));
            miConvert.Click += delegate
            {
                Gateway.Call("POST", "/control/convert?enabled=" + (miConvert.Checked ? "false" : "true"));
                Refresh();
            };
            // Both files are plain text the gateway re-reads on the next request (no restart needed).
            miVocab = new ToolStripMenuItem(L.T("詞彙（Qwen3-ASR 系列）", "Vocabulary (Qwen3-ASR family)"));
            ToolStripMenuItem miVocabEdit = new ToolStripMenuItem(L.T("編輯詞彙提示（英文術語、人名、專有名詞）…", "Edit vocabulary hints (terms, names)…"));
            ToolStripMenuItem miLexEdit = new ToolStripMenuItem(L.T("編輯台灣用詞替換表…", "Edit the word replacement table…"));
            miVocabEdit.Click += delegate { OpenDataFile("vocabularyFile"); };
            miLexEdit.Click += delegate { OpenDataFile("lexiconFile"); };
            miVocab.DropDownItems.Add(miVocabEdit);
            miVocab.DropDownItems.Add(miLexEdit);
            miGuardMenu = new ToolStripMenuItem(L.T("讓出 GPU 給其他程式", "Yield the GPU to other programs"));
            miGuard = new ToolStripMenuItem(L.T("VRAM 不足時自動讓出 GPU（例如開遊戲時）", "Yield automatically when VRAM runs short (e.g. games)"));
            miGuard.Click += delegate
            {
                Gateway.Call("POST", "/control/vram-guard?enabled=" + (miGuard.Checked ? "false" : "true"));
                Refresh();
            };
            ToolStripMenuItem miGuardPanel = new ToolStripMenuItem(L.T("VRAM 用量、白名單與門檻…", "VRAM usage, whitelist and thresholds…"));
            miGuardPanel.Click += delegate { ShowGuard(); };
            miGuardMenu.DropDownItems.Add(miGuard);
            miGuardMenu.DropDownItems.Add(miGuardPanel);
            miLoad = new ToolStripMenuItem(L.T("立即載入模型（佔用 GPU）", "Load model now (uses the GPU)"));
            miUnload = new ToolStripMenuItem(L.T("立即卸載模型（釋放 GPU）", "Unload model now (frees the GPU)"));
            miIdle = new ToolStripMenuItem(L.T("閒置自動卸載", "Unload when idle"));
            miModel = new ToolStripMenuItem(L.T("模型", "Model"));
            miRemote = new ToolStripMenuItem(L.T("允許其他電腦連線（區域網路／VPN）", "Allow other computers (LAN / VPN)"));
            miAutostart = new ToolStripMenuItem(L.T("開機（登入）時自動啟動", "Start at sign-in"));
            // Bilingual on purpose: whoever cannot read the current language still finds it.
            miLang = new ToolStripMenuItem("語言 / Language");
            string[][] langs = { new string[] { "auto", L.T("自動（跟隨 Windows 顯示語言）", "Automatic (Windows display language)") },
                                 new string[] { "zh-TW", "繁體中文" }, new string[] { "en", "English" } };
            foreach (string[] lang in langs)
            {
                ToolStripMenuItem item = new ToolStripMenuItem(lang[1]);
                item.Tag = lang[0];
                item.Checked = lang[0] == L.Setting;
                item.Click += delegate(object s, EventArgs e) { SetLanguage((string)((ToolStripMenuItem)s).Tag); };
                miLang.DropDownItems.Add(item);
            }
            ToolStripMenuItem miInfo = new ToolStripMenuItem(L.T("OpenWhispr 要填什麼…", "OpenWhispr settings…"));
            ToolStripMenuItem miLog = new ToolStripMenuItem(L.T("開啟記錄檔", "Open log"));
            ToolStripMenuItem miRestart = new ToolStripMenuItem(L.T("重新啟動閘道", "Restart gateway"));
            ToolStripMenuItem miQuit = new ToolStripMenuItem(L.T("結束（停止閘道並釋放 GPU）", "Quit (stops the gateway, frees the GPU)"));

            foreach (int minutes in idleChoices)
            {
                ToolStripMenuItem item = new ToolStripMenuItem(minutes == 0 ? L.T("不自動卸載（只手動）", "Never (manual only)") : minutes + L.T(" 分鐘", " minutes"));
                item.Tag = minutes;
                item.Click += delegate(object s, EventArgs e)
                {
                    Gateway.Call("POST", "/control/idle-minutes?value=" + ((ToolStripMenuItem)s).Tag);
                    Refresh();
                };
                miIdle.DropDownItems.Add(item);
            }

            miLoad.Click += delegate { Gateway.Call("POST", "/control/load"); Refresh(); };
            miUnload.Click += delegate { Gateway.Call("POST", "/control/unload"); Refresh(); };
            miRemote.Click += delegate
            {
                Gateway.Call("POST", "/control/remote?enabled=" + (miRemote.Checked ? "false" : "true"));
                Refresh();
            };
            miAutostart.Click += delegate
            {
                bool target = !autostartOn;
                if (Autostart.Set(target)) autostartOn = target;
                else notify.ShowBalloonTip(6000, "WhisprGateway", L.T("無法變更自動啟動設定（工作排程器拒絕）。", "Could not change the start-at-sign-in setting (Task Scheduler refused)."), ToolTipIcon.Warning);
            };
            miInfo.Click += delegate { ShowInfo(); };
            miLog.Click += delegate { if (File.Exists(logFile)) Process.Start("notepad.exe", "\"" + logFile + "\""); };
            miRestart.Click += delegate { StopGateway(); restartNotBefore = DateTime.MinValue; StartGateway(); };
            miQuit.Click += delegate
            {
                quitting = true;
                timer.Stop();
                StopGateway();
                notify.Visible = false;
                ExitThread();
            };

            menu.Items.AddRange(new ToolStripItem[] {
                miStatus, miDetail, miProblem, new ToolStripSeparator(),
                miLoad, miUnload, new ToolStripSeparator(),
                miIdle, miModel, miDevice, miGuardMenu, miPunct, miConvert, miVocab, miRemote, new ToolStripSeparator(),
                miInfo, miAutostart, miLang, new ToolStripSeparator(),
                miLog, miRestart, miQuit });
        }

        // The menu is rebuilt in the new language once it has closed (the click comes from inside it).
        void SetLanguage(string setting)
        {
            Gateway.Call("POST", "/control/ui-language?value=" + setting);
            menu.BeginInvoke((MethodInvoker)delegate
            {
                L.Set(setting);
                if (infoForm != null && !infoForm.IsDisposed) infoForm.Close();
                if (guardForm != null && !guardForm.IsDisposed) guardForm.Close();
                menu.Items.Clear();
                BuildMenu();
                Refresh();
            });
        }

        // Devices come from the gateway: NVIDIA GPUs (CUDA engine), the Vulkan engine's automatic choice
        // and each of its cards, CPU.
        void RebuildDeviceMenu()
        {
            miDevice.DropDownItems.Clear();
            if (lastStatus == null || !lastStatus.ContainsKey("devices")) return;
            string configured = Convert.ToString(lastStatus["device"]);
            string active = Convert.ToString(lastStatus["activeDevice"]);
            string lastGroup = null;
            foreach (object o in (IEnumerable)lastStatus["devices"])
            {
                Dictionary<string, object> d = (Dictionary<string, object>)o;
                string id = Convert.ToString(d["id"]);
                string group = id.StartsWith("gpu:") ? "cuda" : id.StartsWith("vulkan") ? "vulkan" : "cpu";
                if (lastGroup != null && group != lastGroup) miDevice.DropDownItems.Add(new ToolStripSeparator());
                lastGroup = group;
                ToolStripMenuItem item = new ToolStripMenuItem(DeviceLabel(d));
                item.Tag = id;
                item.Checked = id == configured;
                item.Click += delegate(object s, EventArgs e)
                {
                    Gateway.Call("POST", "/control/device?value=" + Uri.EscapeDataString((string)((ToolStripMenuItem)s).Tag));
                    Refresh();
                };
                miDevice.DropDownItems.Add(item);
            }
            if (active != configured)
            {
                miDevice.DropDownItems.Add(new ToolStripSeparator());
                ToolStripMenuItem note = new ToolStripMenuItem(L.T("目前實際使用：", "Currently using: ") + ActiveName(active));
                note.Enabled = false;
                miDevice.DropDownItems.Add(note);
            }
        }

        // "cpu" / "vulkan" / "vulkan:<card name>" / "gpu:N" as a short name for the status line.
        static string ActiveName(string active)
        {
            if (active == "cpu") return "CPU";
            if (active.StartsWith("vulkan:")) return active.Substring(7);
            if (active.StartsWith("gpu:")) return "GPU " + active.Substring(4);
            return active == "vulkan" ? "Vulkan GPU" : "GPU";
        }

        static string DeviceLabel(Dictionary<string, object> d)
        {
            string id = Convert.ToString(d["id"]);
            string name = Gateway.Str(d, "name");
            string size = d["memoryMiB"] != null ? Math.Round(Gateway.Num(d, "memoryMiB") / 1024.0) + " GB" : null;
            if (id == "cpu") return L.T("純 CPU（慢，不佔 GPU）", "CPU only (slow, leaves the GPU alone)");
            if (id == "vulkan") return L.T("Vulkan：自動選卡", "Vulkan: choose the card automatically");
            if (id.StartsWith("vulkan:"))
            {
                string note = Gateway.Bool(d, "integrated") ? L.T("內顯，用系統記憶體", "integrated, uses system RAM") : size;
                return "Vulkan" + L.T("：", ": ") + name + (note != null ? L.T("（" + note + "）", " (" + note + ")") : "");
            }
            return "CUDA GPU " + id.Substring(4) + L.T("：", ": ") + name + (size != null ? L.T("（" + size + "）", " (" + size + ")") : "");
        }

        // Opens one of the gateway's editable text files (paths come from /control/status).
        void OpenDataFile(string key)
        {
            string file = lastStatus != null && lastStatus.ContainsKey(key) ? Convert.ToString(lastStatus[key]) : null;
            if (string.IsNullOrEmpty(file) || !File.Exists(file))
            {
                notify.ShowBalloonTip(5000, "WhisprGateway", L.T("檔案還沒建立：閘道啟動後會自動產生，請稍後再試。",
                    "The file does not exist yet; the gateway creates it after it starts. Try again shortly."), ToolTipIcon.Info);
                return;
            }
            Process.Start("notepad.exe", "\"" + file + "\"");
        }

        // Any ggml *.bin dropped into modelsDir becomes selectable - that is how a model is updated.
        void RebuildModelMenu()
        {
            miModel.DropDownItems.Clear();
            string current = lastStatus != null && lastStatus.ContainsKey("modelFile") ? Convert.ToString(lastStatus["modelFile"]) : "";
            if (Directory.Exists(modelsDir))
            {
                // *.bin = whisper.cpp models; *.gguf = Qwen3-ASR family (their mmproj-*.gguf audio encoders are not models)
                List<string> files = new List<string>(Directory.GetFiles(modelsDir, "*.bin"));
                foreach (string gguf in Directory.GetFiles(modelsDir, "*.gguf"))
                    if (!Regex.IsMatch(Path.GetFileName(gguf), "(^mmproj-|[.-]mmproj[-.])", RegexOptions.IgnoreCase)) files.Add(gguf);
                foreach (string file in files)
                {
                    string name = Path.GetFileName(file);
                    double gb = new FileInfo(file).Length / 1073741824.0;
                    string engine = name.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase) ? "llama.cpp" : "whisper.cpp";
                    ToolStripMenuItem item = new ToolStripMenuItem(name + "  (" + gb.ToString("0.0") + " GB · " + engine + ")");
                    item.Tag = name;
                    item.Checked = string.Equals(name, current, StringComparison.OrdinalIgnoreCase);
                    item.Click += delegate(object s, EventArgs e)
                    {
                        Gateway.Call("POST", "/control/model?file=" + Uri.EscapeDataString((string)((ToolStripMenuItem)s).Tag));
                        Refresh();
                    };
                    miModel.DropDownItems.Add(item);
                }
            }
            if (miModel.DropDownItems.Count > 0) miModel.DropDownItems.Add(new ToolStripSeparator());
            ToolStripMenuItem open = new ToolStripMenuItem(L.T("開啟模型資料夾（放入 ggml .bin，或 GGUF＋mmproj，即可切換）",
                "Open the models folder (drop in a ggml .bin, or a GGUF with its mmproj)"));
            open.Click += delegate { Directory.CreateDirectory(modelsDir); Process.Start("explorer.exe", "\"" + modelsDir + "\""); };
            miModel.DropDownItems.Add(open);
        }

        // ---------------------------------------------------------------- state
        void Tick()
        {
            if (quitting) return;
            Refresh();
            if (lastStatus != null) return; // gateway answers (ours, or one left over from a previous run)

            bool running = nodeProc != null && !nodeProc.HasExited;
            if (running) return; // started, not listening yet
            if (!exitHandled)
            {
                exitHandled = true;
                // Died right after start (bad config, port taken): back off so this never spins.
                if ((DateTime.Now - nodeStartedAt).TotalSeconds < 30) restartNotBefore = DateTime.Now.AddSeconds(60);
            }
            if (DateTime.Now >= restartNotBefore) StartGateway();
        }

        void Refresh()
        {
            lastStatus = Gateway.Call("GET", "/control/status?lang=" + L.Code);
            if (lastStatus == null)
            {
                notify.Icon = icons["down"];
                SetTip(L.T("語音辨識閘道：未回應", "WhisprGateway: not responding"));
                miStatus.Text = L.T("狀態：閘道未回應", "Status: the gateway does not respond");
                miDetail.Text = " ";
                miProblem.Visible = false;
                miLoad.Enabled = miUnload.Enabled = miIdle.Enabled = miModel.Enabled = miDevice.Enabled = miPunct.Enabled = miConvert.Enabled = miVocab.Enabled = miGuardMenu.Enabled = miRemote.Enabled = false;
                return;
            }

            // First missing dependency / fallback, in the gateway's own words; click opens the README.
            string problem = null;
            foreach (object p in Gateway.List(lastStatus, "problems")) { problem = Convert.ToString(p); break; }
            miProblem.Visible = problem != null;
            if (problem != null) miProblem.Text = "⚠ " + problem;

            string backend = Convert.ToString(lastStatus["backend"]);
            int idle = Convert.ToInt32(lastStatus["idleMinutes"]);
            int inFlight = Convert.ToInt32(lastStatus["inFlight"]);
            bool allowRemote = Convert.ToBoolean(lastStatus["allowRemote"]);
            string active = lastStatus.ContainsKey("activeDevice") ? Convert.ToString(lastStatus["activeDevice"]) : "";
            string where = ActiveName(active);
            string label = backend == "loaded" ? L.T("模型已載入（" + where + " 使用中）", "Model loaded (on " + where + ")")
                         : backend == "loading" ? L.T("模型載入中…", "Loading model…")
                         : L.T("模型未載入（不佔資源）", "Model not loaded (uses nothing)");
            bool backedOff = Gateway.Bool(lastStatus, "backedOff");
            if (backedOff)
            {
                string why = Gateway.Str(lastStatus, "guardReason") ?? L.T("其他程式在用 VRAM", "another program is using VRAM");
                label = L.T("已讓出 GPU（" + why + "）", "GPU yielded (" + why + ")")
                      + (backend == "loaded" ? L.T("・CPU 模型已載入", " · CPU model loaded") : L.T("・需要時用 CPU", " · CPU when needed"));
            }

            // Blue whenever dictation is on the CPU: GPU released to a game (loaded or not), or CPU is the chosen device.
            string iconKey = backend == "loading" ? "loading" : backedOff || (backend == "loaded" && active == "cpu") ? "cpu" : backend;
            notify.Icon = icons.ContainsKey(iconKey) ? icons[iconKey] : icons["unloaded"];
            SetTip(L.T("語音辨識閘道：", "WhisprGateway: ") + label);
            miStatus.Text = L.T("狀態：", "Status: ") + label;

            string sep = L.T("｜", " | ");
            string engineName = Gateway.Str(lastStatus, "engine") == "llama" ? "llama.cpp" : "whisper.cpp";
            string detail = engineName + sep + (idle > 0 ? L.T("閒置 " + idle + " 分鐘自動卸載", "unloads after " + idle + " min idle") : L.T("不自動卸載", "no automatic unload"));
            Dictionary<string, object> vr = Gateway.Obj(lastStatus, "vram");
            if (vr != null) detail += sep + "VRAM " + (Gateway.Num(vr, "usedMiB") / 1024.0).ToString("0.0") + "/" + (Gateway.Num(vr, "totalMiB") / 1024.0).ToString("0.0") + " GB";
            if (lastStatus["unloadAt"] != null)
            {
                double leftMs = Convert.ToDouble(lastStatus["unloadAt"]) - (DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalMilliseconds;
                int leftMin = Math.Max(0, (int)Math.Ceiling(leftMs / 60000.0));
                detail += L.T("，約 " + leftMin + " 分鐘後卸載", ", unloads in about " + leftMin + " min");
            }
            if (inFlight > 0) detail = L.T("辨識進行中…", "Transcribing…");
            Dictionary<string, object> client = Gateway.Obj(lastStatus, "lastClient");
            if (client != null && inFlight == 0)
            {
                int agoMin = (int)(((DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalMilliseconds - Gateway.Num(client, "at")) / 60000.0);
                detail += sep + L.T("外部 " + client["ip"] + "（" + agoMin + " 分鐘前）", "remote " + client["ip"] + " (" + agoMin + " min ago)");
            }
            miDetail.Text = detail;

            miIdle.Enabled = miModel.Enabled = miDevice.Enabled = miPunct.Enabled = miConvert.Enabled = miVocab.Enabled = miGuardMenu.Enabled = miRemote.Enabled = true;
            miGuard.Checked = Gateway.Bool(lastStatus, "vramGuard");
            miGuardMenu.Text = L.T("讓出 GPU 給其他程式", "Yield the GPU to other programs") + (miGuard.Checked ? L.T("（開啟）", " (on)") : L.T("（關閉）", " (off)"));
            miPunct.Checked = Gateway.Bool(lastStatus, "punctuation");
            miConvert.Checked = Gateway.Bool(lastStatus, "convertSimplified");
            miLoad.Enabled = backend == "unloaded";
            miUnload.Enabled = backend == "loaded" && inFlight == 0;
            miRemote.Checked = allowRemote;
            foreach (ToolStripMenuItem item in miIdle.DropDownItems) item.Checked = (int)item.Tag == idle;
            if (infoForm != null && !infoForm.IsDisposed) infoForm.SetRemoteEnabled(allowRemote);
        }

        void SetTip(string text)
        {
            notify.Text = text.Length > 63 ? text.Substring(0, 63) : text;
        }

        void ShowInfo()
        {
            if (infoForm == null || infoForm.IsDisposed)
            {
                string model = lastStatus != null ? Convert.ToString(lastStatus["model"]) : "";
                bool allowRemote = lastStatus == null || Convert.ToBoolean(lastStatus["allowRemote"]);
                // This PC's private addresses as the gateway sees them, each with its adapter name, so the
                // user can pick the one the other computer can reach (LAN, ZeroTier, Tailscale, ...).
                remoteHosts.Clear();
                foreach (object o in Gateway.List(lastStatus, "addresses"))
                {
                    Dictionary<string, object> a = (Dictionary<string, object>)o;
                    remoteHosts.Add(Convert.ToString(a["address"]) + "|" + Convert.ToString(a["name"]));
                }
                infoForm = new InfoForm(port, remoteHosts, model, allowRemote);
            }
            infoForm.Show();
            infoForm.Activate();
        }

        void ShowGuard()
        {
            if (guardForm == null || guardForm.IsDisposed) guardForm = new GuardForm(Gateway.Call);
            guardForm.Show();
            guardForm.Activate();
        }

        // ---------------------------------------------------------------- gateway process
        void StartGateway()
        {
            // Optional private copy first (runtime\node.exe), then the system's Node.js.
            string node = Path.Combine(hostDir, "runtime", "node.exe");
            if (!File.Exists(node)) node = FindOnPath("node.exe");
            if (node == null)
            {
                restartNotBefore = DateTime.Now.AddMinutes(2);
                notify.ShowBalloonTip(8000, "WhisprGateway", L.T("找不到 Node.js：請安裝 Node.js 20 以上（nodejs.org），或把 node.exe 放進 runtime 資料夾。",
                    "Node.js not found: install Node.js 20 or later (nodejs.org), or put node.exe into the runtime folder."), ToolTipIcon.Error);
                return;
            }
            ProcessStartInfo psi = new ProcessStartInfo(node, "\"" + gatewayJs + "\"");
            psi.WorkingDirectory = hostDir;
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            nodeProc = Process.Start(psi);
            nodeStartedAt = DateTime.Now;
            exitHandled = false;
        }

        void StopGateway()
        {
            int pid = 0;
            if (nodeProc != null && !nodeProc.HasExited) pid = nodeProc.Id;
            else if (lastStatus != null && lastStatus.ContainsKey("pid")) pid = Convert.ToInt32(lastStatus["pid"]);
            nodeProc = null;
            exitHandled = true;
            if (pid == 0) return;
            // /T also takes down the recognition server (a child of node), which is what releases the GPU.
            ProcessStartInfo psi = new ProcessStartInfo("taskkill.exe", "/PID " + pid + " /T /F");
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            try { using (Process p = Process.Start(psi)) p.WaitForExit(5000); } catch { }
            lastStatus = null;
        }

        static string FindOnPath(string exe)
        {
            foreach (string dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
            {
                if (dir.Trim().Length == 0) continue;
                try
                {
                    string candidate = Path.Combine(dir.Trim(), exe);
                    if (File.Exists(candidate)) return candidate;
                }
                catch { }
            }
            return null;
        }

        // green = model on the GPU, blue = dictation runs on the CPU, orange = loading, grey = nothing loaded, red = gateway down
        internal static readonly KeyValuePair<string, Color>[] IconColors = {
            new KeyValuePair<string, Color>("loaded", Color.FromArgb(34, 160, 80)),
            new KeyValuePair<string, Color>("cpu", Color.FromArgb(40, 120, 215)),
            new KeyValuePair<string, Color>("loading", Color.FromArgb(230, 140, 20)),
            new KeyValuePair<string, Color>("unloaded", Color.FromArgb(120, 120, 120)),
            new KeyValuePair<string, Color>("down", Color.FromArgb(200, 50, 50)),
        };

        internal static Icon MakeIcon(Color color)
        {
            using (Bitmap bmp = new Bitmap(32, 32))
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                g.Clear(Color.Transparent);
                using (SolidBrush brush = new SolidBrush(color)) g.FillEllipse(brush, 1, 1, 30, 30);
                using (Font font = new Font("Microsoft JhengHei UI", 17, FontStyle.Bold, GraphicsUnit.Pixel))
                using (StringFormat fmt = new StringFormat())
                {
                    fmt.Alignment = StringAlignment.Center;
                    fmt.LineAlignment = StringAlignment.Center;
                    g.DrawString("語", font, Brushes.White, new RectangleF(0, 1, 32, 32), fmt);
                }
                return Icon.FromHandle(bmp.GetHicon());
            }
        }
    }

    // Shared by both windows. The process is DPI aware and fonts are in points, so text already scales;
    // pixel sizes are scaled by hand and each window sizes itself to its content (a fixed ClientSize
    // clipped the lower rows on a 150% display).
    class GatewayForm : Form
    {
        protected float scale = 1f;

        protected GatewayForm(string title)
        {
            Text = title;
            Font = new Font(L.FontName, 10f);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.None;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            using (Graphics g = CreateGraphics()) scale = g.DpiX / 96f;
        }

        protected int Px(int logical) { return (int)Math.Round(logical * scale); }

        protected Label NewLabel(string text, int maxWidth)
        {
            Label label = new Label();
            label.Text = text;
            label.AutoSize = true;
            if (maxWidth > 0) label.MaximumSize = new Size(Px(maxWidth), 0);
            return label;
        }
    }

    // "What do I type into OpenWhispr?" - endpoint URLs with copy buttons. Labels as in OpenWhispr's own
    // zh-TW / en UI (src/locales in its repository).
    class InfoForm : GatewayForm
    {
        readonly List<Control> remoteControls = new List<Control>();
        readonly Label remoteNote;

        public InfoForm(int port, List<string> remoteHosts, string model, bool allowRemote)
            : base(L.T("OpenWhispr 要填什麼", "OpenWhispr settings"))
        {
            FlowLayoutPanel root = new FlowLayoutPanel();
            root.FlowDirection = FlowDirection.TopDown;
            root.WrapContents = false;
            root.AutoSize = true;
            root.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            root.Padding = new Padding(Px(16));

            TableLayoutPanel table = new TableLayoutPanel();
            table.ColumnCount = 3;
            table.AutoSize = true;
            table.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            Label steps = NewLabel(L.T(
                "1. OpenWhispr → 設定 → 語音轉文字，模式選「自架主機」。\r\n" +
                "2. 「端點 URL」填下面的網址（別台電腦請挑它連得到的那個網段）；API 金鑰與模型名稱留空。\r\n" +
                "3. 偏好設定 → 轉錄語言選「自動」，中文書寫（語音輸入）選「保持轉錄原樣」。不要選「中文（繁體）」，它會改寫你的用詞。\r\n" +
                "第一句要多等幾秒載入模型，之後每句不到 1 秒。",
                "1. In OpenWhispr: Settings → Speech-to-Text, choose \"Self-Hosted\".\r\n" +
                "2. Enter the matching URL below as \"Endpoint URL\" (on another computer, pick the address it can reach). Leave the API key and model name empty.\r\n" +
                "3. Preferences → Transcription language: \"Auto\"; Chinese script (dictation): \"Keep as transcribed\".\r\n" +
                "The first sentence waits a few seconds while the model loads; after that each one takes under a second."), 640);
            steps.Margin = new Padding(3, 3, 3, Px(12));
            root.Controls.Add(steps);

            int row = 0;
            AddRow(table, row++, L.T("這台電腦：", "This computer:"), "http://127.0.0.1:" + port + "/v1", null);
            foreach (string entry in remoteHosts) // "address|adapter name"
            {
                string[] parts = entry.Split(new char[] { '|' }, 2);
                string adapter = parts.Length > 1 && parts[1].Length > 0 ? parts[1] : L.T("其他電腦", "other computers");
                if (adapter.Length > 22) adapter = adapter.Substring(0, 21) + "…";
                AddRow(table, row++, L.T("其他電腦（" + adapter + "）：", "Other computers (" + adapter + "):"), "http://" + parts[0] + ":" + port + "/v1", remoteControls);
            }
            AddRow(table, row++, L.T("模型名稱（選填）：", "Model name (optional):"), model, null);

            root.Controls.Add(table);

            remoteNote = NewLabel(L.T("目前已關閉外部連線：其他電腦連不進來（可在圖示選單重新開啟）。",
                "Connections from other computers are off (turn them on in the tray menu)."), 640);
            remoteNote.ForeColor = Color.FromArgb(190, 60, 40);
            remoteNote.Margin = new Padding(3, Px(8), 3, 3);
            root.Controls.Add(remoteNote);

            Controls.Add(root);
            SetRemoteEnabled(allowRemote);
        }

        public void SetRemoteEnabled(bool enabled)
        {
            foreach (Control c in remoteControls) c.Enabled = enabled;
            remoteNote.Visible = !enabled;
        }

        void AddRow(TableLayoutPanel table, int row, string caption, string value, List<Control> track)
        {
            Label label = NewLabel(caption, 0);
            label.Anchor = AnchorStyles.Left;

            TextBox box = new TextBox();
            box.Text = value;
            box.ReadOnly = true;
            box.TabStop = false; // keeps the first URL from opening pre-selected
            box.Width = Px(360);
            box.Anchor = AnchorStyles.Left;
            box.Margin = new Padding(3, Px(5), 3, Px(5));

            Button copy = new Button();
            copy.Text = L.T("複製", "Copy");
            copy.AutoSize = true;
            copy.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            copy.Anchor = AnchorStyles.Left;
            copy.Click += delegate
            {
                try { Clipboard.SetText(box.Text); copy.Text = L.T("已複製", "Copied"); } catch { }
            };

            table.Controls.Add(label, 0, row);
            table.Controls.Add(box, 1, row);
            table.Controls.Add(copy, 2, row);
            if (track != null) { track.Add(box); track.Add(copy); }
        }
    }

    // "VRAM usage, whitelist and thresholds": the programs on the card the model uses, refreshed every
    // 3 s, each with a checkbox that whitelists it (it may hold any amount of VRAM without the gateway
    // giving way), and the two thresholds in GB or as a share of the card.
    class GuardForm : GatewayForm
    {
        readonly Func<string, string, Dictionary<string, object>> call;
        readonly System.Windows.Forms.Timer poll = new System.Windows.Forms.Timer();
        readonly CheckBox chkEnabled = new CheckBox();
        readonly Label lblCard, lblState, lblSaved, eqProc, eqFree;
        readonly ProgressBar bar = new ProgressBar();
        readonly ListView list = new ListView();
        readonly TextBox txtAdd = new TextBox();
        readonly NumericUpDown numProc = new NumericUpDown(), numFree = new NumericUpDown(), numResume = new NumericUpDown();
        readonly ComboBox unitProc = new ComboBox(), unitFree = new ComboBox();
        double totalMiB; // the watched card's VRAM, 0 = unknown
        bool updating; // set while the form fills itself, so its own changes are not sent back
        bool settingsLoaded; // thresholds come from the gateway once; after that they are the user's to edit

        const int Width0 = 620;

        public GuardForm(Func<string, string, Dictionary<string, object>> call)
            : base(L.T("VRAM 用量與讓出設定", "VRAM usage and GPU yield"))
        {
            this.call = call;
            TableLayoutPanel root = new TableLayoutPanel();
            root.ColumnCount = 1;
            root.AutoSize = true;
            root.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            root.Padding = new Padding(Px(14));

            chkEnabled.Text = L.T("VRAM 不足時自動讓出 GPU（例如開遊戲時）", "Yield the GPU automatically when VRAM runs short (e.g. games)");
            chkEnabled.AutoSize = true;
            chkEnabled.CheckedChanged += delegate
            {
                if (updating) return;
                call("POST", "/control/vram-guard?enabled=" + (chkEnabled.Checked ? "true" : "false"));
                RefreshData();
            };
            root.Controls.Add(chkEnabled);

            lblCard = NewLabel(" ", Width0);
            lblCard.Margin = new Padding(3, Px(10), 3, Px(2));
            root.Controls.Add(lblCard);
            bar.Maximum = 1000;
            bar.Size = new Size(Px(Width0), Px(12));
            root.Controls.Add(bar);
            lblState = NewLabel(" ", Width0);
            lblState.Margin = new Padding(3, Px(4), 3, Px(10));
            root.Controls.Add(lblState);

            root.Controls.Add(NewLabel(L.T("用這張卡的程式，每 3 秒更新。勾選＝加入白名單：它佔再多 VRAM 也不讓出。",
                "Programs on this card, updated every 3 s. Tick one to whitelist it: it may use any amount of VRAM without the gateway giving way."), Width0));
            list.View = View.Details;
            list.CheckBoxes = true;
            list.FullRowSelect = true;
            list.HeaderStyle = ColumnHeaderStyle.Nonclickable;
            list.Size = new Size(Px(Width0), Px(230));
            list.Columns.Add(L.T("程式", "Program"), Px(200));
            list.Columns.Add("PID", Px(70), HorizontalAlignment.Right);
            list.Columns.Add("VRAM", Px(85), HorizontalAlignment.Right);
            list.Columns.Add(L.T("佔比", "Share"), Px(60), HorizontalAlignment.Right);
            list.Columns.Add(L.T("狀態", "Status"), Px(Width0 - 200 - 70 - 85 - 60 - 25));
            typeof(Control).GetProperty("DoubleBuffered", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(list, true, null);
            list.ItemCheck += delegate(object s, ItemCheckEventArgs e)
            {
                if (updating) return;
                string[] tag = (string[])list.Items[e.Index].Tag; // { name, kind }
                if (tag[1] == "ours" || tag[1] == "system") { e.NewValue = e.CurrentValue; return; }
                call("POST", "/control/vram-whitelist?add=" + (e.NewValue == CheckState.Checked ? "true" : "false") + "&name=" + Uri.EscapeDataString(tag[0]));
                BeginInvoke((MethodInvoker)RefreshData); // after the check has been applied to the item
            };
            root.Controls.Add(list);

            FlowLayoutPanel addRow = new FlowLayoutPanel();
            addRow.AutoSize = true;
            addRow.WrapContents = false;
            addRow.Margin = new Padding(0, Px(4), 0, Px(8));
            Label addLabel = NewLabel(L.T("把不在清單上的程式加入白名單：", "Whitelist a program that is not listed:"), 0);
            addLabel.Anchor = AnchorStyles.Left;
            addLabel.Margin = new Padding(3, Px(6), 3, 3);
            txtAdd.Width = Px(180);
            Button btnAdd = new Button();
            btnAdd.Text = L.T("加入", "Add");
            btnAdd.AutoSize = true;
            btnAdd.Click += delegate
            {
                string name = txtAdd.Text.Trim();
                if (name.Length == 0) return;
                call("POST", "/control/vram-whitelist?add=true&name=" + Uri.EscapeDataString(name));
                txtAdd.Text = "";
                RefreshData();
            };
            addRow.Controls.Add(addLabel);
            addRow.Controls.Add(txtAdd);
            addRow.Controls.Add(btnAdd);
            root.Controls.Add(addRow);

            GroupBox group = new GroupBox();
            group.Text = L.T("什麼時候讓出", "When to yield");
            group.AutoSize = true;
            group.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            group.MinimumSize = new Size(Px(Width0), 0);
            TableLayoutPanel grid = new TableLayoutPanel();
            grid.ColumnCount = 4;
            grid.AutoSize = true;
            grid.Location = new Point(Px(8), Px(24));
            eqProc = NewLabel(" ", 0);
            eqFree = NewLabel(" ", 0);
            AddThreshold(grid, 0, L.T("單一程式佔用 ≥", "One program uses ≥"), numProc, unitProc, eqProc);
            AddThreshold(grid, 1, L.T("或剩餘 VRAM <", "or free VRAM <"), numFree, unitFree, eqFree);
            numResume.Minimum = 5;
            numResume.Maximum = 3600;
            numResume.Increment = 5;
            numResume.Width = Px(80);
            grid.Controls.Add(Centered(NewLabel(L.T("程式結束後等", "After it exits, wait"), 0)), 0, 2);
            grid.Controls.Add(numResume, 1, 2);
            Label resumeUnit = Centered(NewLabel(L.T("秒再回到 GPU", "s, then use the GPU again"), 0));
            grid.Controls.Add(resumeUnit, 2, 2);
            grid.SetColumnSpan(resumeUnit, 2);
            Label note = NewLabel(L.T("「剩餘 VRAM」只在模型已載入 GPU 時檢查；設成 0 就不用這個條件。",
                "Free VRAM is only checked while the model is on the GPU; 0 turns this condition off."), Width0 - 30);
            note.ForeColor = SystemColors.GrayText;
            note.Margin = new Padding(3, Px(6), 3, Px(4));
            grid.Controls.Add(note, 0, 3);
            grid.SetColumnSpan(note, 4);
            group.Controls.Add(grid);
            root.Controls.Add(group);

            FlowLayoutPanel buttons = new FlowLayoutPanel();
            buttons.AutoSize = true;
            buttons.FlowDirection = FlowDirection.RightToLeft;
            buttons.Anchor = AnchorStyles.Right;
            buttons.Margin = new Padding(0, Px(10), 0, 0);
            Button btnClose = NewButton(L.T("關閉", "Close"));
            btnClose.Click += delegate { Close(); };
            Button btnApply = NewButton(L.T("套用", "Apply"));
            btnApply.Click += delegate { Apply(); };
            Button btnDefaults = NewButton(L.T("預設值", "Defaults"));
            btnDefaults.Click += delegate
            {
                SetAmount(numProc, unitProc, "MiB", 2048, true);
                SetAmount(numFree, unitFree, "MiB", 1024, false);
                numResume.Value = 60;
            };
            lblSaved = NewLabel(" ", 0);
            lblSaved.Margin = new Padding(3, Px(8), Px(10), 3);
            buttons.Controls.Add(btnClose);
            buttons.Controls.Add(btnApply);
            buttons.Controls.Add(btnDefaults);
            buttons.Controls.Add(lblSaved);
            root.Controls.Add(buttons);

            Controls.Add(root);
            AcceptButton = btnApply;
            CancelButton = btnClose;

            RefreshData();
            poll.Interval = 3000;
            poll.Tick += delegate { RefreshData(); };
            Shown += delegate { poll.Start(); };
            FormClosed += delegate { poll.Stop(); poll.Dispose(); };
        }

        Button NewButton(string text)
        {
            Button b = new Button();
            b.Text = text;
            b.AutoSize = true;
            b.MinimumSize = new Size(Px(80), 0);
            return b;
        }

        static Label Centered(Label label) { label.Anchor = AnchorStyles.Left; return label; }

        void AddThreshold(TableLayoutPanel grid, int row, string caption, NumericUpDown num, ComboBox unit, Label eq)
        {
            num.Width = Px(80);
            unit.DropDownStyle = ComboBoxStyle.DropDownList;
            unit.Items.AddRange(new object[] { "GB", "%" });
            unit.Width = Px(64);
            unit.SelectedIndex = 0;
            bool isProcess = num == numProc;
            ConfigureNum(num, false, isProcess);
            // Switching the unit converts the number (still in the old unit), so the threshold stays put.
            unit.SelectedIndexChanged += delegate
            {
                if (updating) return;
                bool percent = unit.SelectedIndex == 1;
                double mib = percent ? (double)num.Value * 1024 : (totalMiB > 0 ? (double)num.Value * totalMiB / 100 : 2048);
                if (percent) SetAmount(num, unit, "%", totalMiB > 0 ? mib * 100 / totalMiB : 15, isProcess);
                else SetAmount(num, unit, "MiB", mib, isProcess);
            };
            num.ValueChanged += delegate { UpdateEquivalents(); };
            grid.Controls.Add(Centered(NewLabel(caption, 0)), 0, row);
            grid.Controls.Add(num, 1, row);
            grid.Controls.Add(unit, 2, row);
            grid.Controls.Add(Centered(eq), 3, row);
        }

        static void ConfigureNum(NumericUpDown num, bool percent, bool isProcess)
        {
            num.DecimalPlaces = percent ? 0 : 1;
            num.Increment = percent ? 1m : 0.5m;
            num.Minimum = isProcess ? (percent ? 1m : 0.1m) : 0m;
            num.Maximum = percent ? (isProcess ? 100m : 90m) : 1024m;
        }

        // unit "MiB" shows GB; "%" shows a share of the card.
        void SetAmount(NumericUpDown num, ComboBox unit, string kind, double value, bool isProcess)
        {
            bool was = updating;
            updating = true;
            bool percent = kind == "%";
            ConfigureNum(num, percent, isProcess);
            unit.SelectedIndex = percent ? 1 : 0;
            decimal v = (decimal)Math.Round(percent ? value : value / 1024.0, percent ? 0 : 1);
            num.Value = Math.Max(num.Minimum, Math.Min(num.Maximum, v));
            updating = was;
            UpdateEquivalents();
        }

        // The same threshold in the other unit, e.g. "≈ 17% of 11.7 GB" or "≈ 1.8 GB".
        void UpdateEquivalents()
        {
            eqProc.Text = Equivalent(numProc, unitProc);
            eqFree.Text = Equivalent(numFree, unitFree);
        }

        string Equivalent(NumericUpDown num, ComboBox unit)
        {
            if (totalMiB <= 0) return unit.SelectedIndex == 1 ? L.T("（讀不到這張卡的總量）", "(card size unknown)") : "";
            double v = (double)num.Value;
            if (unit.SelectedIndex == 1) return "≈ " + Gateway.Gb(totalMiB * v / 100);
            return "≈ " + Math.Round(v * 1024 * 100 / totalMiB) + "%" + L.T("（共 " + Gateway.Gb(totalMiB) + "）", " of " + Gateway.Gb(totalMiB));
        }

        string ParamOf(NumericUpDown num, ComboBox unit)
        {
            if (unit.SelectedIndex == 1) return Uri.EscapeDataString(((int)num.Value).ToString(CultureInfo.InvariantCulture) + "%");
            return ((int)Math.Round((double)num.Value * 1024)).ToString(CultureInfo.InvariantCulture);
        }

        void Apply()
        {
            Dictionary<string, object> r = call("POST", "/control/vram-guard-settings?process=" + ParamOf(numProc, unitProc)
                + "&minFree=" + ParamOf(numFree, unitFree) + "&resume=" + ((int)numResume.Value).ToString(CultureInfo.InvariantCulture)
                + "&lang=" + L.Code);
            lblSaved.ForeColor = r != null ? Color.FromArgb(34, 130, 70) : Color.FromArgb(190, 60, 40);
            lblSaved.Text = r != null ? L.T("已套用", "Applied") : L.T("沒有套用：閘道沒有回應", "Not applied: the gateway does not respond");
            RefreshData();
        }

        void RefreshData()
        {
            Dictionary<string, object> d = call("GET", "/control/vram?lang=" + L.Code);
            if (d == null)
            {
                lblState.ForeColor = Color.FromArgb(190, 60, 40);
                lblState.Text = L.T("閘道沒有回應。", "The gateway does not respond.");
                return;
            }
            updating = true;
            try
            {
                bool enabled = Gateway.Bool(d, "enabled");
                chkEnabled.Checked = enabled;
                Dictionary<string, object> card = Gateway.Obj(d, "card");
                totalMiB = Gateway.Num(card, "totalMiB");
                double used = Gateway.Num(card, "usedMiB");
                string cardName = Gateway.Str(card, "name") ?? L.T("（找不到顯示卡）", "(no graphics card found)");
                lblCard.Text = totalMiB > 0
                    ? cardName + L.T("｜已用 ", " | used ") + Gateway.Gb(used) + " / " + Gateway.Gb(totalMiB) + L.T("，剩 ", ", free ") + Gateway.Gb(totalMiB - used)
                    : cardName + L.T("｜讀不到 VRAM 總量", " | VRAM size unknown");
                bar.Value = totalMiB > 0 ? (int)Math.Max(0, Math.Min(1000, used * 1000 / totalMiB)) : 0;

                bool backedOff = Gateway.Bool(d, "backedOff");
                lblState.ForeColor = backedOff ? Color.FromArgb(190, 60, 40) : SystemColors.ControlText;
                lblState.Text = !enabled ? L.T("自動讓出已關閉，下面的條件暫不生效。", "Automatic yielding is off; the conditions below are not applied.")
                    : Gateway.Bool(card, "integrated") ? L.T("模型在內顯上，用的是系統記憶體，不會跟遊戲搶 VRAM，所以不讓出。",
                        "The model is on an integrated GPU and uses system RAM, so it never competes for VRAM and does not yield.")
                    : backedOff ? L.T("已讓出 GPU：" + Gateway.Str(d, "reason") + "；聽寫暫用 CPU。", "GPU yielded: " + Gateway.Str(d, "reason") + "; dictation uses the CPU meanwhile.")
                    : L.T("監看中：條件成立時，模型會讓出這張卡。", "Watching: the model gives this card up when a condition below is met.");
                if (!Gateway.Bool(d, "sampled")) lblState.Text += L.T("（讀取各程式用量中…）", " (reading per-program use…)");

                FillList(d);
                if (!settingsLoaded)
                {
                    Dictionary<string, object> p = Gateway.Obj(d, "process"), f = Gateway.Obj(d, "minFree");
                    SetAmount(numProc, unitProc, Gateway.Str(p, "unit"), Gateway.Num(p, "value"), true);
                    SetAmount(numFree, unitFree, Gateway.Str(f, "unit"), Gateway.Num(f, "value"), false);
                    numResume.Value = (decimal)Math.Max(5, Math.Min(3600, Gateway.Num(d, "resumeSeconds")));
                    settingsLoaded = true;
                }
                UpdateEquivalents();
            }
            finally { updating = false; }
        }

        void FillList(Dictionary<string, object> d)
        {
            int top = list.TopItem != null ? list.TopItem.Index : 0;
            string selected = list.SelectedItems.Count > 0 ? ((string[])list.SelectedItems[0].Tag)[0] + "|" + list.SelectedItems[0].SubItems[1].Text : null;
            List<string> running = new List<string>();
            list.BeginUpdate();
            list.Items.Clear();
            foreach (object o in Gateway.List(d, "processes"))
            {
                Dictionary<string, object> p = (Dictionary<string, object>)o;
                string name = Gateway.Str(p, "name"), kind = Gateway.Str(p, "kind") ?? "";
                double mib = Gateway.Num(p, "mib");
                running.Add(name.ToLowerInvariant());
                string share = totalMiB > 0 ? Math.Round(mib * 100 / totalMiB) + "%" : "";
                string vram = mib >= 1024 ? Gateway.Gb(mib) : Math.Round(mib) + " MB";
                AddItem(name, kind, Convert.ToString(Gateway.Num(p, "pid")), vram, share);
            }
            // Whitelisted programs that are not running stay visible, so they can be removed.
            foreach (object o in Gateway.List(d, "whitelist"))
            {
                string name = Convert.ToString(o);
                if (!running.Contains(name.ToLowerInvariant())) AddItem(name, "idle", "", "", "");
            }
            list.EndUpdate();
            if (list.Items.Count > 0) list.TopItem = list.Items[Math.Min(top, list.Items.Count - 1)];
            if (selected != null)
                foreach (ListViewItem item in list.Items)
                    if (((string[])item.Tag)[0] + "|" + item.SubItems[1].Text == selected) { item.Selected = true; break; }
        }

        void AddItem(string name, string kind, string pid, string vram, string share)
        {
            string status = kind == "ours" ? L.T("本閘道的模型（不計入）", "This gateway's model (not counted)")
                          : kind == "system" ? L.T("Windows 系統（不計入）", "Windows itself (not counted)")
                          : kind == "whitelist" ? L.T("白名單", "Whitelisted")
                          : kind == "idle" ? L.T("白名單（未執行）", "Whitelisted (not running)")
                          : kind == "trigger" ? L.T("觸發了讓出", "Caused the yield")
                          : kind == "over" ? L.T("超過門檻", "Above the threshold")
                          : "";
            ListViewItem item = new ListViewItem(new string[] { name, pid, vram, share, status });
            item.Tag = new string[] { name, kind };
            item.Checked = kind == "whitelist" || kind == "idle";
            if (kind == "ours" || kind == "system" || kind == "idle") item.ForeColor = SystemColors.GrayText;
            else if (kind == "trigger") item.ForeColor = Color.FromArgb(190, 60, 40);
            else if (kind == "over") item.ForeColor = Color.FromArgb(180, 100, 0);
            list.Items.Add(item);
        }

        // Layout check data for --render-guard --sample (made-up programs and numbers).
        internal const string SampleJson = "{\"enabled\":true,\"backedOff\":true,\"reason\":\"Cyberpunk2077 5.1 GB\",\"sampled\":true," +
            "\"card\":{\"name\":\"NVIDIA GeForce RTX 5070 Ti Laptop GPU\",\"totalMiB\":11944,\"usedMiB\":8130}," +
            "\"processes\":[{\"pid\":14820,\"name\":\"Cyberpunk2077\",\"mib\":5243,\"kind\":\"trigger\"}," +
            "{\"pid\":9312,\"name\":\"obs64\",\"mib\":1480,\"kind\":\"whitelist\"},{\"pid\":7710,\"name\":\"chrome\",\"mib\":612,\"kind\":\"\"}," +
            "{\"pid\":1404,\"name\":\"dwm\",\"mib\":389,\"kind\":\"system\"}],\"whitelist\":[\"obs64\",\"Discord\"]," +
            "\"process\":{\"unit\":\"%\",\"value\":15,\"mib\":1792},\"minFree\":{\"unit\":\"MiB\",\"value\":1024,\"mib\":1024},\"resumeSeconds\":60}";
    }
}
