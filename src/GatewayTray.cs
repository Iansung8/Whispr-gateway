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

            // Debug aid: render the info window to a PNG (checks layout at the current display scale).
            int render = Array.IndexOf(args, "--render-info");
            if (render >= 0 && render + 1 < args.Length)
            {
                SetProcessDPIAware();
                Application.EnableVisualStyles();
                List<string> hosts = new List<string>();
                hosts.Add("192.168.1.23|乙太網路"); // placeholder addresses, only for this layout check
                hosts.Add("10.0.0.5|ZeroTier One [0123456789abcdef]");
                using (InfoForm form = new InfoForm(8790, hosts, "breeze-asr-25-q8_0", Array.IndexOf(args, "--remote-off") < 0))
                {
                    form.Show();
                    Application.DoEvents();
                    using (Bitmap bmp = new Bitmap(form.Width, form.Height))
                    {
                        form.DrawToBitmap(bmp, new Rectangle(0, 0, form.Width, form.Height));
                        bmp.Save(args[render + 1], System.Drawing.Imaging.ImageFormat.Png);
                    }
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
            pointsHere = xml.IndexOf(System.Security.SecurityElement.Escape(Application.ExecutablePath), StringComparison.OrdinalIgnoreCase) >= 0;
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
                "  <Triggers><LogonTrigger><Enabled>true</Enabled><UserId>" + esc(user) + "</UserId><Delay>PT20S</Delay></LogonTrigger></Triggers>\r\n" +
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

        ToolStripMenuItem miStatus, miDetail, miProblem, miLoad, miUnload, miIdle, miModel, miDevice, miPunct, miConvert, miVocab, miRemote, miAutostart;
        readonly int[] idleChoices = new int[] { 5, 15, 30, 60, 0 };

        Process nodeProc;
        DateTime nodeStartedAt = DateTime.MinValue;
        DateTime restartNotBefore = DateTime.MinValue;
        bool exitHandled = true;
        bool quitting;
        volatile bool autostartOn; // cached: asking Task Scheduler takes a few hundred ms
        Dictionary<string, object> lastStatus;
        InfoForm infoForm;

        public TrayApp(bool quiet)
        {
            hostDir = Path.GetDirectoryName(Application.ExecutablePath);
            gatewayJs = Path.Combine(hostDir, "whisper-gateway.js");
            logFile = Path.Combine(hostDir, "gateway.log");

            // First run: the live config is created from the shipped defaults (the live file is not in git).
            string configPath = Path.Combine(hostDir, "gateway.config.json");
            if (!File.Exists(configPath)) File.Copy(Path.Combine(hostDir, "gateway.config.default.json"), configPath);
            Dictionary<string, object> cfg = json.Deserialize<Dictionary<string, object>>(
                File.ReadAllText(configPath, Encoding.UTF8));
            port = Convert.ToInt32(cfg["port"]);
            // Same rule as the gateway: relative paths are relative to the app folder.
            string models = Environment.ExpandEnvironmentVariables(cfg.ContainsKey("modelsDir") ? Convert.ToString(cfg["modelsDir"]) : "models");
            modelsDir = Path.IsPathRooted(models) ? models : Path.Combine(hostDir, models);

            icons["loaded"] = MakeIcon(Color.FromArgb(34, 160, 80));
            icons["loading"] = MakeIcon(Color.FromArgb(230, 140, 20));
            icons["unloaded"] = MakeIcon(Color.FromArgb(120, 120, 120));
            icons["down"] = MakeIcon(Color.FromArgb(200, 50, 50));

            BuildMenu();
            notify.Icon = icons["down"];
            notify.Text = "語音辨識閘道：啟動中";
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
                notify.ShowBalloonTip(6000, "語音辨識閘道已啟動",
                    "點這裡或圖示選單的「OpenWhispr 要填什麼」查看端點網址。", ToolTipIcon.Info);
            }
        }

        // ---------------------------------------------------------------- menu
        void BuildMenu()
        {
            miStatus = new ToolStripMenuItem("狀態：啟動中"); miStatus.Enabled = false;
            miDetail = new ToolStripMenuItem(" "); miDetail.Enabled = false;
            miProblem = new ToolStripMenuItem(" "); miProblem.ForeColor = Color.FromArgb(190, 60, 40); miProblem.Visible = false;
            miProblem.Click += delegate
            {
                string readme = Path.Combine(hostDir, "README.md");
                if (File.Exists(readme)) Process.Start("notepad.exe", "\"" + readme + "\"");
            };
            miDevice = new ToolStripMenuItem("運算裝置");
            miPunct = new ToolStripMenuItem("自動補標點（依停頓切句，whisper 模型）");
            miPunct.Click += delegate
            {
                Call("POST", "/control/punctuation?enabled=" + (miPunct.Checked ? "false" : "true"));
                Refresh();
            };
            miConvert = new ToolStripMenuItem("簡體輸出轉台灣繁體（Qwen3-ASR 系列）");
            miConvert.Click += delegate
            {
                Call("POST", "/control/convert?enabled=" + (miConvert.Checked ? "false" : "true"));
                Refresh();
            };
            // Both files are plain text the gateway re-reads on the next request (no restart needed).
            miVocab = new ToolStripMenuItem("詞彙（Qwen3-ASR 系列）");
            ToolStripMenuItem miVocabEdit = new ToolStripMenuItem("編輯詞彙提示（英文術語、人名、專有名詞）…");
            ToolStripMenuItem miLexEdit = new ToolStripMenuItem("編輯台灣用詞替換表…");
            miVocabEdit.Click += delegate { OpenDataFile("vocabularyFile"); };
            miLexEdit.Click += delegate { OpenDataFile("lexiconFile"); };
            miVocab.DropDownItems.Add(miVocabEdit);
            miVocab.DropDownItems.Add(miLexEdit);
            miLoad = new ToolStripMenuItem("立即載入模型（佔用 GPU）");
            miUnload = new ToolStripMenuItem("立即卸載模型（釋放 GPU）");
            miIdle = new ToolStripMenuItem("閒置自動卸載");
            miModel = new ToolStripMenuItem("模型");
            miRemote = new ToolStripMenuItem("允許其他電腦連線（區域網路／VPN）");
            miAutostart = new ToolStripMenuItem("開機（登入）時自動啟動");
            ToolStripMenuItem miInfo = new ToolStripMenuItem("OpenWhispr 要填什麼…");
            ToolStripMenuItem miLog = new ToolStripMenuItem("開啟記錄檔");
            ToolStripMenuItem miRestart = new ToolStripMenuItem("重新啟動閘道");
            ToolStripMenuItem miQuit = new ToolStripMenuItem("結束（停止閘道並釋放 GPU）");

            foreach (int minutes in idleChoices)
            {
                ToolStripMenuItem item = new ToolStripMenuItem(minutes == 0 ? "不自動卸載（只手動）" : minutes + " 分鐘");
                item.Tag = minutes;
                item.Click += delegate(object s, EventArgs e)
                {
                    Call("POST", "/control/idle-minutes?value=" + ((ToolStripMenuItem)s).Tag);
                    Refresh();
                };
                miIdle.DropDownItems.Add(item);
            }

            miLoad.Click += delegate { Call("POST", "/control/load"); Refresh(); };
            miUnload.Click += delegate { Call("POST", "/control/unload"); Refresh(); };
            miRemote.Click += delegate
            {
                Call("POST", "/control/remote?enabled=" + (miRemote.Checked ? "false" : "true"));
                Refresh();
            };
            miAutostart.Click += delegate
            {
                bool target = !autostartOn;
                if (Autostart.Set(target)) autostartOn = target;
                else notify.ShowBalloonTip(6000, "語音辨識閘道", "無法變更自動啟動設定（工作排程器拒絕）。", ToolTipIcon.Warning);
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
                miIdle, miModel, miDevice, miPunct, miConvert, miVocab, miRemote, new ToolStripSeparator(),
                miInfo, miAutostart, new ToolStripSeparator(),
                miLog, miRestart, miQuit });
            menu.Opening += delegate { Refresh(); RebuildModelMenu(); RebuildDeviceMenu(); miAutostart.Checked = autostartOn; };
        }

        // Devices come from the gateway: NVIDIA GPUs (CUDA engine), Vulkan if that engine is installed, CPU.
        void RebuildDeviceMenu()
        {
            miDevice.DropDownItems.Clear();
            if (lastStatus == null || !lastStatus.ContainsKey("devices")) return;
            string configured = Convert.ToString(lastStatus["device"]);
            string active = Convert.ToString(lastStatus["activeDevice"]);
            foreach (object o in (IEnumerable)lastStatus["devices"])
            {
                Dictionary<string, object> d = (Dictionary<string, object>)o;
                string id = Convert.ToString(d["id"]);
                string text = id == "cpu" ? "純 CPU（慢，不佔 GPU）"
                            : id == "vulkan" ? "Vulkan（任何廠牌 GPU，自動選擇）"
                            : "GPU " + id.Substring(4) + "：" + d["name"] + (d["memoryMiB"] != null ? "（" + Math.Round(Convert.ToDouble(d["memoryMiB"]) / 1024.0) + " GB）" : "");
                ToolStripMenuItem item = new ToolStripMenuItem(text);
                item.Tag = id;
                item.Checked = id == configured;
                item.Click += delegate(object s, EventArgs e)
                {
                    Call("POST", "/control/device?value=" + Uri.EscapeDataString((string)((ToolStripMenuItem)s).Tag));
                    Refresh();
                };
                miDevice.DropDownItems.Add(item);
            }
            if (active != configured)
            {
                miDevice.DropDownItems.Add(new ToolStripSeparator());
                ToolStripMenuItem note = new ToolStripMenuItem("目前實際使用：" + (active == "cpu" ? "CPU" : active));
                note.Enabled = false;
                miDevice.DropDownItems.Add(note);
            }
        }

        // Opens one of the gateway's editable text files (paths come from /control/status).
        void OpenDataFile(string key)
        {
            string file = lastStatus != null && lastStatus.ContainsKey(key) ? Convert.ToString(lastStatus[key]) : null;
            if (string.IsNullOrEmpty(file) || !File.Exists(file))
            {
                notify.ShowBalloonTip(5000, "語音辨識閘道", "檔案還沒建立：閘道啟動後會自動產生，請稍後再試。", ToolTipIcon.Info);
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
                        Call("POST", "/control/model?file=" + Uri.EscapeDataString((string)((ToolStripMenuItem)s).Tag));
                        Refresh();
                    };
                    miModel.DropDownItems.Add(item);
                }
            }
            if (miModel.DropDownItems.Count > 0) miModel.DropDownItems.Add(new ToolStripSeparator());
            ToolStripMenuItem open = new ToolStripMenuItem("開啟模型資料夾（放入 ggml .bin，或 GGUF＋mmproj，即可切換）");
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
            lastStatus = Call("GET", "/control/status");
            if (lastStatus == null)
            {
                notify.Icon = icons["down"];
                SetTip("語音辨識閘道：未回應");
                miStatus.Text = "狀態：閘道未回應";
                miDetail.Text = " ";
                miProblem.Visible = false;
                miLoad.Enabled = miUnload.Enabled = miIdle.Enabled = miModel.Enabled = miDevice.Enabled = miPunct.Enabled = miConvert.Enabled = miVocab.Enabled = miRemote.Enabled = false;
                return;
            }

            // First missing dependency / fallback, in the gateway's own words; click opens the README.
            string problem = null;
            if (lastStatus.ContainsKey("problems"))
                foreach (object p in (IEnumerable)lastStatus["problems"]) { problem = Convert.ToString(p); break; }
            miProblem.Visible = problem != null;
            if (problem != null) miProblem.Text = "⚠ " + problem;

            string backend = Convert.ToString(lastStatus["backend"]);
            int idle = Convert.ToInt32(lastStatus["idleMinutes"]);
            int inFlight = Convert.ToInt32(lastStatus["inFlight"]);
            bool allowRemote = Convert.ToBoolean(lastStatus["allowRemote"]);
            string active = lastStatus.ContainsKey("activeDevice") ? Convert.ToString(lastStatus["activeDevice"]) : "";
            string where = active == "cpu" ? "CPU" : active == "vulkan" ? "Vulkan GPU" : active.StartsWith("gpu:") ? "GPU " + active.Substring(4) : "GPU";
            string label = backend == "loaded" ? "模型已載入（" + where + " 使用中）"
                         : backend == "loading" ? "模型載入中…"
                         : "模型未載入（不佔資源）";

            notify.Icon = icons.ContainsKey(backend) ? icons[backend] : icons["unloaded"];
            SetTip("語音辨識閘道：" + label);
            miStatus.Text = "狀態：" + label;

            string engineName = lastStatus.ContainsKey("engine") && Convert.ToString(lastStatus["engine"]) == "llama" ? "llama.cpp" : "whisper.cpp";
            string detail = engineName + "｜" + (idle > 0 ? "閒置 " + idle + " 分鐘自動卸載" : "不自動卸載");
            if (lastStatus["unloadAt"] != null)
            {
                double leftMs = Convert.ToDouble(lastStatus["unloadAt"]) - (DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalMilliseconds;
                detail += "，約 " + Math.Max(0, (int)Math.Ceiling(leftMs / 60000.0)) + " 分鐘後卸載";
            }
            if (inFlight > 0) detail = "辨識進行中…";
            Dictionary<string, object> client = lastStatus.ContainsKey("lastClient") ? lastStatus["lastClient"] as Dictionary<string, object> : null;
            if (client != null && inFlight == 0)
            {
                double agoMin = ((DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalMilliseconds - Convert.ToDouble(client["at"])) / 60000.0;
                detail += "｜外部 " + client["ip"] + "（" + (int)agoMin + " 分鐘前）";
            }
            miDetail.Text = detail;

            miIdle.Enabled = miModel.Enabled = miDevice.Enabled = miPunct.Enabled = miConvert.Enabled = miVocab.Enabled = miRemote.Enabled = true;
            miPunct.Checked = lastStatus.ContainsKey("punctuation") && Convert.ToBoolean(lastStatus["punctuation"]);
            miConvert.Checked = lastStatus.ContainsKey("convertSimplified") && Convert.ToBoolean(lastStatus["convertSimplified"]);
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
                if (lastStatus != null && lastStatus.ContainsKey("addresses"))
                    foreach (object o in (IEnumerable)lastStatus["addresses"])
                    {
                        Dictionary<string, object> a = (Dictionary<string, object>)o;
                        remoteHosts.Add(Convert.ToString(a["address"]) + "|" + Convert.ToString(a["name"]));
                    }
                infoForm = new InfoForm(port, remoteHosts, model, allowRemote);
            }
            infoForm.Show();
            infoForm.Activate();
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
                notify.ShowBalloonTip(8000, "語音辨識閘道", "找不到 Node.js：請安裝 Node.js 18 以上（nodejs.org），或把 node.exe 放進 runtime 資料夾。", ToolTipIcon.Error);
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
            // /T also takes down whisper-server (a child of node), which is what releases the GPU.
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

        Dictionary<string, object> Call(string method, string path)
        {
            try
            {
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create("http://127.0.0.1:" + port + path);
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

        static Icon MakeIcon(Color color)
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

    // "What do I type into OpenWhispr?" - endpoint URLs with copy buttons.
    class InfoForm : Form
    {
        readonly List<Control> remoteControls = new List<Control>();
        readonly Label remoteNote = new Label();

        public InfoForm(int port, List<string> remoteHosts, string model, bool allowRemote)
        {
            Text = "OpenWhispr 要填什麼";
            Font = new Font("Microsoft JhengHei UI", 10f);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            // The process is DPI aware and fonts are in points, so text already scales. Pixel sizes are
            // scaled by hand and the window sizes itself to its content - a fixed ClientSize clipped
            // the lower rows on a 150% display.
            AutoScaleMode = AutoScaleMode.None;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            using (Graphics g = CreateGraphics()) scale = g.DpiX / 96f;

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

            Label steps = new Label();
            steps.AutoSize = true;
            steps.MaximumSize = new Size(Px(640), 0);
            steps.Margin = new Padding(3, 3, 3, Px(12));
            steps.Text =
                "1. OpenWhispr → 設定 → 語音轉文字，模式選「自架主機」。\r\n" +
                "2. 「端點 URL」填下面對應的網址（其他電腦請挑它連得到的那個網段）；API Key 留空，模型名稱可留空。\r\n" +
                "3. 偏好設定 → 轉錄語言 選「中文（繁體）」。\r\n" +
                "第一句會多等約 2 秒（模型載入 GPU），之後每句約 1 秒；閒置後自動卸載。";
            root.Controls.Add(steps);

            int row = 0;
            AddRow(table, row++, "這台電腦：", "http://127.0.0.1:" + port + "/v1", null);
            foreach (string entry in remoteHosts) // "address|adapter name"
            {
                string[] parts = entry.Split(new char[] { '|' }, 2);
                string adapter = parts.Length > 1 && parts[1].Length > 0 ? parts[1] : "其他電腦";
                if (adapter.Length > 22) adapter = adapter.Substring(0, 21) + "…";
                AddRow(table, row++, "其他電腦（" + adapter + "）：", "http://" + parts[0] + ":" + port + "/v1", remoteControls);
            }
            AddRow(table, row++, "模型名稱（選填）：", model, null);

            root.Controls.Add(table);

            remoteNote.AutoSize = true;
            remoteNote.MaximumSize = new Size(Px(640), 0);
            remoteNote.ForeColor = Color.FromArgb(190, 60, 40);
            remoteNote.Margin = new Padding(3, Px(8), 3, 3);
            remoteNote.Text = "目前已關閉外部連線：其他電腦連不進來（可在圖示選單重新開啟）。";
            root.Controls.Add(remoteNote);

            Controls.Add(root);
            SetRemoteEnabled(allowRemote);
        }

        float scale = 1f;
        int Px(int logical) { return (int)Math.Round(logical * scale); }

        public void SetRemoteEnabled(bool enabled)
        {
            foreach (Control c in remoteControls) c.Enabled = enabled;
            remoteNote.Visible = !enabled;
        }

        void AddRow(TableLayoutPanel table, int row, string caption, string value, List<Control> track)
        {
            Label label = new Label();
            label.Text = caption;
            label.AutoSize = true;
            label.Anchor = AnchorStyles.Left;

            TextBox box = new TextBox();
            box.Text = value;
            box.ReadOnly = true;
            box.TabStop = false; // keeps the first URL from opening pre-selected
            box.Width = Px(360);
            box.Anchor = AnchorStyles.Left;
            box.Margin = new Padding(3, Px(5), 3, Px(5));

            Button copy = new Button();
            copy.Text = "複製";
            copy.AutoSize = true;
            copy.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            copy.Anchor = AnchorStyles.Left;
            copy.Click += delegate
            {
                try { Clipboard.SetText(box.Text); copy.Text = "已複製"; } catch { }
            };

            table.Controls.Add(label, 0, row);
            table.Controls.Add(box, 1, row);
            table.Controls.Add(copy, 2, row);
            if (track != null) { track.Add(box); track.Add(copy); }
        }
    }
}
