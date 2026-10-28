// ============================================================
//  抖音浏览器 —— 独立无边框浏览器（WebView2 内核）
//  打开即进入抖音网页版 https://www.douyin.com
//  无边框、无标签栏、无菜单、无任何按钮
//  顶部约 20 像素为隐形拖拽区，窗口边缘可拉伸
//  F11 切换全屏，Alt+F4 / Ctrl+W 关闭
// ============================================================

using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

// 程序集信息（决定任务管理器 / 文件属性中显示的进程名称）
[assembly: AssemblyTitle("抖窗 (TokFrame)")]
[assembly: AssemblyProduct("抖窗 (TokFrame)")]
[assembly: AssemblyDescription("抖音网页版无边框浏览器")]
[assembly: AssemblyCompany("TokFrame")]
[assembly: AssemblyFileVersion("1.0.0.0")]
[assembly: AssemblyVersion("1.0.0.0")]

namespace DouyinBrowser
{
    internal static class Program
    {
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr LoadLibrary(string lpFileName);

        [STAThread]
        private static void Main()
        {
            try
            {
                BootstrapEmbeddedResources(); // 单文件运行：解包内嵌 DLL
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.ThreadException += delegate(object s, ThreadExceptionEventArgs ex) { LogError(ex.Exception); };
                AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs ex) { LogError(ex.ExceptionObject as Exception); };
                Application.Run(new BrowserForm());
            }
            catch (Exception ex)
            {
                LogError(ex);
            }
        }

        // ---------- 单文件运行支撑：所有 DLL 已嵌入 EXE，运行时按需解包 ----------
        private static void BootstrapEmbeddedResources()
        {
            // 1) 原生 WebView2Loader.dll：解压到用户数据目录并预加载，
            //    之后 WebView2 内部的 DllImport("WebView2Loader.dll") 会直接命中已加载模块
            try
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "DouyinBrowser");
                Directory.CreateDirectory(dir);
                string loaderPath = Path.Combine(dir, "WebView2Loader.dll");

                using (Stream s = Assembly.GetExecutingAssembly()
                    .GetManifestResourceStream("DouyinBrowser.WebView2Loader.dll"))
                {
                    if (s != null)
                    {
                        using (FileStream fs = File.Create(loaderPath))
                        {
                            CopyStream(s, fs);
                        }
                    }
                }
                LoadLibrary(loaderPath);
            }
            catch { }

            // 2) 托管程序集（WebView2 Core / WinForms）：首次用到时从嵌入资源加载
            AppDomain.CurrentDomain.AssemblyResolve += delegate(object sender, ResolveEventArgs args)
            {
                try
                {
                    string name = new AssemblyName(args.Name).Name;
                    string res = null;
                    if (name == "Microsoft.Web.WebView2.Core") res = "DouyinBrowser.WebView2.Core.dll";
                    else if (name == "Microsoft.Web.WebView2.WinForms") res = "DouyinBrowser.WebView2.WinForms.dll";
                    if (res == null) return null;

                    using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream(res))
                    {
                        if (s == null) return null;
                        byte[] bytes = new byte[s.Length];
                        int read = 0;
                        while (read < bytes.Length)
                        {
                            int n = s.Read(bytes, read, bytes.Length - read);
                            if (n <= 0) break;
                            read += n;
                        }
                        return Assembly.Load(bytes);
                    }
                }
                catch { return null; }
            };
        }

        private static void CopyStream(Stream src, Stream dst)
        {
            byte[] buf = new byte[81920];
            int n;
            while ((n = src.Read(buf, 0, buf.Length)) > 0)
            {
                dst.Write(buf, 0, n);
            }
        }

        private static void LogError(Exception ex)
        {
            try
            {
                string p = Path.Combine(Path.GetTempPath(), "douyinbrowser-error.log");
                File.AppendAllText(p,
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss ") + (ex == null ? "unknown error" : ex.ToString()) + Environment.NewLine);
            }
            catch { }
        }
    }

    public class BrowserForm : Form
    {
        private const string HomeUrl = "https://www.douyin.com/";
        private const int TitleBarHeight = 38; // 顶部黑色导航栏高度

        private WebView2 webView;
        private TitleBarPanel titleBar;
        private EdgeStrip leftStrip, rightStrip, bottomStrip;
        private WinButton btnPin, btnMin, btnMax, btnClose;
        private ToolTip pinTip;
        private Mutex singleInstanceMutex;
        private bool fullscreen = false;
        private bool manualMax = false;
        private Rectangle restoreBounds;
        private DateTime lastF11 = DateTime.MinValue;

        [DllImport("user32.dll")]
        private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);
        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        public BrowserForm()
        {
            // 单实例：重复启动只唤醒已打开窗口
            bool createdNew;
            singleInstanceMutex = new Mutex(true, @"Local\DouyinBrowser_SingleInstance_v1", out createdNew);
            if (!createdNew)
            {
                IntPtr hwnd = FindWindow(null, "抖音浏览器");
                if (hwnd != IntPtr.Zero)
                {
                    ShowWindow(hwnd, 9); // SW_RESTORE
                    SetForegroundWindow(hwnd);
                }
                Environment.Exit(0);
            }

            Text = "抖音浏览器";
            FormBorderStyle = FormBorderStyle.None;   // ★ 无边框
            BackColor = Color.FromArgb(22, 24, 35);   // 与抖音深色一致，避免启动白屏
            StartPosition = FormStartPosition.Manual;
            MinimumSize = new Size(960, 600);
            LoadBounds();

            webView = new WebView2();
            webView.Dock = DockStyle.None;
            webView.DefaultBackgroundColor = Color.FromArgb(22, 24, 35);
            Controls.Add(webView);

            // 边缘细条（仅 8px，不遮挡页面点击）：
            // 左/右/下三条负责拉伸；底部细条的两端兼作下边角
            leftStrip = new EdgeStrip(EdgeStrip.Kind.Left);
            rightStrip = new EdgeStrip(EdgeStrip.Kind.Right);
            bottomStrip = new EdgeStrip(EdgeStrip.Kind.Bottom);
            leftStrip.Dock = DockStyle.None;
            rightStrip.Dock = DockStyle.None;
            bottomStrip.Dock = DockStyle.None;
            Controls.Add(leftStrip);
            Controls.Add(rightStrip);
            Controls.Add(bottomStrip);

            CreateTitleBar();

            // 显式布局：顶栏 → 网页 → 三条边缘细条
            Layout += delegate
            {
                if (webView == null || titleBar == null || leftStrip == null) return;
                int barHeight = titleBar.Visible ? TitleBarHeight : 0;
                int m = HitTestCodes.EdgeMargin;
                Rectangle area = new Rectangle(0, barHeight,
                    ClientSize.Width, ClientSize.Height - barHeight);
                leftStrip.Bounds = new Rectangle(area.Left, area.Top, m, area.Height);
                rightStrip.Bounds = new Rectangle(area.Right - m, area.Top, m, area.Height);
                bottomStrip.Bounds = new Rectangle(area.Left, area.Bottom - m, area.Width, m);
                webView.Bounds = Rectangle.FromLTRB(
                    area.Left + m, area.Top, area.Right - m, area.Bottom - m);
            };

            Load += OnLoad;
            KeyPreview = true;
            KeyDown += OnKeyDown;
            FormClosed += OnFormClosed;
        }

        private async void OnLoad(object sender, EventArgs e)
        {
            try
            {
                ApplyDarkFrame(); // 边框纯黑（与顶栏/抖音深色界面一体）

                string userData = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "DouyinBrowser", "WebView2Data");

                // 允许视频自动播放（抖音体验）
                CoreWebView2EnvironmentOptions options =
                    new CoreWebView2EnvironmentOptions("--autoplay-policy=no-user-gesture-required");
                CoreWebView2Environment env =
                    await CoreWebView2Environment.CreateAsync(null, userData, options);
                await webView.EnsureCoreWebView2Async(env);

                webView.CoreWebView2.Settings.AreDevToolsEnabled = false;
                webView.CoreWebView2.Settings.IsStatusBarEnabled = false;
                webView.CoreWebView2.NewWindowRequested += OnNewWindowRequested;

                // WebView2 原生内容窗口创建后位于最上层，
                // 必须把顶栏与边缘细条重新置顶，边缘拉伸/拖拽才能生效
                // （页面中央区域没有任何窗口遮挡，点击直达网页）。
                RaiseChromeAboveWebView();

                RegisterKeyboardHook();

                webView.CoreWebView2.Navigate(HomeUrl);
                webView.Focus();
            }
            catch (Exception ex)
            {
                MessageBox.Show("浏览器初始化失败：" + ex.Message, "抖音浏览器",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                Close();
            }
        }

        private void RaiseChromeAboveWebView()
        {
            leftStrip.BringToFront();
            rightStrip.BringToFront();
            bottomStrip.BringToFront();
            titleBar.BringToFront();
        }

        // ---------- 快捷键 ----------
        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.F11)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                ToggleFullscreenSafely();
            }
        }

        private void OnAcceleratorKeyPressed(object sender, CoreWebView2AcceleratorKeyPressedEventArgs e)
        {
            if (e.KeyEventKind != CoreWebView2KeyEventKind.KeyDown) return;
            uint vk = e.VirtualKey;
            if (vk == 0x7A) // F11 全屏
            {
                e.Handled = true;
                ToggleFullscreenSafely();
            }
            else if (vk == 0x57 && (Control.ModifierKeys & Keys.Control) == Keys.Control) // Ctrl+W 关闭
            {
                e.Handled = true;
                Close();
            }
        }

        // ---------- 全局键盘钩子（WebView2 不向宿主转发按键，用钩子捕获 F11 / Ctrl+W） ----------
        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
        private LowLevelKeyboardProc hookProc;
        private IntPtr hookId = IntPtr.Zero;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);
        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();
        [DllImport("kernel32.dll")]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const int VK_F11 = 0x7A;
        private const int VK_W = 0x57;
        private const int VK_K = 0x4B;
        private const int VK_L = 0x4C;

        private void RegisterKeyboardHook()
        {
            try
            {
                hookProc = KeyboardHookCallback;
                using (Process cur = Process.GetCurrentProcess())
                using (ProcessModule mod = cur.MainModule)
                {
                    hookId = SetWindowsHookEx(WH_KEYBOARD_LL, hookProc, GetModuleHandle(mod.ModuleName), 0);
                }
            }
            catch { }
        }

        private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int msg = wParam.ToInt32();
                if ((msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN) && GetForegroundWindow() == this.Handle)
                {
                    int vk = Marshal.ReadInt32(lParam);
                    Keys mods = Control.ModifierKeys;
                    if (vk == VK_F11)
                    {
                        ToggleFullscreenSafely();
                        return (IntPtr)1; // 拦截按键
                    }
                    if (vk == VK_W && (mods & Keys.Control) == Keys.Control)
                    {
                        Close();
                        return (IntPtr)1;
                    }
                    if ((mods & Keys.Control) == Keys.Control && (mods & Keys.Shift) == Keys.Shift)
                    {
                        if (vk == VK_K) // Ctrl+Shift+K：清缓存（保留登录）
                        {
                            ClearBrowsingData(false);
                            return (IntPtr)1;
                        }
                        if (vk == VK_L) // Ctrl+Shift+L：清除全部数据（含登录状态）
                        {
                            ClearBrowsingData(true);
                            return (IntPtr)1;
                        }
                    }
                }
            }
            return CallNextHookEx(hookId, nCode, wParam, lParam);
        }

        // ---------- 浏览数据清理（缓存 / 全部） ----------
        private void ClearBrowsingData(bool includeCookies)
        {
            try
            {
                if (webView == null || webView.CoreWebView2 == null) return;

                string doneMessage = includeCookies
                    ? "已清除全部浏览数据（含登录状态），需要重新登录。"
                    : "缓存已清理，登录状态已保留。";

                System.Threading.Tasks.Task clearTask;
                if (includeCookies)
                {
                    clearTask = webView.CoreWebView2.Profile.ClearBrowsingDataAsync(); // 全部清除
                }
                else
                {
                    CoreWebView2BrowsingDataKinds kinds =
                        CoreWebView2BrowsingDataKinds.DiskCache
                        | CoreWebView2BrowsingDataKinds.CacheStorage
                        | CoreWebView2BrowsingDataKinds.ServiceWorkers
                        | CoreWebView2BrowsingDataKinds.FileSystems;
                    clearTask = webView.CoreWebView2.Profile.ClearBrowsingDataAsync(kinds); // 仅缓存
                }

                clearTask.ContinueWith(delegate(System.Threading.Tasks.Task t)
                {
                    bool ok = (t.Exception == null && !t.IsFaulted && !t.IsCanceled);
                    try
                    {
                        BeginInvoke(new Action(delegate
                        {
                            MessageBox.Show(ok ? doneMessage : "清理失败，请稍后再试。",
                                "抖音浏览器", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        }));
                    }
                    catch { }
                });
            }
            catch { }
        }

        private void ToggleFullscreenSafely()
        {
            // 防止 KeyDown 与 AcceleratorKeyPressed 双触发
            if ((DateTime.Now - lastF11).TotalMilliseconds < 400) return;
            lastF11 = DateTime.Now;
            ToggleFullscreen();
        }

        private void ToggleFullscreen()
        {
            if (!fullscreen)
            {
                restoreBounds = Bounds;
                WindowState = FormWindowState.Normal;
                Bounds = Screen.FromControl(this).Bounds; // 覆盖整个屏幕（含任务栏区域）
                titleBar.Visible = false;                 // 全屏时隐藏顶栏（同 Edge）
                leftStrip.Visible = false;
                rightStrip.Visible = false;
                bottomStrip.Visible = false;
                fullscreen = true;
            }
            else
            {
                Bounds = restoreBounds;
                titleBar.Visible = true;
                leftStrip.Visible = true;
                rightStrip.Visible = true;
                bottomStrip.Visible = true;
                RaiseChromeAboveWebView();
                fullscreen = false;
            }
        }

        // ---------- 顶部黑色导航栏（Edge 风格：整条可拖动，右侧四个窗口按钮） ----------
        private void CreateTitleBar()
        {
            titleBar = new TitleBarPanel();
            titleBar.Dock = DockStyle.Top;
            titleBar.Height = TitleBarHeight;
            Controls.Add(titleBar);

            btnPin = new WinButton(WinButton.Kind.Pin);
            btnClose = new WinButton(WinButton.Kind.Close);
            btnMax = new WinButton(WinButton.Kind.Maximize);
            btnMin = new WinButton(WinButton.Kind.Minimize);

            btnPin.Dock = DockStyle.Right;
            btnClose.Dock = DockStyle.Right;
            btnMax.Dock = DockStyle.Right;
            btnMin.Dock = DockStyle.Right;

            // 注意：WinForms 停靠规则是「后添加者先贴边」。
            // 从左到右：置顶 → 最小化 → 最大化 → 关闭（最后添加的 Close 最靠右）。
            titleBar.Controls.Add(btnPin);
            titleBar.Controls.Add(btnMin);
            titleBar.Controls.Add(btnMax);
            titleBar.Controls.Add(btnClose);

            pinTip = new ToolTip();
            pinTip.SetToolTip(btnPin, "窗口置顶（保持最前）");

            btnPin.Click += delegate { TogglePin(); };

            btnMin.Click += delegate
            {
                // 最大化状态下直接最小化会被 WinForms 误处理成「还原」，
                // 因此先还原，再延迟最小化。
                if (manualMax || IsSysMaximized())
                {
                    ManualRestore();
                    try
                    {
                        BeginInvoke(new Action(delegate { WindowState = FormWindowState.Minimized; }));
                    }
                    catch { }
                }
                else
                {
                    WindowState = FormWindowState.Minimized;
                }
            };
            btnMax.Click += delegate { ToggleMaximize(); };
            btnClose.Click += delegate { Close(); };

            // 窗口状态变化时同步最大化状态（含系统贴靠最大化）并刷新图标
            Resize += delegate
            {
                SyncMaximizeState();
            };
        }

        private void TogglePin()
        {
            // 窗口置顶开关：激活时按钮图标变青色
            TopMost = !TopMost;
            btnPin.IsPinned = TopMost;
            btnPin.Invalidate();
        }

        private void ToggleMaximize()
        {
            // 点击事件期间按钮子控件仍持有鼠标捕获，此时直接调用系统
            // 最大化会被窗口管理器忽略；延迟到本次消息处理完成后再执行。
            try
            {
                BeginInvoke(new Action(delegate
                {
                    if (manualMax || IsSysMaximized()) ManualRestore(); else ManualMaximize();
                }));
            }
            catch { }
        }

        private void ManualMaximize()
        {
            if (manualMax || IsSysMaximized()) return;
            restoreBounds = Bounds;   // 记录还原目标（备用）
            manualMax = true;
            SetStripsResize(false);
            RefreshMaxGlyph();
            // 走系统标准最大化命令：由系统计算正确的最大化矩形
            // （WinForms 自行设置 WindowState.Maximized 在无边框下有缺陷）
            Message msg = Message.Create(Handle, 0x0112, (IntPtr)0xF030, IntPtr.Zero);
            DefWndProc(ref msg);
        }

        private void ManualRestore()
        {
            if (!manualMax && !IsSysMaximized()) return;
            manualMax = false;
            SetStripsResize(true);
            RefreshMaxGlyph();
            // 系统标准还原：自动回到最大化前的位置（WINDOWPLACEMENT）
            Message msg = Message.Create(Handle, 0x0112, (IntPtr)0xF120, IntPtr.Zero);
            DefWndProc(ref msg);
        }

        // 系统贴靠（拖到屏幕边缘/顶部）会直接设置 WS_MAXIMIZE 样式位，
        // 在此与手动状态同步，保证还原按钮/双击行为一致。
        private void SyncMaximizeState()
        {
            if (!IsHandleCreated) return;
            bool sysMax = IsSysMaximized();
            if (sysMax != manualMax)
            {
                manualMax = sysMax;
                SetStripsResize(!sysMax);
                RefreshMaxGlyph();
            }
            if (!sysMax)
            {
                restoreBounds = Bounds; // 持续记录最近的非最大化边界（还原目标）
            }
        }

        private void SetStripsResize(bool enabled)
        {
            titleBar.DisableResize = !enabled;
            leftStrip.DisableResize = !enabled;
            rightStrip.DisableResize = !enabled;
            bottomStrip.DisableResize = !enabled;
        }

        private void RefreshMaxGlyph()
        {
            btnMax.IsMaximized = manualMax;
            btnMax.Invalidate();
        }

        // ---------- Snap（Windows 贴靠分屏）支持 ----------
        // 无边框窗口默认没有 WS_THICKFRAME，Windows 判定其不可调整大小，
        // 因而拒绝贴靠。补上该样式位（视觉不变），拖拽即支持 Snap。
        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.Style |= 0x00040000   // WS_THICKFRAME：贴靠、系统边缘缩放
                          | 0x00010000   // WS_MAXIMIZEBOX
                          | 0x00020000;  // WS_MINIMIZEBOX
                return cp;
            }
        }

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
        private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
        private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        // WS_THICKFRAME 带来的窗口边框：系统默认按浅色绘制，会出现白色边条。
        // 通过 DWM 属性把窗口标记为深色模式并把边框颜色设为纯黑，
        // 与黑色顶栏、抖音深色界面融为一体。
        private void ApplyDarkFrame()
        {
            if (!IsHandleCreated) return;
            try
            {
                int dark = 1;
                DwmSetWindowAttribute(Handle, 20, ref dark, 4);  // DWMWA_USE_IMMERSIVE_DARK_MODE
                int black = 0x00000000;                           // COLORREF 纯黑
                DwmSetWindowAttribute(Handle, 34, ref black, 4);  // DWMWA_BORDER_COLOR
            }
            catch { /* Win10 无此属性时忽略 */ }
        }

        private const long WS_MAXIMIZE = 0x01000000;

        [StructLayout(LayoutKind.Sequential)]
        private struct WinPoint { public int x; public int y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MINMAXINFO
        {
            public WinPoint ptReserved;
            public WinPoint ptMaxSize;
            public WinPoint ptMaxPosition;
            public WinPoint ptMinTrackSize;
            public WinPoint ptMaxTrackSize;
        }

        // 修正无边框窗口的最大化边界：
        // 无标题栏窗口的默认 WM_GETMINMAXINFO 会返回虚拟桌面级的错误值，
        // 导致系统最大化/贴靠把窗口甩到副屏或屏幕外。
        // 注意：ptMaxPosition 在 WinForms 管线中是【相对值】，
        // 应用方会自行加上显示器原点 —— 传绝对坐标会被加两次（副屏加倍偏移）。
        private void FixMinMaxInfo(ref Message m)
        {
            base.WndProc(ref m); // 先让 WinForms 处理一遍
            if (m.LParam != IntPtr.Zero)
            {
                MINMAXINFO mmi = (MINMAXINFO)Marshal.PtrToStructure(m.LParam, typeof(MINMAXINFO));
                Screen scr = Screen.FromHandle(Handle);
                Rectangle wa = scr.WorkingArea;
                mmi.ptMaxPosition.x = -8;                // 相对值（标准边框补偿）
                mmi.ptMaxPosition.y = -8;
                mmi.ptMaxSize.x = wa.Width + 16;         // 尺寸为绝对值
                mmi.ptMaxSize.y = wa.Height + 16;
                mmi.ptMaxTrackSize.x = wa.Width + 16;    // 拖动跟踪上限
                mmi.ptMaxTrackSize.y = wa.Height + 16;
                Marshal.StructureToPtr(mmi, m.LParam, false);
            }
        }

        private bool IsSysMaximized()
        {
            if (!IsHandleCreated) return false;
            return (GetWindowLongPtr(Handle, -16).ToInt64() & WS_MAXIMIZE) != 0;
        }

        private void SetSysMaximized(bool max)
        {
            if (!IsHandleCreated) return;
            long style = GetWindowLongPtr(Handle, -16).ToInt64();
            if (max) style |= WS_MAXIMIZE; else style &= ~WS_MAXIMIZE;
            SetWindowLongPtr(Handle, -16, new IntPtr(style));
            SetWindowPos(Handle, IntPtr.Zero, 0, 0, 0, 0,
                0x0002 | 0x0004 | 0x0001 | 0x0010); // NOMOVE|NOSIZE|NOZORDER|NOACTIVATE + FRAMECHANGED
        }

        // 拦截系统最大化/还原命令（双击顶栏、Win+↑ 等），改走手动路径
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0024) // WM_GETMINMAXINFO：修正最大化边界
            {
                FixMinMaxInfo(ref m);
                return;
            }
            if (m.Msg == 0x0112) // WM_SYSCOMMAND
            {
                int cmd = (int)(m.WParam.ToInt64() & 0xFFF0);
                if (cmd == 0xF030) // SC_MAXIMIZE：双击顶栏 / Win+↑ → 切换式
                {
                    if (WindowState == FormWindowState.Minimized)
                    {
                        WindowState = FormWindowState.Normal; // 先还原最小化状态
                    }
                    if (manualMax || IsSysMaximized()) ManualRestore(); else ManualMaximize();
                    return;
                }
                if (cmd == 0xF120) // SC_RESTORE：任务栏点击还原 / Win+↓
                {
                    if (manualMax || IsSysMaximized())
                    {
                        ManualRestore();
                        return;
                    }
                    if (WindowState == FormWindowState.Minimized)
                    {
                        WindowState = FormWindowState.Normal; // 从最小化还原（任务栏点击）
                    }
                    return;
                }
            }
            if (m.Msg == 0x00A1 && (manualMax || IsSysMaximized())) // 最大化状态下拖动顶栏：先还原（同 Edge 行为）
            {
                ManualRestore();
            }
            base.WndProc(ref m);
        }

        // ---------- 新窗口处理 ----------
        private void OnNewWindowRequested(object sender, CoreWebView2NewWindowRequestedEventArgs e)
        {
            e.Handled = true;
            string url = e.Uri ?? string.Empty;
            if (IsDouyinUrl(url))
            {
                webView.CoreWebView2.Navigate(url);   // 抖音域内：本窗口打开
            }
            else
            {
                try { System.Diagnostics.Process.Start(url); } catch { } // 其它：系统浏览器
            }
        }

        private static bool IsDouyinUrl(string url)
        {
            try
            {
                Uri uri = new Uri(url);
                string host = uri.Host.ToLowerInvariant();
                return host == "douyin.com" || host.EndsWith(".douyin.com") ||
                       host == "iesdouyin.com" || host.EndsWith(".iesdouyin.com");
            }
            catch { return false; }
        }

        // ---------- 窗口位置记忆 ----------
        private string StateFile
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "DouyinBrowser", "state.txt");
            }
        }

        private void LoadBounds()
        {
            try
            {
                if (File.Exists(StateFile))
                {
                    string[] parts = File.ReadAllText(StateFile).Trim().Split(',');
                    if (parts.Length == 4)
                    {
                        int x = int.Parse(parts[0]);
                        int y = int.Parse(parts[1]);
                        int w = int.Parse(parts[2]);
                        int h = int.Parse(parts[3]);
                        Rectangle r = new Rectangle(x, y, w, h);
                        bool onScreen = false;
                        foreach (Screen scr in Screen.AllScreens)
                        {
                            if (scr.WorkingArea.IntersectsWith(r)) { onScreen = true; break; }
                        }
                        if (onScreen && w >= 960 && h >= 600)
                        {
                            Bounds = r;
                            return;
                        }
                    }
                }
            }
            catch { }
            Bounds = new Rectangle(120, 120, 1280, 800);
        }

        private void OnFormClosed(object sender, FormClosedEventArgs e)
        {
            try
            {
                if (!fullscreen && !manualMax)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(StateFile));
                    File.WriteAllText(StateFile,
                        string.Format("{0},{1},{2},{3}", Left, Top, Width, Height));
                }
            }
            catch { }
            if (hookId != IntPtr.Zero)
            {
                try { UnhookWindowsHookEx(hookId); } catch { }
                hookId = IntPtr.Zero;
            }
            if (webView != null) { webView.Dispose(); }
        }
    }

    // ============================================================
    //  命中测试常量（WM_NCHITTEST 返回值）
    // ============================================================
    internal static class HitTestCodes
    {
        public const int WM_NCHITTEST = 0x84;
        public const int HTCLIENT = 1;
        public const int HTCAPTION = 2;
        public const int HTLEFT = 10;
        public const int HTRIGHT = 11;
        public const int HTTOP = 12;
        public const int HTTOPLEFT = 13;
        public const int HTTOPRIGHT = 14;
        public const int HTBOTTOM = 15;
        public const int HTBOTTOMLEFT = 16;
        public const int HTBOTTOMRIGHT = 17;
        public const int HTTRANSPARENT = -1;
        public const int EdgeMargin = 8;
    }

    // ============================================================
    //  顶部黑色导航栏面板（带一条很淡的底边分隔线）
    //  整条栏本身充当拖拽区：WM_NCHITTEST 返回 HTCAPTION，
    //  顶部 8px 与四角返回拉伸代码，按钮区域由按钮自身响应。
    // ============================================================
    public class TitleBarPanel : Panel
    {
        // 最大化时禁用边缘拉伸（仅保留拖拽）
        public bool DisableResize = false;

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();
        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

        public TitleBarPanel()
        {
            BackColor = Color.Black;
            DoubleBuffered = true;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (Pen pen = new Pen(Color.FromArgb(46, 46, 46), 1f))
            {
                e.Graphics.DrawLine(pen, 0, Height - 1, Width, Height - 1);
            }
        }

        protected override void WndProc(ref Message m)
        {
            // 关键：顶栏是子窗口，若让系统直接处理拖拽/双击，它会移动子面板本身。
            // 必须转发给顶层窗体，整个窗口（含网页）才会一起动。
            if (m.Msg == 0x00A1 || m.Msg == 0x00A3 || m.Msg == 0x00A4)
            {
                // WM_NCLBUTTONDOWN / WM_NCLBUTTONDBLCLK / WM_NCRBUTTONDOWN
                ReleaseCapture();
                SendMessage(Parent.Handle, m.Msg, m.WParam, m.LParam);
                return;
            }

            base.WndProc(ref m);
            if (m.Msg != HitTestCodes.WM_NCHITTEST) return;

            long lp = m.LParam.ToInt64();
            int mx = (short)(lp & 0xFFFF);
            int my = (short)((lp >> 16) & 0xFFFF);
            Point p = PointToClient(new Point(mx, my));

            int r = HitTestCodes.HTCAPTION; // 整条顶栏 = 拖拽区
            if (!DisableResize)
            {
                int mgn = HitTestCodes.EdgeMargin;
                bool left = p.X <= mgn, right = p.X >= Width - mgn;
                bool top = p.Y <= mgn;

                if (top && left) r = HitTestCodes.HTTOPLEFT;       // 左上角拉伸
                else if (top && right) r = HitTestCodes.HTTOPRIGHT; // 右上角拉伸
                else if (top) r = HitTestCodes.HTTOP;               // 顶边拉伸
                else if (left) r = HitTestCodes.HTLEFT;             // 左边缘拉伸
                else if (right) r = HitTestCodes.HTRIGHT;           // 右边缘拉伸
            }
            m.Result = (IntPtr)r;
        }
    }

    // ============================================================
    //  边缘细条（8px）：只贴在窗口的左/右/下边缘，负责拉伸命中。
    //  不覆盖页面中央区域，因此完全不干扰网页点击。
    //  使用 WS_EX_TRANSPARENT 且不绘制任何内容，视觉上完全透明。
    // ============================================================
    public class EdgeStrip : Control
    {
        public enum Kind { Left, Right, Bottom }

        private Kind kind;

        // 最大化时禁用边缘拉伸（全部穿透）
        public bool DisableResize = false;

        public EdgeStrip(Kind k)
        {
            kind = k;
            SetStyle(ControlStyles.SupportsTransparentBackColor, false);
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer, true);
            TabStop = false;
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x00000020; // WS_EX_TRANSPARENT：不绘制，直接看到下层网页
                return cp;
            }
        }

        protected override void OnPaintBackground(PaintEventArgs pevent)
        {
            // 不绘制背景，保持透明
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            // 不绘制任何内容
        }

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();
        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

        protected override void WndProc(ref Message m)
        {
            // 拉伸拖动必须转发给顶层窗体，否则系统会拉伸细条子窗口本身
            if (m.Msg == 0x00A1) // WM_NCLBUTTONDOWN
            {
                ReleaseCapture();
                SendMessage(Parent.Handle, m.Msg, m.WParam, m.LParam);
                return;
            }

            if (m.Msg == HitTestCodes.WM_NCHITTEST)
            {
                int r = HitTestCodes.HTTRANSPARENT;
                if (!DisableResize)
                {
                    switch (kind)
                    {
                        case Kind.Left:
                            r = HitTestCodes.HTLEFT;
                            break;
                        case Kind.Right:
                            r = HitTestCodes.HTRIGHT;
                            break;
                        case Kind.Bottom:
                            {
                                long lp = m.LParam.ToInt64();
                                int mx = (short)(lp & 0xFFFF);
                                int my = (short)((lp >> 16) & 0xFFFF);
                                Point p = PointToClient(new Point(mx, my));
                                if (p.X <= HitTestCodes.EdgeMargin) r = HitTestCodes.HTBOTTOMLEFT;
                                else if (p.X >= Width - HitTestCodes.EdgeMargin) r = HitTestCodes.HTBOTTOMRIGHT;
                                else r = HitTestCodes.HTBOTTOM;
                            }
                            break;
                    }
                }
                m.Result = (IntPtr)r;
                return;
            }
            base.WndProc(ref m);
        }
    }

    // ============================================================
    //  窗口控制按钮（置顶 / 最小化 / 最大化还原 / 关闭），Edge 风格
    // ============================================================
    public class WinButton : Control
    {
        public enum Kind { Pin, Minimize, Maximize, Close }

        private Kind kind;
        private bool hovered = false;
        private bool pressed = false;

        public bool IsMaximized = false;
        public bool IsPinned = false;

        public WinButton(Kind k)
        {
            kind = k;
            Width = 46;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, false);
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            hovered = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            hovered = false;
            pressed = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                pressed = true;
                Invalidate();
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            pressed = false;
            Invalidate();
            base.OnMouseUp(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            // 背景：悬停高亮；关闭按钮悬停为红色
            Color back = Color.Black;
            if (hovered)
            {
                if (kind == Kind.Close)
                {
                    back = pressed ? Color.FromArgb(241, 112, 122) : Color.FromArgb(232, 17, 35);
                }
                else
                {
                    back = pressed ? Color.FromArgb(70, 70, 70) : Color.FromArgb(52, 52, 52);
                }
            }
            g.Clear(back);

            float cx = Width / 2f;
            float cy = Height / 2f;
            using (Pen pen = new Pen(Color.White, 1.3f))
            {
                switch (kind)
                {
                    case Kind.Pin: // 置顶：箭头上方一条横线（"保持最前"），激活时渲染为青色
                        {
                            Color gc = IsPinned ? Color.FromArgb(37, 244, 238) : Color.White;
                            using (Pen pinPen = new Pen(gc, 1.3f))
                            {
                                g.DrawLine(pinPen, cx - 6.5f, cy - 7.5f, cx + 6.5f, cy - 7.5f); // 顶栏线
                                g.DrawLine(pinPen, cx, cy + 7.5f, cx, cy - 2.5f);               // 箭杆
                                g.DrawLine(pinPen, cx - 4f, cy + 1f, cx, cy - 2.5f);            // 左翼
                                g.DrawLine(pinPen, cx + 4f, cy + 1f, cx, cy - 2.5f);            // 右翼
                            }
                        }
                        break;

                    case Kind.Minimize: // 短横线
                        g.DrawLine(pen, cx - 8, cy + 5, cx + 8, cy + 5);
                        break;

                    case Kind.Maximize:
                        if (!IsMaximized)
                        {
                            g.DrawRectangle(pen, cx - 5.5f, cy - 5.5f, 11, 11); // 最大化：方框
                        }
                        else
                        {
                            // 还原：左下实框 + 右上三边
                            g.DrawRectangle(pen, cx - 6.5f, cy - 2.5f, 8.5f, 8.5f);
                            g.DrawLine(pen, cx - 1.5f, cy - 6.5f, cx + 6f, cy - 6.5f);
                            g.DrawLine(pen, cx + 6f, cy - 6.5f, cx + 6f, cy + 1.5f);
                            g.DrawLine(pen, cx - 1.5f, cy - 6.5f, cx - 1.5f, cy - 4.5f);
                        }
                        break;

                    case Kind.Close: // 叉
                        g.DrawLine(pen, cx - 6.5f, cy - 6.5f, cx + 6.5f, cy + 6.5f);
                        g.DrawLine(pen, cx + 6.5f, cy - 6.5f, cx - 6.5f, cy + 6.5f);
                        break;
                }
            }
        }
    }
}
