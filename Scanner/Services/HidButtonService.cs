using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace CzurWpfDemo.Services;

/// <summary>
/// Barcha USB HID qurilmalardan raw input orqali signal oladi.
/// CZUR ET24 Pro UVC kamera (WIA driver emas) bo'lgani uchun WIA ishlamaydi —
/// shuning uchun bu klass Raw Input API ishlatadi.
/// </summary>
public class HidButtonService
{
    // ── Win32 konstantalar ──────────────────────────────────────────────────
    private const int  WM_INPUT       = 0x00FF;
    private const uint RIDEV_INPUTSINK = 0x00000100;
    private const uint RIDEV_PAGEONLY  = 0x00200000;
    private const uint RIDEV_REMOVE    = 0x00000001;
    private const uint RIM_TYPEHID     = 2;
    private const uint RID_INPUT       = 0x10000003;
    private const uint RIDI_DEVICENAME = 0x20000007;

    // ── Strukturalar ────────────────────────────────────────────────────────
    [StructLayout(LayoutKind.Sequential)]
    private struct RAWINPUTDEVICE
    {
        public ushort usUsagePage;
        public ushort usUsage;
        public uint   dwFlags;
        public IntPtr hwndTarget;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWINPUTHEADER
    {
        public uint   dwType;
        public uint   dwSize;
        public IntPtr hDevice;
        public IntPtr wParam;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWINPUTDEVICELIST
    {
        public IntPtr hDevice;
        public uint   dwType;
    }

    // ── P/Invoke ─────────────────────────────────────────────────────────────
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterRawInputDevices(
        [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)]
        RAWINPUTDEVICE[] pRawInputDevices,
        uint uiNumDevices, uint cbSize);

    [DllImport("user32.dll")]
    private static extern uint GetRawInputData(
        IntPtr hRawInput, uint uiCommand,
        IntPtr pData, ref uint pcbSize, uint cbSizeHeader);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint GetRawInputDeviceInfo(
        IntPtr hDevice, uint uiCommand, IntPtr pData, ref uint pcbSize);

    [DllImport("user32.dll")]
    private static extern uint GetRawInputDeviceList(
        IntPtr pList, ref uint puiNumDevices, uint cbSize);

    // ── Log ──────────────────────────────────────────────────────────────────
    private static readonly string LogPath = @"C:\czur_wia_debug.txt";

    static HidButtonService()
    {
        // Class birinchi marta yuklanganda darhol yozadi
        try { File.AppendAllText(LogPath,
            $"[{DateTime.Now:HH:mm:ss.fff}] [HID] === HidButtonService class loaded ==={Environment.NewLine}"); }
        catch { }
    }

    private static void Log(string msg)
    {
        var line = $"[{DateTime.Now:HH:mm:ss.fff}] [HID] {msg}";
        System.Diagnostics.Debug.WriteLine(line);
        try { File.AppendAllText(LogPath, line + Environment.NewLine); } catch { }
    }

    // ── Public ───────────────────────────────────────────────────────────────
    public event Action? ButtonPressed;

    private HwndSource? _hwndSource;
    private IntPtr      _hwnd;

    /// <summary>AppShell.OnSourceInitialized dan chaqiriladi.</summary>
    public void Start(IntPtr hwnd)
    {
        _hwnd = hwnd;
        Log($"HidButtonService.Start() hwnd=0x{hwnd:X}");

        EnumerateDevices();
        RegisterUsagePages(hwnd);

        _hwndSource = HwndSource.FromHwnd(hwnd);
        if (_hwndSource == null)
        {
            Log("HwndSource olinmadi!");
            return;
        }
        _hwndSource.AddHook(WndProc);
        Log("WndProc hook qo'shildi.");
    }

    public void Stop()
    {
        try
        {
            _hwndSource?.RemoveHook(WndProc);
            // ro'yxatdan o'chirish
            var remove = new RAWINPUTDEVICE[]
            {
                new() { usUsagePage = 0x01, dwFlags = RIDEV_REMOVE, hwndTarget = IntPtr.Zero },
                new() { usUsagePage = 0x0C, dwFlags = RIDEV_REMOVE, hwndTarget = IntPtr.Zero },
                new() { usUsagePage = 0x0D, dwFlags = RIDEV_REMOVE, hwndTarget = IntPtr.Zero },
            };
            RegisterRawInputDevices(remove, (uint)remove.Length, (uint)Marshal.SizeOf<RAWINPUTDEVICE>());
        }
        catch { }
    }

    // ── Barcha HID qurilmalarni konsolga chiqarish ──────────────────────────
    private void EnumerateDevices()
    {
        try
        {
            uint numDevices = 0;
            uint structSize = (uint)Marshal.SizeOf<RAWINPUTDEVICELIST>();
            GetRawInputDeviceList(IntPtr.Zero, ref numDevices, structSize);

            Log($"Tizimda raw-input qurilmalar soni: {numDevices}");
            if (numDevices == 0) return;

            IntPtr listPtr = Marshal.AllocHGlobal((int)(numDevices * structSize));
            try
            {
                GetRawInputDeviceList(listPtr, ref numDevices, structSize);
                for (int i = 0; i < numDevices; i++)
                {
                    var item = Marshal.PtrToStructure<RAWINPUTDEVICELIST>(
                        listPtr + i * (int)structSize);

                    string name = GetDeviceName(item.hDevice);
                    Log($"  [{i}] type={item.dwType} name={name}");
                }
            }
            finally { Marshal.FreeHGlobal(listPtr); }
        }
        catch (Exception ex) { Log($"EnumerateDevices xato: {ex.Message}"); }
    }

    private static string GetDeviceName(IntPtr hDevice)
    {
        try
        {
            uint sz = 0;
            GetRawInputDeviceInfo(hDevice, RIDI_DEVICENAME, IntPtr.Zero, ref sz);
            if (sz == 0) return "(yo'q)";
            IntPtr buf = Marshal.AllocHGlobal((int)sz * 2);
            try
            {
                GetRawInputDeviceInfo(hDevice, RIDI_DEVICENAME, buf, ref sz);
                return Marshal.PtrToStringUni(buf) ?? "(null)";
            }
            finally { Marshal.FreeHGlobal(buf); }
        }
        catch { return "(xato)"; }
    }

    // ── Ro'yxatdan o'tkazish ─────────────────────────────────────────────────
    private void RegisterUsagePages(IntPtr hwnd)
    {
        // 0x01 Generic Desktop (keyboard, mouse, gamepad, …)
        // 0x0C Consumer (media keys, scan buttons, …)
        // 0x0D Digitizers (pen, touchscreen)
        var devices = new RAWINPUTDEVICE[]
        {
            new() { usUsagePage = 0x01, usUsage = 0x00, dwFlags = RIDEV_PAGEONLY | RIDEV_INPUTSINK, hwndTarget = hwnd },
            new() { usUsagePage = 0x0C, usUsage = 0x00, dwFlags = RIDEV_PAGEONLY | RIDEV_INPUTSINK, hwndTarget = hwnd },
            new() { usUsagePage = 0x0D, usUsage = 0x00, dwFlags = RIDEV_PAGEONLY | RIDEV_INPUTSINK, hwndTarget = hwnd },
        };

        bool ok = RegisterRawInputDevices(
            devices, (uint)devices.Length, (uint)Marshal.SizeOf<RAWINPUTDEVICE>());
        Log($"RegisterRawInputDevices → {ok}  LastError={Marshal.GetLastWin32Error()}");
    }

    // ── WndProc ──────────────────────────────────────────────────────────────
    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WM_INPUT) return IntPtr.Zero;

        uint dwSize = 0;
        uint headerSize = (uint)Marshal.SizeOf<RAWINPUTHEADER>();
        GetRawInputData(lParam, RID_INPUT, IntPtr.Zero, ref dwSize, headerSize);

        if (dwSize == 0) return IntPtr.Zero;

        IntPtr buf = Marshal.AllocHGlobal((int)dwSize);
        try
        {
            if (GetRawInputData(lParam, RID_INPUT, buf, ref dwSize, headerSize) != dwSize)
                return IntPtr.Zero;

            var header = Marshal.PtrToStructure<RAWINPUTHEADER>(buf);
            if (header.dwType != RIM_TYPEHID) return IntPtr.Zero;

            // RAWHID: dwSizeHid (4) + dwCount (4) + bRawData[]
            int offset    = (int)headerSize;
            uint sizeHid  = (uint)Marshal.ReadInt32(buf, offset);
            uint count    = (uint)Marshal.ReadInt32(buf, offset + 4);

            if (count == 0 || sizeHid == 0) return IntPtr.Zero;

            byte[] rawData = new byte[sizeHid * count];
            Marshal.Copy(buf + offset + 8, rawData, 0, rawData.Length);

            string devName = GetDeviceName(header.hDevice);
            string hex     = BitConverter.ToString(rawData);
            Log($"WM_INPUT HID: device={devName}  data={hex}");

            // Scan button ma'lumotlari: har qanday non-zero HID input → ButtonPressed
            // (keyin aniq filtr qo'shamiz)
            bool anyNonZero = false;
            foreach (byte b in rawData) if (b != 0) { anyNonZero = true; break; }
            if (anyNonZero) ButtonPressed?.Invoke();
        }
        finally { Marshal.FreeHGlobal(buf); }

        return IntPtr.Zero;
    }
}
