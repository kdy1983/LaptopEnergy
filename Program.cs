using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace CpuPowerTray;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var mutex = new Mutex(true, "CpuPowerTray.Singleton", out bool createdNew);

        if (!createdNew)
        {
            MessageBox.Show(
                "CPU Power Tray가 이미 실행 중입니다.",
                "CPU Power Tray",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new TrayApplicationContext());
    }
}

internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly NotifyIcon _notifyIcon;
    private readonly ContextMenuStrip _menu;
    private readonly ToolStripMenuItem _statusItem;
    private readonly Dictionary<int, ToolStripMenuItem> _maximumItems = new();
    private readonly Dictionary<int, ToolStripMenuItem> _minimumItems = new();
    private readonly List<Image> _ownedImages = new();
    private bool _disposed;

    public TrayApplicationContext()
    {
        _menu = new ContextMenuStrip
        {
            ImageScalingSize = new Size(18, 18),
            ShowImageMargin = true,
            ShowCheckMargin = true
        };

        _statusItem = new ToolStripMenuItem("현재 설정 읽는 중...")
        {
            Enabled = false,
            Image = Own(MenuIconFactory.CreateStatus())
        };

        BuildMenu();
        _menu.Opening += (_, _) => RefreshStatus();

        _notifyIcon = new NotifyIcon
        {
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application,
            Text = "CPU Power Tray",
            ContextMenuStrip = _menu,
            Visible = true
        };

        _notifyIcon.MouseClick += NotifyIcon_MouseClick;
        RefreshStatus();
    }

    private void BuildMenu()
    {
        _menu.Items.Add(_statusItem);
        _menu.Items.Add(new ToolStripSeparator());

        var quickToggle = new ToolStripMenuItem("빠른 전환: 최대 75% ↔ 99%")
        {
            Image = Own(MenuIconFactory.CreateLightning())
        };
        quickToggle.Click += (_, _) => Toggle75And99();
        _menu.Items.Add(quickToggle);

        var maxMenu = new ToolStripMenuItem("최대 프로세서 상태 (AC+배터리)")
        {
            Image = Own(MenuIconFactory.CreateProcessorArrow(upward: true))
        };

        for (int value = 10; value <= 100; value += 5)
        {
            int selectedValue = value;
            AddPreset(
                maxMenu,
                _maximumItems,
                selectedValue,
                isMaximum: true,
                () => ApplyMaximum(selectedValue));
        }

        maxMenu.DropDownItems.Add(new ToolStripSeparator());
        AddPreset(
            maxMenu,
            _maximumItems,
            99,
            isMaximum: true,
            () => ApplyMaximum(99),
            "99% (빠른 전환값)");

        _menu.Items.Add(maxMenu);

        var minMenu = new ToolStripMenuItem("최소 프로세서 상태 (AC+배터리)")
        {
            Image = Own(MenuIconFactory.CreateProcessorArrow(upward: false))
        };

        for (int value = 10; value <= 100; value += 5)
        {
            int selectedValue = value;
            AddPreset(
                minMenu,
                _minimumItems,
                selectedValue,
                isMaximum: false,
                () => ApplyMinimum(selectedValue));
        }

        _menu.Items.Add(minMenu);
        _menu.Items.Add(new ToolStripSeparator());

        var refreshItem = new ToolStripMenuItem("현재 설정 새로고침")
        {
            Image = Own(MenuIconFactory.CreateRefresh())
        };
        refreshItem.Click += (_, _) => RefreshStatus(showError: true);
        _menu.Items.Add(refreshItem);

        var openPowerOptions = new ToolStripMenuItem("고급 전원 옵션 열기")
        {
            Image = Own(MenuIconFactory.CreateSliders())
        };
        openPowerOptions.Click += (_, _) => OpenAdvancedPowerOptions();
        _menu.Items.Add(openPowerOptions);

        _menu.Items.Add(new ToolStripSeparator());

        var exitItem = new ToolStripMenuItem("종료")
        {
            Image = Own(MenuIconFactory.CreatePower())
        };
        exitItem.Click += (_, _) => ExitApplication();
        _menu.Items.Add(exitItem);
    }

    private void AddPreset(
        ToolStripMenuItem parent,
        IDictionary<int, ToolStripMenuItem> itemMap,
        int value,
        bool isMaximum,
        Action action,
        string? label = null)
    {
        var item = new ToolStripMenuItem(label ?? $"{value}%")
        {
            Image = Own(MenuIconFactory.CreateLevel(value, isMaximum)),
            CheckOnClick = false
        };

        item.Click += (_, _) => action();
        parent.DropDownItems.Add(item);
        itemMap[value] = item;
    }

    private Image Own(Image image)
    {
        _ownedImages.Add(image);
        return image;
    }

    private void NotifyIcon_MouseClick(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            Toggle75And99();
        }
    }

    private void Toggle75And99()
    {
        try
        {
            ProcessorPowerState state = ProcessorPower.ReadCurrent();
            int next = state.AcMaximum <= 75 ? 99 : 75;
            ProcessorPower.SetMaximumBoth(next);
            ShowApplied($"최대 프로세서 상태를 {next}%로 변경했습니다.");
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    private void ApplyMaximum(int value)
    {
        try
        {
            ProcessorPower.SetMaximumBoth(value);
            ShowApplied($"최대 프로세서 상태를 {value}%로 변경했습니다.");
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    private void ApplyMinimum(int value)
    {
        try
        {
            ProcessorPower.SetMinimumBoth(value);
            ShowApplied($"최소 프로세서 상태를 {value}%로 변경했습니다.");
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    private void ShowApplied(string message)
    {
        RefreshStatus();
        _notifyIcon.BalloonTipTitle = "CPU 전원 설정 변경";
        _notifyIcon.BalloonTipText = message;
        _notifyIcon.BalloonTipIcon = ToolTipIcon.Info;
        _notifyIcon.ShowBalloonTip(1800);
    }

    private void RefreshStatus(bool showError = false)
    {
        try
        {
            ProcessorPowerState state = ProcessorPower.ReadCurrent();
            _statusItem.Text =
                $"현재 | AC 최소 {state.AcMinimum}% / 최대 {state.AcMaximum}% | " +
                $"배터리 최소 {state.DcMinimum}% / 최대 {state.DcMaximum}%";

            _notifyIcon.Text = $"CPU 전원: AC {state.AcMaximum}% / DC {state.DcMaximum}%";

            UpdateCheckedItems(_maximumItems, state.AcMaximum, state.DcMaximum);
            UpdateCheckedItems(_minimumItems, state.AcMinimum, state.DcMinimum);
        }
        catch (Exception ex)
        {
            _statusItem.Text = "현재 설정을 읽지 못했습니다.";
            _notifyIcon.Text = "CPU Power Tray";
            ClearChecks(_maximumItems);
            ClearChecks(_minimumItems);

            if (showError)
            {
                ShowError(ex);
            }
        }
    }

    private static void UpdateCheckedItems(
        IReadOnlyDictionary<int, ToolStripMenuItem> items,
        int acValue,
        int dcValue)
    {
        foreach ((int value, ToolStripMenuItem item) in items)
        {
            item.Checked = acValue == value && dcValue == value;
        }
    }

    private static void ClearChecks(IReadOnlyDictionary<int, ToolStripMenuItem> items)
    {
        foreach (ToolStripMenuItem item in items.Values)
        {
            item.Checked = false;
        }
    }

    private static void OpenAdvancedPowerOptions()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "control.exe",
                Arguments = "powercfg.cpl,,3",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                "고급 전원 옵션을 열 수 없습니다.",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private static void ShowError(Exception ex)
    {
        MessageBox.Show(
            ex.Message,
            "CPU 전원 설정 변경 실패",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
    }

    private void ExitApplication()
    {
        _notifyIcon.Visible = false;
        ExitThread();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _menu.Dispose();

            foreach (Image image in _ownedImages)
            {
                image.Dispose();
            }
        }

        base.Dispose(disposing);
    }
}

internal static class MenuIconFactory
{
    private const int Size = 18;

    public static Bitmap CreateStatus()
    {
        Bitmap bitmap = CreateBitmap();
        using Graphics g = Prepare(bitmap);
        using var pen = new Pen(Color.FromArgb(55, 125, 210), 1.8f);
        using var checkPen = new Pen(Color.FromArgb(45, 155, 85), 2.0f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        };

        g.DrawEllipse(pen, 2.5f, 2.5f, 13f, 13f);
        g.DrawLine(checkPen, 5f, 9.5f, 8f, 12f);
        g.DrawLine(checkPen, 8f, 12f, 13.3f, 6.5f);
        return bitmap;
    }

    public static Bitmap CreateLightning()
    {
        Bitmap bitmap = CreateBitmap();
        using Graphics g = Prepare(bitmap);
        using var brush = new SolidBrush(Color.FromArgb(244, 178, 20));
        PointF[] bolt =
        {
            new(10.2f, 1.5f),
            new(4.2f, 9.2f),
            new(8.1f, 9.2f),
            new(6.7f, 16.5f),
            new(13.8f, 7.3f),
            new(9.7f, 7.3f)
        };
        g.FillPolygon(brush, bolt);
        return bitmap;
    }

    public static Bitmap CreateProcessorArrow(bool upward)
    {
        Bitmap bitmap = CreateBitmap();
        using Graphics g = Prepare(bitmap);
        using var chipPen = new Pen(Color.FromArgb(80, 90, 105), 1.4f);
        using var chipBrush = new SolidBrush(Color.FromArgb(225, 230, 236));
        using var arrowPen = new Pen(
            upward ? Color.FromArgb(42, 130, 220) : Color.FromArgb(65, 155, 90),
            2.0f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        };

        g.FillRectangle(chipBrush, 4.5f, 4.5f, 8f, 8f);
        g.DrawRectangle(chipPen, 4.5f, 4.5f, 8f, 8f);

        for (int i = 0; i < 3; i++)
        {
            float pos = 6f + i * 2.5f;
            g.DrawLine(chipPen, pos, 2.5f, pos, 4.5f);
            g.DrawLine(chipPen, pos, 12.5f, pos, 14.5f);
            g.DrawLine(chipPen, 2.5f, pos, 4.5f, pos);
            g.DrawLine(chipPen, 12.5f, pos, 14.5f, pos);
        }

        float centerX = 14.4f;
        if (upward)
        {
            g.DrawLine(arrowPen, centerX, 13.8f, centerX, 5.2f);
            g.DrawLine(arrowPen, centerX, 5.2f, 11.8f, 7.8f);
            g.DrawLine(arrowPen, centerX, 5.2f, 17f, 7.8f);
        }
        else
        {
            g.DrawLine(arrowPen, centerX, 4.2f, centerX, 12.8f);
            g.DrawLine(arrowPen, centerX, 12.8f, 11.8f, 10.2f);
            g.DrawLine(arrowPen, centerX, 12.8f, 17f, 10.2f);
        }

        return bitmap;
    }

    public static Bitmap CreateLevel(int value, bool isMaximum)
    {
        Bitmap bitmap = CreateBitmap();
        using Graphics g = Prepare(bitmap);
        using var borderPen = new Pen(Color.FromArgb(95, 100, 110), 1.2f);
        using var fillBrush = new SolidBrush(
            isMaximum ? Color.FromArgb(55, 135, 220) : Color.FromArgb(70, 165, 100));

        g.DrawRectangle(borderPen, 2.5f, 5f, 12f, 8f);
        g.DrawLine(borderPen, 15f, 7.2f, 15f, 10.8f);

        float usableWidth = 10f;
        float fillWidth = Math.Max(1f, usableWidth * Math.Clamp(value, 0, 100) / 100f);
        g.FillRectangle(fillBrush, 3.5f, 6f, fillWidth, 6f);
        return bitmap;
    }

    public static Bitmap CreateRefresh()
    {
        Bitmap bitmap = CreateBitmap();
        using Graphics g = Prepare(bitmap);
        using var pen = new Pen(Color.FromArgb(55, 125, 210), 1.8f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        };
        using var brush = new SolidBrush(Color.FromArgb(55, 125, 210));

        g.DrawArc(pen, 3f, 3f, 12f, 12f, 35f, 285f);
        PointF[] arrow =
        {
            new(13.2f, 2.5f),
            new(16.2f, 4.7f),
            new(12.5f, 6.2f)
        };
        g.FillPolygon(brush, arrow);
        return bitmap;
    }

    public static Bitmap CreateSliders()
    {
        Bitmap bitmap = CreateBitmap();
        using Graphics g = Prepare(bitmap);
        using var linePen = new Pen(Color.FromArgb(85, 95, 110), 1.6f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        };
        using var knobBrush = new SolidBrush(Color.FromArgb(55, 125, 210));

        g.DrawLine(linePen, 3f, 5f, 15f, 5f);
        g.DrawLine(linePen, 3f, 9f, 15f, 9f);
        g.DrawLine(linePen, 3f, 13f, 15f, 13f);
        g.FillEllipse(knobBrush, 6f, 3f, 4f, 4f);
        g.FillEllipse(knobBrush, 11f, 7f, 4f, 4f);
        g.FillEllipse(knobBrush, 4f, 11f, 4f, 4f);
        return bitmap;
    }

    public static Bitmap CreatePower()
    {
        Bitmap bitmap = CreateBitmap();
        using Graphics g = Prepare(bitmap);
        using var pen = new Pen(Color.FromArgb(205, 70, 70), 2.0f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        };

        g.DrawArc(pen, 3f, 3.5f, 12f, 12f, -45f, 270f);
        g.DrawLine(pen, 9f, 1.8f, 9f, 8.5f);
        return bitmap;
    }

    private static Bitmap CreateBitmap() => new(Size, Size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);

    private static Graphics Prepare(Bitmap bitmap)
    {
        Graphics g = Graphics.FromImage(bitmap);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.Clear(Color.Transparent);
        return g;
    }
}

internal readonly record struct ProcessorPowerState(
    int AcMinimum,
    int AcMaximum,
    int DcMinimum,
    int DcMaximum);

internal static class ProcessorPower
{
    private const uint ErrorSuccess = 0;

    private static readonly Guid ProcessorSubgroup =
        new("54533251-82be-4824-96c1-47b60b740d00");

    private static readonly Guid MinimumProcessorState =
        new("893dee8e-2bef-41e0-89c6-b55d0929964c");

    private static readonly Guid MaximumProcessorState =
        new("bc5038f7-23e0-4960-96da-33abaf5935ec");

    public static ProcessorPowerState ReadCurrent()
    {
        Guid scheme = GetActiveScheme();

        return new ProcessorPowerState(
            AcMinimum: checked((int)ReadAc(scheme, MinimumProcessorState)),
            AcMaximum: checked((int)ReadAc(scheme, MaximumProcessorState)),
            DcMinimum: checked((int)ReadDc(scheme, MinimumProcessorState)),
            DcMaximum: checked((int)ReadDc(scheme, MaximumProcessorState)));
    }

    public static void SetMaximumBoth(int value)
    {
        ValidatePercentage(value);

        Guid scheme = GetActiveScheme();
        uint acMinimum = ReadAc(scheme, MinimumProcessorState);
        uint dcMinimum = ReadDc(scheme, MinimumProcessorState);

        // 최소값이 새 최대값보다 높으면 유효한 범위를 유지하도록 같이 낮춥니다.
        if (acMinimum > value)
        {
            WriteAc(scheme, MinimumProcessorState, (uint)value);
        }

        if (dcMinimum > value)
        {
            WriteDc(scheme, MinimumProcessorState, (uint)value);
        }

        WriteAc(scheme, MaximumProcessorState, (uint)value);
        WriteDc(scheme, MaximumProcessorState, (uint)value);
        ActivateScheme(scheme);
    }

    public static void SetMinimumBoth(int value)
    {
        ValidatePercentage(value);

        Guid scheme = GetActiveScheme();
        uint acMaximum = ReadAc(scheme, MaximumProcessorState);
        uint dcMaximum = ReadDc(scheme, MaximumProcessorState);

        // 최대값이 새 최소값보다 낮으면 유효한 범위를 유지하도록 같이 높입니다.
        if (acMaximum < value)
        {
            WriteAc(scheme, MaximumProcessorState, (uint)value);
        }

        if (dcMaximum < value)
        {
            WriteDc(scheme, MaximumProcessorState, (uint)value);
        }

        WriteAc(scheme, MinimumProcessorState, (uint)value);
        WriteDc(scheme, MinimumProcessorState, (uint)value);
        ActivateScheme(scheme);
    }

    private static void ValidatePercentage(int value)
    {
        if (value is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "값은 0~100 사이여야 합니다.");
        }
    }

    private static Guid GetActiveScheme()
    {
        uint result = NativeMethods.PowerGetActiveScheme(IntPtr.Zero, out IntPtr schemePointer);
        EnsureSuccess(result, "활성 전원 계획 확인");

        try
        {
            return Marshal.PtrToStructure<Guid>(schemePointer);
        }
        finally
        {
            if (schemePointer != IntPtr.Zero)
            {
                NativeMethods.LocalFree(schemePointer);
            }
        }
    }

    private static uint ReadAc(Guid scheme, Guid setting)
    {
        Guid subgroup = ProcessorSubgroup;
        uint result = NativeMethods.PowerReadACValueIndex(
            IntPtr.Zero,
            ref scheme,
            ref subgroup,
            ref setting,
            out uint value);

        EnsureSuccess(result, "AC 전원 설정 읽기");
        return value;
    }

    private static uint ReadDc(Guid scheme, Guid setting)
    {
        Guid subgroup = ProcessorSubgroup;
        uint result = NativeMethods.PowerReadDCValueIndex(
            IntPtr.Zero,
            ref scheme,
            ref subgroup,
            ref setting,
            out uint value);

        EnsureSuccess(result, "배터리 전원 설정 읽기");
        return value;
    }

    private static void WriteAc(Guid scheme, Guid setting, uint value)
    {
        Guid subgroup = ProcessorSubgroup;
        uint result = NativeMethods.PowerWriteACValueIndex(
            IntPtr.Zero,
            ref scheme,
            ref subgroup,
            ref setting,
            value);

        EnsureSuccess(result, "AC 전원 설정 쓰기");
    }

    private static void WriteDc(Guid scheme, Guid setting, uint value)
    {
        Guid subgroup = ProcessorSubgroup;
        uint result = NativeMethods.PowerWriteDCValueIndex(
            IntPtr.Zero,
            ref scheme,
            ref subgroup,
            ref setting,
            value);

        EnsureSuccess(result, "배터리 전원 설정 쓰기");
    }

    private static void ActivateScheme(Guid scheme)
    {
        uint result = NativeMethods.PowerSetActiveScheme(IntPtr.Zero, ref scheme);
        EnsureSuccess(result, "전원 설정 적용");
    }

    private static void EnsureSuccess(uint result, string operation)
    {
        if (result != ErrorSuccess)
        {
            throw new Win32Exception((int)result, $"{operation} 실패 (오류 코드: {result})");
        }
    }

    private static class NativeMethods
    {
        [DllImport("powrprof.dll", SetLastError = true)]
        internal static extern uint PowerGetActiveScheme(
            IntPtr userRootPowerKey,
            out IntPtr activePolicyGuid);

        [DllImport("powrprof.dll", SetLastError = true)]
        internal static extern uint PowerReadACValueIndex(
            IntPtr rootPowerKey,
            ref Guid schemeGuid,
            ref Guid subgroupOfPowerSettingsGuid,
            ref Guid powerSettingGuid,
            out uint acValueIndex);

        [DllImport("powrprof.dll", SetLastError = true)]
        internal static extern uint PowerReadDCValueIndex(
            IntPtr rootPowerKey,
            ref Guid schemeGuid,
            ref Guid subgroupOfPowerSettingsGuid,
            ref Guid powerSettingGuid,
            out uint dcValueIndex);

        [DllImport("powrprof.dll", SetLastError = true)]
        internal static extern uint PowerWriteACValueIndex(
            IntPtr rootPowerKey,
            ref Guid schemeGuid,
            ref Guid subgroupOfPowerSettingsGuid,
            ref Guid powerSettingGuid,
            uint acValueIndex);

        [DllImport("powrprof.dll", SetLastError = true)]
        internal static extern uint PowerWriteDCValueIndex(
            IntPtr rootPowerKey,
            ref Guid schemeGuid,
            ref Guid subgroupOfPowerSettingsGuid,
            ref Guid powerSettingGuid,
            uint dcValueIndex);

        [DllImport("powrprof.dll", SetLastError = true)]
        internal static extern uint PowerSetActiveScheme(
            IntPtr userRootPowerKey,
            ref Guid schemeGuid);

        [DllImport("kernel32.dll")]
        internal static extern IntPtr LocalFree(IntPtr memory);
    }
}
