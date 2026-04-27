using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OpenCvSharp;
using CzurWpfDemo.Services;
using ZXing;
using ZXing.Common;
using OcvRect = OpenCvSharp.Rect;

namespace CzurWpfDemo.Views;

public partial class BarcodeScanPage : UserControl
{
    // ─── Kamera holati ──────────────────────────────────────────────
    private VideoCapture? _capture;
    private CancellationTokenSource? _cts;
    private Task? _captureTask;
    private bool _isRunning;

    // ─── Barcode aniqlash ───────────────────────────────────────────
    private bool _isProcessingBarcode;
    private int _frameCounter;
    private string? _lastBarcode;
    private int _barcodeHitCount;
    private bool _barcodeConfirmed;
    private bool _guideInitialized;

    // ─── Corner detection ───────────────────────────────────────────
    private readonly System.Windows.Point[] _cropPoints = new System.Windows.Point[4];

    // ─── FPS ────────────────────────────────────────────────────────
    private readonly Stopwatch _fpsWatch = new();
    private int _fpsFrames;

    // Barcode ROI: kamera kadrida o'ng yuqori burchak (X:55-97%, Y:7-28%)
    private const double RoiX1 = 0.70; // o‘ngroq
    private const double RoiY1 = 0.65; // pastroqqa
    private const double RoiX2 = 0.98;
    private const double RoiY2 = 0.95;

    public BarcodeScanPage()
    {
        InitializeComponent();
        Loaded   += async (_, _) => await StartCameraAsync();
        Unloaded += async (_, _) => await StopCameraAsync();
    }

    public async Task StopCameraAsync()
    {
        _isRunning = false;
        _cts?.Cancel();
        if (_captureTask != null)
            try { await _captureTask; } catch { }
        _capture?.Release();
        _capture?.Dispose();
        _capture = null;
    }

    // ─── Kamerani boshlash ──────────────────────────────────────────
    private async Task StartCameraAsync()
    {
        SetStatus("Kamera ulanmoqda...");

        bool ok = await Task.Run(() =>
        {
            try
            {
                _capture = new VideoCapture(0, VideoCaptureAPIs.DSHOW);
                if (!_capture.IsOpened()) return false;
                _capture.Set(VideoCaptureProperties.FrameWidth,  1920);
                _capture.Set(VideoCaptureProperties.FrameHeight, 1080);
                _capture.Set(VideoCaptureProperties.Fps, 20);
                _capture.Set(VideoCaptureProperties.FourCC, VideoWriter.FourCC('M', 'J', 'P', 'G'));
                _capture.BufferSize = 1;
                return _capture.IsOpened();
            }
            catch { return false; }
        });

        if (!ok)
        {
            SetStatus("❌ Kamera ulanmadi. USB ulanganligi va qurilma menejerini tekshiring.");
            return;
        }

        _isRunning = true;
        _cts = new CancellationTokenSource();
        PlaceholderPanel.Visibility = Visibility.Collapsed;
        CameraImage.Visibility      = Visibility.Visible;
        OverlayCanvas.Visibility    = Visibility.Visible;
        SetStatus("Hujjatni sarlavha tomoni yuqoriga tutib, barcode qutisi ichiga soling");
        _fpsWatch.Restart();
        _captureTask = Task.Run(() => CaptureLoop(_cts.Token));
    }

    // ─── Asosiy kadr olish loopi ─────────────────────────────────────
    private void CaptureLoop(CancellationToken token)
    {
        using var mat = new Mat();

        while (!token.IsCancellationRequested && _isRunning)
        {
            try
            {
                if (_capture == null || !_capture.Read(mat) || mat.Empty())
                {
                    Thread.Sleep(10);
                    continue;
                }

                // FPS hisoblash
                _fpsFrames++;
                if (_fpsWatch.ElapsedMilliseconds >= 1000)
                {
                    var fps = _fpsFrames / (_fpsWatch.ElapsedMilliseconds / 1000.0);
                    _fpsFrames = 0;
                    _fpsWatch.Restart();
                    Dispatcher.InvokeAsync(() => TxtFps.Text = $"{fps:F1} fps");
                }

                // 1. Corner detection
                Point2f[] corners;
                try { corners = FindDocumentCorners(mat); }
                catch { corners = DefaultCorners(mat); }

                // 2. Document crop (ENG MUHIM)
                var doc = WarpDocument(mat, corners);

                int dw = doc.Cols;
                int dh = doc.Rows;

                // 3. Bitmap tayyorlash
                var docBmp = MatToBitmapSource(doc);
                docBmp.Freeze();

                // 4. UI update
                Dispatcher.InvokeAsync(() =>
                {
                    CameraImage.Source = docBmp;

                    CameraGrid.Width = dw;
                    CameraGrid.Height = dh;

                    OverlayCanvas.Width = dw;
                    OverlayCanvas.Height = dh;

                    // Polygon endi kerak emas (optional)
                    LiveCropPolygon.Visibility = Visibility.Collapsed;

                    if (!_guideInitialized)
                    {
                        InitGuideOverlay(dw, dh);
                        _guideInitialized = true;
                    }
                });

                // 5. Barcode detection (doc ichidan!)
                _frameCounter++;
                if (_frameCounter % 3 == 0 && !_isProcessingBarcode && !_barcodeConfirmed)
                {
                    var clone = doc.Clone(); // MUHIM: thread safe
                    _ = TryDetectBarcodeAsync(clone);
                }
            }
            catch (OperationCanceledException) { break; }
            catch
            {
                Thread.Sleep(30);
            }
        }
    }
    private static (string? text, ResultPoint[]? points) DecodeWithPoints(byte[] data, int width, int height)
    {
        var reader = new BarcodeReaderGeneric
        {
            AutoRotate = false,
            Options = new DecodingOptions
            {
                TryHarder = true,
                PossibleFormats = new List<BarcodeFormat>
            {
                BarcodeFormat.EAN_13,
                BarcodeFormat.CODE_128,
                BarcodeFormat.CODE_39,
                BarcodeFormat.ITF,
                BarcodeFormat.QR_CODE
            }
            }
        };

        var result = reader.Decode(data, width, height, RGBLuminanceSource.BitmapFormat.Gray8);

        return (result?.Text, result?.ResultPoints);
    }

    // ─── ROI kesish → kattalashtirish → xavfsiz decode ──────────────
    private async Task TryDetectBarcodeAsync(Mat full)
    {
        _isProcessingBarcode = true;

        try
        {
            var (detected, rect) = await Task.Run(() => ExtractAndDecode(full));

            if (detected == null) return;

            // 🔥 KVADRATNI HARAKATLANTIRISH
            if (rect != null)
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    BarcodeRoiRect.Width = rect.Value.Width;
                    BarcodeRoiRect.Height = rect.Value.Height;

                    Canvas.SetLeft(BarcodeRoiRect, rect.Value.X);
                    Canvas.SetTop(BarcodeRoiRect, rect.Value.Y);

                    BarcodeRoiRect.Visibility = Visibility.Visible;
                });
            }

            if (detected == _lastBarcode) _barcodeHitCount++;
            else { _lastBarcode = detected; _barcodeHitCount = 1; }

            if (_barcodeHitCount >= 2)
                await Dispatcher.InvokeAsync(() => OnBarcodeConfirmed(detected));
        }
        finally
        {
            _isProcessingBarcode = false;
        }
    }

    private static (string? text, System.Windows.Rect? rect) ExtractAndDecode(Mat full)
    {
        try
        {
            using var gray = new Mat();
            Cv2.CvtColor(full, gray, ColorConversionCodes.BGR2GRAY);

            var data = new byte[gray.Rows * gray.Cols];
            System.Runtime.InteropServices.Marshal.Copy(gray.Data, data, 0, data.Length);

            var (text, points) = DecodeWithPoints(data, gray.Cols, gray.Rows);

            if (text == null || points == null || points.Length == 0)
                return (null, null);

            var rect = GetBoundingRect(points);
            return (text, rect);
        }
        catch
        {
            return (null, null);
        }
        finally
        {
            full.Dispose();
        }
    }
    private static System.Windows.Rect GetBoundingRect(ResultPoint[] points)
    {
        float minX = points.Min(p => p.X);
        float minY = points.Min(p => p.Y);
        float maxX = points.Max(p => p.X);
        float maxY = points.Max(p => p.Y);

        return new System.Windows.Rect(minX, minY, maxX - minX, maxY - minY);
    }
    // ─── ZXing orqali grayscale byte array dan barcode o'qish ────────
    private static string? DecodeFromGrayBytes(byte[] data, int width, int height)
    {
        var hints = new Dictionary<DecodeHintType, object>
        {
            [DecodeHintType.POSSIBLE_FORMATS] = new List<BarcodeFormat>
        {
            BarcodeFormat.EAN_13, BarcodeFormat.CODE_128,
            BarcodeFormat.CODE_39, BarcodeFormat.ITF, BarcodeFormat.QR_CODE
        },
            [DecodeHintType.TRY_HARDER] = true
        };

        var reader = new MultiFormatReader();

        // 1. Normal
        try
        {
            var src = new GrayBytesLuminance(data, width, height);
            var res = reader.decode(new BinaryBitmap(new HybridBinarizer(src)), hints);
            if (res?.Text != null) return res.Text;
        }
        catch { }

        // 2. 90° rotate
        try
        {
            var rot = RotateGray90(data, width, height);
            var src = new GrayBytesLuminance(rot, height, width);
            var res = reader.decode(new BinaryBitmap(new HybridBinarizer(src)), hints);
            if (res?.Text != null) return res.Text;
        }
        catch { }

        // 3. 270° rotate (eng muhim!)
        try
        {
            var rot = RotateGray270(data, width, height);
            var src = new GrayBytesLuminance(rot, height, width);
            var res = reader.decode(new BinaryBitmap(new HybridBinarizer(src)), hints);
            if (res?.Text != null) return res.Text;
        }
        catch { }

        return null;
    }
    private static byte[] RotateGray270(byte[] src, int w, int h)
    {
        var dst = new byte[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                dst[(w - 1 - x) * h + y] = src[y * w + x];
        return dst;
    }
    // Grayscale rasmni 90° soat yo'nalishi bo'yicha aylantirish
    private static byte[] RotateGray90(byte[] src, int w, int h)
    {
        var dst = new byte[w * h];
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
            dst[x * h + (h - 1 - y)] = src[y * w + x];
        return dst;
    }

    // ─── Barcode tasdiqlandi ─────────────────────────────────────────
    private async void OnBarcodeConfirmed(string barcode)
    {
        if (_barcodeConfirmed) return;
        _barcodeConfirmed = true;
        _isRunning = false;
        _cts?.Cancel();

        TxtOverlayBarcode.Text = barcode;
        DetectedOverlay.Visibility = Visibility.Visible;
        SetStatus($"Barcode aniqlandi: {barcode} — shartnoma yuklanmoqda...");

        try
        {
            var validation = await GetContractService.ValidateAsync(barcode);
            if (validation?.Status != true)
            {
                SetStatus($"❌ Barcode topilmadi: {barcode}");
                ResetDetection(); return;
            }

            var all      = await GetContractService.SearchAllAsync(barcode);
            var contract = all?.Resoult?.Data?.FirstOrDefault();
            if (contract == null)
            {
                SetStatus("❌ Shartnoma ma'lumotlari topilmadi.");
                ResetDetection(); return;
            }

            await StopCameraAsync();
            AppShell.Current?.NavigateReplace(new ScannerPage(contract));
        }
        catch (Exception ex)
        {
            SetStatus($"❌ Xatolik: {ex.Message}");
            ResetDetection();
        }
    }

    private void ResetDetection()
    {
        _barcodeConfirmed = false;
        _lastBarcode      = null;
        _barcodeHitCount  = 0;
        DetectedOverlay.Visibility   = Visibility.Collapsed;
        BarcodeFoundBadge.Visibility = Visibility.Collapsed;
        _isRunning = true;
        _cts       = new CancellationTokenSource();
        _captureTask = Task.Run(() => CaptureLoop(_cts.Token));
        SetStatus("Hujjatni sarlavha tomoni yuqoriga tutib, barcode qutisi ichiga soling");
    }

    // ─── Guide qutisi (birinchi kadrda chiziladi) ────────────────────
    private void InitGuideOverlay(int fw, int fh)
    {
        double rx = fw * RoiX1, ry = fh * RoiY1;
        double rw = fw * (RoiX2 - RoiX1);
        double rh = fh * (RoiY2 - RoiY1);

        BarcodeRoiRect.Width  = rw;
        BarcodeRoiRect.Height = rh;
        System.Windows.Controls.Canvas.SetLeft(BarcodeRoiRect, rx);
        System.Windows.Controls.Canvas.SetTop(BarcodeRoiRect,  ry);

        double fontSize = Math.Max(14, fw * 0.012);
        BarcodeRoiLabel.FontSize = fontSize;
        System.Windows.Controls.Canvas.SetLeft(BarcodeRoiLabel, rx);
        System.Windows.Controls.Canvas.SetTop(BarcodeRoiLabel,  Math.Max(0, ry - fontSize * 1.8));
    }

    // ─── Corner detection ────────────────────────────────────────────
    private static Point2f[] FindDocumentCorners(Mat img)
    {
        using var gray    = new Mat(); Cv2.CvtColor(img, gray, ColorConversionCodes.BGR2GRAY);
        using var blurred = new Mat(); Cv2.BilateralFilter(gray, blurred, 9, 75, 75);
        using var edged   = new Mat(); Cv2.Canny(blurred, edged, 30, 100);
        using var kernel  = Cv2.GetStructuringElement(MorphShapes.Rect, new OpenCvSharp.Size(15, 15));
        using var closed  = new Mat(); Cv2.MorphologyEx(edged, closed, MorphTypes.Close, kernel);

        Cv2.FindContours(closed, out var contours, out _,
            RetrievalModes.External, ContourApproximationModes.ApproxSimple);

        if (contours.Length > 0)
        {
            var sorted = contours.OrderByDescending(c => Cv2.ContourArea(c)).Take(5).ToList();
            foreach (var c in sorted)
            {
                var peri   = Cv2.ArcLength(c, true);
                var approx = Cv2.ApproxPolyDP(c, 0.04 * peri, true);
                if (approx.Length == 4 && Cv2.ContourArea(approx) > img.Width * img.Height * 0.1)
                    return OrderPoints(approx.Select(p => new Point2f(p.X, p.Y)).ToArray());
            }
            var largest = sorted.FirstOrDefault();
            if (largest != null && Cv2.ContourArea(largest) > img.Width * img.Height * 0.1)
                return OrderPoints(Cv2.MinAreaRect(largest).Points());
        }
        return DefaultCorners(img);
    }

    private static Point2f[] DefaultCorners(Mat img)
    {
        int mx = (int)(img.Width * 0.1), my = (int)(img.Height * 0.1);
        return new[]
        {
            new Point2f(mx, my), new Point2f(img.Width - mx, my),
            new Point2f(img.Width - mx, img.Height - my), new Point2f(mx, img.Height - my)
        };
    }

    private static Point2f[] OrderPoints(Point2f[] pts)
    {
        var sum  = pts.Select(p => p.X + p.Y).ToArray();
        var diff = pts.Select(p => p.Y - p.X).ToArray();
        return new[]
        {
            pts[Array.IndexOf(sum,  sum.Min())],
            pts[Array.IndexOf(diff, diff.Min())],
            pts[Array.IndexOf(sum,  sum.Max())],
            pts[Array.IndexOf(diff, diff.Max())]
        };
    }

    // ─── UI yordamchi ────────────────────────────────────────────────
    private void UpdatePolygon()
    {
        LiveCropPolygon.Points.Clear();
        foreach (var p in _cropPoints) LiveCropPolygon.Points.Add(p);
    }

    private void SetStatus(string msg) => TxtStatus.Text = msg;

    private void BtnBack_Click(object sender, RoutedEventArgs e)
        => AppShell.Current?.GoBack();

    private static BitmapSource MatToBitmapSource(Mat mat)
    {
        using var rgb = new Mat();
        Cv2.CvtColor(mat, rgb, ColorConversionCodes.BGR2RGB);
        return BitmapSource.Create(rgb.Cols, rgb.Rows, 96, 96,
            PixelFormats.Rgb24, null,
            rgb.Data, rgb.Rows * rgb.Cols * 3, rgb.Cols * 3);
    }
    private static Mat WarpDocument(Mat src, Point2f[] corners)
    {
        float widthA = Distance(corners[2], corners[3]);
        float widthB = Distance(corners[1], corners[0]);
        float maxWidth = Math.Max(widthA, widthB);

        float heightA = Distance(corners[1], corners[2]);
        float heightB = Distance(corners[0], corners[3]);
        float maxHeight = Math.Max(heightA, heightB);

        var dstPts = new[]
        {
        new Point2f(0, 0),
        new Point2f(maxWidth - 1, 0),
        new Point2f(maxWidth - 1, maxHeight - 1),
        new Point2f(0, maxHeight - 1)
    };

        var matrix = Cv2.GetPerspectiveTransform(corners, dstPts);
        var warped = new Mat();
        Cv2.WarpPerspective(src, warped, matrix, new OpenCvSharp.Size(maxWidth, maxHeight));

        return warped;
    }

    private static float Distance(Point2f p1, Point2f p2)
    {
        return (float)Math.Sqrt(Math.Pow(p1.X - p2.X, 2) + Math.Pow(p1.Y - p2.Y, 2));
    }
}

// ─── ZXing LuminanceSource: OpenCvSharp GetRawData() dan xavfsiz ────
internal sealed class GrayBytesLuminance : LuminanceSource
{
    private readonly byte[] _data;

    public GrayBytesLuminance(byte[] data, int width, int height) : base(width, height)
        => _data = data;

    public override byte[] getRow(int y, byte[]? row)
    {
        if (row == null || row.Length < Width) row = new byte[Width];
        int offset = y * Width;
        int len = Math.Min(Width, _data.Length - offset);
        if (len > 0) Array.Copy(_data, offset, row, 0, len);
        return row;
    }

    public override byte[] Matrix => _data;
}
