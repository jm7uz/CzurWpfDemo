using System;
using System.IO;
using System.Threading;
using System.Windows.Threading;
using WIA;

namespace CzurWpfDemo.Services;

/// <summary>
/// CZUR ET24 Pro "Scan" tugmasini WIA event orqali tinglaydi.
/// WIA COM eventi STA thread + message loop talab qiladi —
/// shuning uchun alohida STA thread ishlatiladi.
/// </summary>
public class WiaButtonListener : IDisposable
{
    private Thread?     _staThread;
    private Dispatcher? _staDispatcher;

    public event Action? ScanButtonPressed;

    // Barcha standart WIA scan eventlari
    private static readonly string[] ScanEventIds =
    {
        "{A6C5A715-8C6E-11d2-977A-0000F87A926F}", // wiaEventScanImage
        "{FC4767C1-189F-4AF1-B646-3899F873A4F4}", // wiaEventScanImage2
        "{F23EFE59-ABFE-4C49-9CFC-9C04EB8BEF34}", // wiaEventScanImage3
        "{C00EB793-8C6E-11d2-977A-0000F87A926F}", // wiaEventScanFilmImage
        "{B441F425-8C6E-11d2-977A-0000F87A926F}", // wiaEventScanPrintImage
        "{C00EB794-8C6E-11d2-977A-0000F87A926F}", // wiaEventScanFaxImage
    };

    private static readonly string LogPath = @"C:\czur_wia_debug.txt";

    private static void Log(string msg)
    {
        var line = $"[{DateTime.Now:HH:mm:ss.fff}] {msg}";
        System.Diagnostics.Debug.WriteLine(line);
        try { File.AppendAllText(LogPath, line + Environment.NewLine); } catch { }
    }

    public void Start()
    {
        Log("WiaButtonListener.Start() chaqirildi.");
        try
        {
            _staThread = new Thread(StaRun)
            {
                IsBackground = true,
                Name         = "WIA-STA"
            };
            _staThread.SetApartmentState(ApartmentState.STA);
            _staThread.Start();
            Log("STA thread ishga tushirildi.");
        }
        catch (Exception ex)
        {
            Log($"Start() xato: {ex}");
        }
    }

    [STAThread]
    private void StaRun()
    {
        try
        {
            _staDispatcher = Dispatcher.CurrentDispatcher;

            Log("WIA STA thread boshlandi.");

            var manager = new DeviceManager();

            // Mavjud WIA qurilmalarini ro'yxatga olish
            int devCount = manager.DeviceInfos.Count;
            Log($"WIA qurilmalar soni: {devCount}");
            for (int i = 1; i <= devCount; i++)
            {
                try
                {
                    DeviceInfo info = manager.DeviceInfos[i];
                    Log($"  [{i}] ID={info.DeviceID}  Type={info.Type}");
                    try
                    {
                        var name = (string)info.Properties["Name"].get_Value();
                        Log($"       Name={name}");
                    }
                    catch { }
                }
                catch (Exception ex)
                {
                    Log($"  [{i}] o'qishda xato: {ex.Message}");
                }
            }

            // Barcha scan eventlarini ro'yxatdan o'tkazish
            foreach (var evId in ScanEventIds)
            {
                try
                {
                    manager.RegisterEvent(evId, "*");
                    Log($"RegisterEvent OK: {evId}");
                }
                catch (Exception ex)
                {
                    Log($"RegisterEvent XATO ({evId}): {ex.Message}");
                }
            }

            manager.OnEvent += OnWiaEvent;
            Log("OnEvent ulandi. Dispatcher.Run() boshlanmoqda...");

            Dispatcher.Run();

            // Chiqqanda ro'yxatdan o'chirish
            try
            {
                manager.OnEvent -= OnWiaEvent;
                foreach (var evId in ScanEventIds)
                    try { manager.UnregisterEvent(evId, "*"); } catch { }
            }
            catch { }

            Log("WIA STA thread to'xtatildi.");
        }
        catch (Exception ex)
        {
            Log($"STA thread kritik xato: {ex}");
        }
    }

    private void OnWiaEvent(string eventId, string deviceId, string itemId)
    {
        Log($"OnWiaEvent KELDI! eventId={eventId}  deviceId={deviceId}  itemId={itemId}");
        ScanButtonPressed?.Invoke();
    }

    public void Stop()
    {
        try { _staDispatcher?.InvokeShutdown(); } catch { }
        _staThread  = null;
        _staDispatcher = null;
    }

    public void Dispose() => Stop();
}
