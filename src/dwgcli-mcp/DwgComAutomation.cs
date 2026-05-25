// DwgComAutomation — 可选的 COM 自动化模块
// ============================================================
// 通过 CAD COM API 实现 dwgcli 无法做到的渲染/截图/PDF 导出。
// 支持 AutoCAD、ZWCAD、GstarCAD、BricsCAD。
// 哲学: 无 CAD = 静默降级，不破坏 dwgcli 的零依赖特性。
//
// 依赖: System.Drawing.Common (仅在 Windows + CAD 安装时使用)
// ============================================================

using System.Drawing;
using System.Runtime.InteropServices;

namespace DwgCli.Mcp;

/// <summary>
/// 可选的 COM 自动化模块 — 通过 CAD COM API 实现渲染/截图/PDF 导出。
/// 支持 AutoCAD / ZWCAD / GstarCAD / BricsCAD，按优先级自动检测。
/// 所有操作都是可选的，CAD 不可用时返回明确错误，不抛异常。
/// 仅在 Windows 上有效。
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class DwgComAutomation : IDisposable
{
    /// <summary>支持的 CAD 类型与 ProgID 映射。</summary>
    private static readonly (string Name, string ProgId)[] SupportedCads =
    [
        ("AutoCAD",  "AutoCAD.Application"),
        ("ZWCAD",    "ZWCAD.Application"),
        ("GstarCAD", "GCAD.Application"),
        ("BricsCAD", "BricscadApp.AcadApplication"),
    ];

    /// <summary>窗口搜索词（用于截图时的窗口查找，按类型映射）。</summary>
    private static readonly (string Type, string WindowTitle)[] WindowSearchTerms =
    [
        ("AutoCAD",  "AutoCAD"),
        ("ZWCAD",    "ZWCAD"),
        ("GstarCAD", "GstarCAD"),
        ("BricsCAD", "BricsCAD"),
    ];

    /// <summary>通用 CAD 窗口类名（所有类型共用）。</summary>
    private static readonly string[] CadWindowClasses =
    [
        "AfxFrameOrView", "AfxMDIFrame", "OpenedDoor",
        "AutoCAD", "AutoCAD.Launcher",
    ];

    // COM 连接重用：MCP 会话内保持连接，避免反复启动/关闭 CAD
    private static DwgComAutomation? _sharedInstance;
    private static readonly object _lock = new();
    private dynamic? _acadApp;
    private string _connectedCadType = "";
    private bool _disposed;

    /// <summary>当前连接的 CAD 类型名称（如 "AutoCAD"、"ZWCAD"）。</summary>
    public string ConnectedCadType => _connectedCadType;

    /// <summary>获取或创建共享实例（MCP 会话内复用）。</summary>
    public static DwgComAutomation GetShared(
        bool visible = false,
        string? preferredCadType = null)
    {
        lock (_lock)
        {
            if (_sharedInstance == null || !_sharedInstance.IsConnected)
            {
                _sharedInstance?.Dispose();
                _sharedInstance = new DwgComAutomation();
                _sharedInstance.Connect(visible, preferredCadType);
            }
            return _sharedInstance;
        }
    }

    /// <summary>
    /// 重置共享实例（强制下次调用重新连接）。
    /// </summary>
    public static void ResetShared()
    {
        lock (_lock)
        {
            _sharedInstance?.Dispose();
            _sharedInstance = null;
        }
    }

    // ── Win32 API (窗口截图等) ────────────────────────────────────────

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern IntPtr FindWindow(string? lpClassName, string lpWindowName);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("ole32.dll")]
    private static extern int CLSIDFromProgID([MarshalAs(UnmanagedType.LPWStr)] string lpszProgID, out Guid pclsid);

    [DllImport("ole32.dll")]
    private static extern int GetActiveObject(ref Guid rclsid, IntPtr pvReserved, [MarshalAs(UnmanagedType.IUnknown)] out object ppunk);

    private const int SW_RESTORE = 9;
    private const int S_OK = 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    // ── 检测 & 连接 ───────────────────────────────────────────────────

    /// <summary>
    /// 检测系统中已安装的 CAD 类型（不启动进程）。
    /// </summary>
    public static List<string> GetAvailableCads()
    {
        var available = new List<string>();
        foreach (var (name, progId) in SupportedCads)
        {
            try
            {
                if (Type.GetTypeFromProgID(progId) != null)
                    available.Add(name);
            }
            catch { }
        }
        return available;
    }

    /// <summary>
    /// 检测指定 CAD 类型是否已安装。
    /// </summary>
    public static bool IsAvailable(string? cadType = null)
    {
        if (cadType != null)
        {
            foreach (var (name, progId) in SupportedCads)
            {
                if (!string.Equals(name, cadType, StringComparison.OrdinalIgnoreCase)) continue;
                try { return Type.GetTypeFromProgID(progId) != null; }
                catch { return false; }
            }
            return false;
        }

        // 不指定类型则检查任意 CAD
        return GetAvailableCads().Count > 0;
    }

    /// <summary>
    /// 连接到 CAD，按优先级自动检测或指定类型。
    /// </summary>
    public bool Connect(bool visible = false, string? preferredCadType = null)
    {
        if (_acadApp != null) return true;

        // 确定尝试顺序
        var candidates = preferredCadType != null
            ? SupportedCads.Where(c =>
                string.Equals(c.Name, preferredCadType, StringComparison.OrdinalIgnoreCase))
            : SupportedCads; // 默认优先级: AutoCAD → ZWCAD → GstarCAD → BricsCAD

        foreach (var (name, progId) in candidates)
        {
            // 先尝试附加到已运行的实例
            if (CLSIDFromProgID(progId, out var clsid) == S_OK)
            {
                try
                {
                    var hr = GetActiveObject(ref clsid, IntPtr.Zero, out var obj);
                    if (hr == S_OK && obj != null)
                    {
                        _acadApp = obj;
                        _connectedCadType = name;
                        break;
                    }
                }
                catch { /* fall through to CreateInstance */ }
            }

            // 没有运行中的实例，启动新实例
            if (_acadApp == null)
            {
                try
                {
                    var type = Type.GetTypeFromProgID(progId);
                    if (type != null)
                    {
                        _acadApp = Activator.CreateInstance(type);
                        _connectedCadType = name;
                        break;
                    }
                }
                catch { /* try next candidate */ }
            }
        }

        if (_acadApp != null)
        {
            try { _acadApp.Visible = visible; } catch { }
            return true;
        }
        return false;
    }

    /// <summary>
    /// 是否已连接。
    /// </summary>
    public bool IsConnected => _acadApp != null;

    /// <summary>
    /// 确保已连接，未连接时自动尝试连接。
    /// </summary>
    private bool EnsureConnected(string? preferredCadType = null)
    {
        if (_acadApp != null) return true;
        return Connect(visible: false, preferredCadType);
    }

    // ── 在 CAD 中打开图纸 ────────────────────────────────────────────

    /// <summary>
    /// 在 AutoCAD 中打开 DWG 文件，返回文档对象。
    /// </summary>
    public bool OpenInCad(string dwgPath)
    {
        if (!EnsureConnected()) return false;
        if (!File.Exists(dwgPath)) return false;

        try
        {
            var docs = _acadApp!.Documents;
            docs.Open(Path.GetFullPath(dwgPath));
            return true;
        }
        catch
        {
            return false;
        }
    }

    // ── 截图 (窗口) ───────────────────────────────────────────────────

    /// <summary>
    /// 截图 AutoCAD 窗口，返回 base64 PNG。
    /// </summary>
    public string? Screenshot()
    {
        if (!EnsureConnected()) return null;

        try
        {
            var hwnd = FindCadWindow();
            if (hwnd == IntPtr.Zero) return null;

            // 还原窗口（如果最小化）
            if (IsIconic(hwnd))
                ShowWindow(hwnd, SW_RESTORE);

            // 置前
            try { SetForegroundWindow(hwnd); } catch { }
            Thread.Sleep(300);

            // 获取窗口位置
            if (!GetWindowRect(hwnd, out var rect)) return null;
            var width = rect.Right - rect.Left;
            var height = rect.Bottom - rect.Top;
            if (width <= 0 || height <= 0) return null;

            // 截屏
            using var bitmap = new Bitmap(width, height);
            using var g = Graphics.FromImage(bitmap);
            g.CopyFromScreen(rect.Left, rect.Top, 0, 0, bitmap.Size);

            using var ms = new MemoryStream();
            bitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            return Convert.ToBase64String(ms.ToArray());
        }
        catch
        {
            return null;
        }
    }

    // ── PNG 导出 (PNGOUT) ────────────────────────────────────────────

    /// <summary>
    /// 使用 CAD 引擎导出 PNG（比截图更清晰，无窗口装饰）。
    /// 通过 SendCommand 调用 PNGOUT。
    /// </summary>
    public bool ExportPng(string dwgPath, string outputPng)
    {
        if (!EnsureConnected()) return false;
        if (!File.Exists(dwgPath)) return false;

        try
        {
            var docs = _acadApp!.Documents;
            var doc = docs.Open(Path.GetFullPath(dwgPath));

            try
            {
                var absPath = Path.GetFullPath(outputPng);

                // 禁用文件对话框
                doc.SendCommand("FILEDIA 0\n");
                Thread.Sleep(100);

                // 缩放至全图
                _acadApp.ZoomExtents();
                Thread.Sleep(200);

                // PNGOUT: command → path → 选择所有 → 确认
                doc.SendCommand($"_PNGOUT\n{absPath}\n_ALL\n\n");
                Thread.Sleep(2000); // 等待渲染

                // 恢复文件对话框
                doc.SendCommand("FILEDIA 1\n");

                return File.Exists(absPath);
            }
            finally
            {
                doc.Close(false);
            }
        }
        catch
        {
            return false;
        }
    }

    // ── PLOT → PDF ───────────────────────────────────────────────────

    /// <summary>
    /// 按窗口范围导出 PDF（通过 -PLOT 命令）。
    /// </summary>
    public bool PlotWindowToPdf(
        string dwgPath,
        double xMin, double yMin,
        double xMax, double yMax,
        string outputPdf,
        string paperSize = "A1",
        string plotter = "DWG To PDF.pc3")
    {
        if (!EnsureConnected()) return false;
        if (!File.Exists(dwgPath)) return false;

        try
        {
            var docs = _acadApp!.Documents;
            var doc = docs.Open(Path.GetFullPath(dwgPath));

            try
            {
                var absPath = Path.GetFullPath(outputPdf);

                // AutoCAD -PLOT 命令参数序列
                var cmd = $"-PLOT\n" +
                          $"Y\n" +                              // 详细配置
                          $"Model\n" +                          // 布局名
                          $"{plotter}\n" +                      // 打印机配置
                          $"{paperSize}\n" +                    // 纸张尺寸
                          $"M\n" +                              // 单位: 毫米
                          $"LANDSCAPE\n" +                      // 横向
                          $"N\n" +                              // 倒置: 否
                          $"W\n" +                              // 打印区域: 窗口
                          $"{xMin},{yMin}\n" +                  // 窗口左下角
                          $"{xMax},{yMax}\n" +                  // 窗口右上角
                          $"F\n" +                              // 布满纸张
                          $"C\n" +                              // 居中打印
                          $"dwgcli_mono.ctb\n" +                // 打印样式表
                          $"N\n" +                              // 着色打印模式
                          $"N\n" +                              // 后台打印: 否
                          $"N\n" +                              // 打印到文件
                          $"N\n" +                              // 保存页面设置: 否
                          $"{absPath}\n" +                      // 输出文件名
                          $"Y\n";                               // 确认打印

                doc.SendCommand(cmd);
                Thread.Sleep(1000); // 等待 PDF 生成

                return File.Exists(absPath);
            }
            finally
            {
                doc.Close(false);
            }
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 批量导出 PDF — 多个视口范围逐个输出。
    /// </summary>
    public List<string> BatchPlotToPdf(
        string dwgPath,
        List<(double xMin, double yMin, double xMax, double yMax, string output)> windows)
    {
        var results = new List<string>();
        foreach (var (xMin, yMin, xMax, yMax, output) in windows)
        {
            if (PlotWindowToPdf(dwgPath, xMin, yMin, xMax, yMax, output))
                results.Add(output);
        }
        return results;
    }

    // ── 视图操作 ─────────────────────────────────────────────────────

    /// <summary>
    /// 在 AutoCAD 中缩放到全图范围。
    /// </summary>
    public bool ZoomExtents()
    {
        if (!EnsureConnected()) return false;

        try
        {
            _acadApp!.ZoomExtents();
            return true;
        }
        catch
        {
            return false;
        }
    }

    // ── 辅助 ──────────────────────────────────────────────────────────

    /// <summary>
    /// 查找当前连接的 CAD 主窗口句柄。
    /// 先按标题匹配已连接的 CAD 类型，再按通用窗口类名搜索。
    /// </summary>
    private IntPtr FindCadWindow()
    {
        // 1. 按当前连接的 CAD 类型标题搜索（例如 ZWCAD 标题含 "ZWCAD"）
        if (!string.IsNullOrEmpty(_connectedCadType))
        {
            var found = IntPtr.Zero;
            var titleTerm = _connectedCadType; // "AutoCAD" / "ZWCAD" / "GstarCAD" / "BricsCAD"

            EnumWindows((hwnd, _) =>
            {
                if (found != IntPtr.Zero) return true;
                if (!IsWindowVisible(hwnd)) return true;

                var title = GetWindowText(hwnd);
                if (!title.Contains(titleTerm, StringComparison.OrdinalIgnoreCase))
                    return true;
                if (title.Contains("VBA", StringComparison.OrdinalIgnoreCase))
                    return true;

                var className = GetClassName(hwnd);
                foreach (var cls in CadWindowClasses)
                {
                    if (className.Contains(cls, StringComparison.OrdinalIgnoreCase))
                    {
                        found = hwnd;
                        break;
                    }
                }
                return true;
            }, IntPtr.Zero);

            if (found != IntPtr.Zero) return found;
        }

        // 2. 按通用窗口类名搜索
        foreach (string cls in CadWindowClasses)
        {
            var hwnd = FindWindow(cls, null);
            if (hwnd != IntPtr.Zero) return hwnd;
        }

        return IntPtr.Zero;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    private static string GetWindowText(IntPtr hWnd)
    {
        var sb = new System.Text.StringBuilder(256);
        GetWindowText(hWnd, sb, sb.Capacity);
        return sb.ToString();
    }

    private static string GetClassName(IntPtr hWnd)
    {
        var sb = new System.Text.StringBuilder(256);
        GetClassName(hWnd, sb, sb.Capacity);
        return sb.ToString();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // 释放 COM 引用。不调用 Quit() — 如果用户自己打开的 CAD 不应关掉。
        // 对于共享实例，只需要释放引用让 GC 和 COM 代理处理即可。
        _acadApp = null;
    }

}
