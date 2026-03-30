using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using OpenCvSharp;
using PdfSharpCore.Pdf;
using PdfSharpCore.Drawing;

namespace CzurWpfDemo
{
    /// <summary>
    /// CZUR ET24 Pro — WPF bilan UVC orqali integratsiya.
    /// 
    /// ESLATMA: CZUR ET24 Pro Windows'da UVC kamera sifatida ko'rinadi.
    /// Shuning uchun rasmiy SDK kerak emas — OpenCvSharp4 orqali ishlaydi.
    /// 
    /// NuGet paketlari:
    ///   dotnet add package OpenCvSharp4
    ///   dotnet add package OpenCvSharp4.runtime.win
    ///   dotnet add package OpenCvSharp4.Extensions
    /// </summary>
    public partial class MainWindow : System.Windows.Window
    {
        // ─── Kamera holati ───────────────────────────────────────────
        private VideoCapture? _capture;
        private CancellationTokenSource? _cts;
        private Task? _captureTask;
        private bool _isRunning;
        private bool _isManualCropMode;
        private Mat? _lastMat;
        private readonly object _matLock = new();

        // ─── Saqlangan rasmlar (Yuqori aniqlikdagi original Mat) ─────────
        private readonly List<Mat> _scannedPages = new();
        private int _captureCount;
        private readonly System.Windows.Point[] _cropPoints = new System.Windows.Point[4];

        // ─── FPS hisoblash ───────────────────────────────────────────
        private readonly Stopwatch _fpsStopwatch = new();
        private int _frameCount;
        private double _currentFps;

        // ─── Razresheniyalar xaritasi ─────────────────────────────────
        private readonly (int Width, int Height)[] _resolutions =
        {
            (5696, 4272),   // 24MP ET24 Pro (Maksimal Sifat) - 1-2 fps
            (4000, 3000),   // 12MP (Yuqori Sifat)
            (3072, 1728),   // CZUR Taqdimot rejimi — 12fps
            (1920, 1080),   // Standart HD
            (1536, 1152),   // CZUR Skan rejimi — 20fps
        };

        public MainWindow()
        {
            InitializeComponent();
            Log("CZUR ET24 Pro WPF Demo yuklandi. Qurilmani USB orqali ulang.");
        }

        // ─── Kamerani Boshlash ────────────────────────────────────────
        private async void BtnStart_Click(object sender, RoutedEventArgs e)
        {
            int cameraIndex = CmbCameraIndex.SelectedIndex;
            var (width, height) = _resolutions[CmbResolution.SelectedIndex];

            SetStatus("Ulanyapti...", "#F59E0B");
            Log($"Kamera {cameraIndex} ochilmoqda ({width}×{height})...");

            BtnStart.IsEnabled = false;

            bool success = await Task.Run(() => InitCamera(cameraIndex, width, height));

            if (success)
            {
                _isRunning = true;
                _cts = new CancellationTokenSource();

                PlaceholderPanel.Visibility = Visibility.Collapsed;
                CameraImage.Visibility = Visibility.Visible;

                BtnStop.IsEnabled = true;
                BtnManualCrop.IsEnabled = true;
                _isManualCropMode = false;
                BtnCapture.IsEnabled = true;
                
                LiveCropCanvas.Visibility = Visibility.Visible;

                SetStatus("Jonli efir", "#10B981");
                Log($"✅ Kamera muvaffaqiyatli ulandi. {width}×{height} @ CZUR UVC rejimi.");

                _fpsStopwatch.Restart();
                _frameCount = 0;
                
                foreach (var mat in _scannedPages) mat.Dispose();
                _scannedPages.Clear();
                _captureCount = 0;
                TxtCaptureCount.Text = "0";
                BtnSave.IsEnabled = false;
                CapturedImage.Source = null;

                _captureTask = Task.Run(() => CaptureLoop(_cts.Token));
            }
            else
            {
                BtnStart.IsEnabled = true;
                SetStatus("Xatolik", "#EF4444");
                Log("❌ Kamera ulanmadi. Qurilma ulanganligi va indeksi to'g'riligini tekshiring.");

                MessageBox.Show(
                    "Kamera ochilmadi!\n\n" +
                    "Tekshiring:\n" +
                    "• CZUR ET24 Pro USB ga ulangan\n" +
                    "• Qurilma menejerida ko'rinadi\n" +
                    "• Boshqa dastur kamerani ishlatmayapti\n" +
                    "• Kamera indeksi to'g'ri (0, 1, 2...)",
                    "Xatolik",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        // ─── Kamerani To'xtatish ─────────────────────────────────────
        private async void BtnStop_Click(object sender, RoutedEventArgs e)
        {
            _isRunning = false;
            _cts?.Cancel();

            if (_captureTask != null)
                await _captureTask;

            _capture?.Release();
            _capture?.Dispose();
            _capture = null;

            Dispatcher.Invoke(() =>
            {
                CameraImage.Visibility = Visibility.Collapsed;
                LiveCropCanvas.Visibility = Visibility.Collapsed;
                
                PlaceholderPanel.Visibility = Visibility.Visible;
                BtnStart.IsEnabled = true;
                BtnStop.IsEnabled = false;
                BtnManualCrop.IsEnabled = false;
                _isManualCropMode = false;
                BtnManualCrop.Content = "🖐️  Qo'lda tahrirlash";
                
                BtnCapture.IsEnabled = false;
                TxtFps.Text = "—";
                SetStatus("To'xtatildi", "#9CA3AF");
                
                foreach (var mat in _scannedPages) mat.Dispose();
                _scannedPages.Clear();
                _captureCount = 0;
                TxtCaptureCount.Text = "0";
                CapturedImage.Source = null;
                BtnSave.IsEnabled = false;
                
                Log("⏹ Kamera to'xtatildi.");
            });
        }

        // ─── Kamera inizializatsiyasi ─────────────────────────────────
        private bool InitCamera(int index, int width, int height)
        {
            try
            {
                // DirectShow orqali (Windows'da UVC kameralar uchun eng yaxshi)
                _capture = new VideoCapture(index, VideoCaptureAPIs.DSHOW);

                if (!_capture.IsOpened())
                    return false;

                // CZUR ET24 Pro uchun maksimal sozlamalar - Tiniqlik uchun 
                // Aslida kamera maksimal darajasi bo'yicha ulanishi uchun 4K / eng yuqori resolutionga harakat qilamiz
                _capture.Set(VideoCaptureProperties.FrameWidth, width);
                _capture.Set(VideoCaptureProperties.FrameHeight, height);
                
                // 24MP katta hajmli tasvirlar uchun kamera tezlikni kutmasligi uchun (shartli kadr chastotasi)
                if (width >= 4000)
                    _capture.Set(VideoCaptureProperties.Fps, 2);
                else
                    _capture.Set(VideoCaptureProperties.Fps, 20);

                // MJPG format — CZUR uchun eng yaxshi sifat/tezlik nisbati
                _capture.Set(VideoCaptureProperties.FourCC, VideoWriter.FourCC('M', 'J', 'P', 'G'));

                // Buffer minimallashtirilsin (jonli efir uchun)
                _capture.BufferSize = 1;

                return _capture.IsOpened();
            }
            catch
            {
                return false;
            }
        }

        // ─── Asosiy Kadr Olish Loop ───────────────────────────────────
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

                    lock (_matLock)
                    {
                        _lastMat?.Dispose();
                        _lastMat = mat.Clone();
                    }

                    // FPS hisoblash
                    _frameCount++;
                    if (_fpsStopwatch.ElapsedMilliseconds >= 1000)
                    {
                        _currentFps = _frameCount / (_fpsStopwatch.ElapsedMilliseconds / 1000.0);
                        _frameCount = 0;
                        _fpsStopwatch.Restart();
                    }

                    // UI ni yangilash va Live Crop izlash
                    var bitmapSource = MatToBitmapSource(mat);
                    bitmapSource.Freeze();

                    // Avtomatik chegara nuqtalarini fonda topish (Agar qo'lda tahrirlash rejimida bo'lmasa)
                    System.Windows.Point[]? liveCorners = null;
                    if (!_isManualCropMode)
                    {
                        var corners = FindDocumentCorners(mat);
                        liveCorners = corners.Select(c => new System.Windows.Point(c.X, c.Y)).ToArray();
                    }

                    Dispatcher.InvokeAsync(() =>
                    {
                        CameraImage.Source = bitmapSource;
                        
                        // O'lchamlarni sinxronlashtirish
                        if (mat.Width > 0 && mat.Height > 0)
                        {
                            LiveCropCanvas.Width = mat.Width;
                            LiveCropCanvas.Height = mat.Height;
                            
                            CameraGrid.Width = mat.Width;
                            CameraGrid.Height = mat.Height;
                        }

                        if (liveCorners != null)
                        {
                            for (int i = 0; i < 4; i++)
                                _cropPoints[i] = liveCorners[i];

                            UpdatePolygon();
                        }

                        TxtFps.Text = $"{_currentFps:F1} fps";
                    });
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Dispatcher.InvokeAsync(() => Log($"⚠ Kadr xatosi: {ex.Message}"));
                    Thread.Sleep(100);
                }
            }
        }

        // ─── Rasm Olish (Live Crop asosida srazi qirqish) ───────────────────
        private void BtnCapture_Click(object sender, RoutedEventArgs e)
        {
            Mat captureMat;
            lock (_matLock)
            {
                if (_lastMat == null || _lastMat.Empty()) return;
                captureMat = _lastMat.Clone();
            }

            var srcPts = new[] {
                new Point2f((float)_cropPoints[0].X, (float)_cropPoints[0].Y),
                new Point2f((float)_cropPoints[1].X, (float)_cropPoints[1].Y),
                new Point2f((float)_cropPoints[2].X, (float)_cropPoints[2].Y),
                new Point2f((float)_cropPoints[3].X, (float)_cropPoints[3].Y)
            };

            double widthTop = Math.Sqrt(Math.Pow(srcPts[1].X - srcPts[0].X, 2) + Math.Pow(srcPts[1].Y - srcPts[0].Y, 2));
            double widthBottom = Math.Sqrt(Math.Pow(srcPts[2].X - srcPts[3].X, 2) + Math.Pow(srcPts[2].Y - srcPts[3].Y, 2));
            int maxWidth = Math.Max((int)widthTop, (int)widthBottom);

            double heightLeft = Math.Sqrt(Math.Pow(srcPts[3].X - srcPts[0].X, 2) + Math.Pow(srcPts[3].Y - srcPts[0].Y, 2));
            double heightRight = Math.Sqrt(Math.Pow(srcPts[2].X - srcPts[1].X, 2) + Math.Pow(srcPts[2].Y - srcPts[1].Y, 2));
            int maxHeight = Math.Max((int)heightLeft, (int)heightRight);

            if (maxWidth <= 0 || maxHeight <= 0)
            {
                captureMat.Dispose();
                return;
            }

            var dstPts = new[] {
                new Point2f(0, 0),
                new Point2f(maxWidth - 1, 0),
                new Point2f(maxWidth - 1, maxHeight - 1),
                new Point2f(0, maxHeight - 1)
            };

            using var transform = Cv2.GetPerspectiveTransform(srcPts, dstPts);
            using var warped = new Mat();
            Cv2.WarpPerspective(captureMat, warped, transform, new OpenCvSharp.Size(maxWidth, maxHeight));

            using var enhanced = EnhanceDocumentClarity(warped);

            // Original tahrirlangan qog'ozni xotiraga olish
            _scannedPages.Add(enhanced.Clone());
            _captureCount++;

            var bmp = MatToBitmapSource(enhanced);
            bmp.Freeze();
            CapturedImage.Source = bmp;
            TxtCaptureCount.Text = _captureCount.ToString();
            BtnSave.IsEnabled = true;

            captureMat.Dispose();
            warped.Dispose();
            enhanced.Dispose();
            
            // Orqaga avto rejimga qaytarib qo'yish (agar qo'lda tahrirlashda tursa)
            if (_isManualCropMode)
            {
                BtnManualCrop_Click(null, null);
            }

            Log($"📸 Skan #{_captureCount} kesildi va hujjatga qo'shildi.");
        }

        // ─── Manual Crop (Nuqtalarni qo'lda surish rejimini yoqish/o'chirish) ───────────
        private void BtnManualCrop_Click(object sender, RoutedEventArgs e)
        {
            if (!_isManualCropMode)
            {
                _isManualCropMode = true;
                // Avtokadr to'xtatilib interaktiv Thumb pufakchalar ko'rinadi
                BtnManualCrop.Content = "🤖  Avtomatik topish";
                BtnManualCrop.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(245, 158, 11)); // Amber
                
                ThumbTL.Visibility = Visibility.Visible;
                ThumbTR.Visibility = Visibility.Visible;
                ThumbBR.Visibility = Visibility.Visible;
                ThumbBL.Visibility = Visibility.Visible;
                
                System.Windows.Controls.Canvas.SetLeft(ThumbTL, _cropPoints[0].X - ThumbTL.Width / 2);
                System.Windows.Controls.Canvas.SetTop(ThumbTL, _cropPoints[0].Y - ThumbTL.Height / 2);
                
                System.Windows.Controls.Canvas.SetLeft(ThumbTR, _cropPoints[1].X - ThumbTR.Width / 2);
                System.Windows.Controls.Canvas.SetTop(ThumbTR, _cropPoints[1].Y - ThumbTR.Height / 2);
                
                System.Windows.Controls.Canvas.SetLeft(ThumbBR, _cropPoints[2].X - ThumbBR.Width / 2);
                System.Windows.Controls.Canvas.SetTop(ThumbBR, _cropPoints[2].Y - ThumbBR.Height / 2);
                
                System.Windows.Controls.Canvas.SetLeft(ThumbBL, _cropPoints[3].X - ThumbBL.Width / 2);
                System.Windows.Controls.Canvas.SetTop(ThumbBL, _cropPoints[3].Y - ThumbBL.Height / 2);
                Log("✋ Qo'lda tahrirlash yoqildi. Nuqtalarni ushlab kerakli joyga suring, so'ng Rasmga olish bosing.");
            }
            else
            {
                _isManualCropMode = false;
                // Orqaga qaytish
                BtnManualCrop.Content = "🖐️  Qo'lda tahrirlash";
                BtnManualCrop.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(139, 92, 246)); // Violet
                
                ThumbTL.Visibility = Visibility.Collapsed;
                ThumbTR.Visibility = Visibility.Collapsed;
                ThumbBR.Visibility = Visibility.Collapsed;
                ThumbBL.Visibility = Visibility.Collapsed;
                Log("🤖 Avto kuzatish rejimi yoqildi.");
            }
        }

        private void Thumb_DragDelta(object sender, System.Windows.Controls.Primitives.DragDeltaEventArgs e)
        {
            if (sender is System.Windows.Controls.Primitives.Thumb thumb && int.TryParse(thumb.Tag?.ToString(), out int index))
            {
                double newX = _cropPoints[index].X + e.HorizontalChange;
                double newY = _cropPoints[index].Y + e.VerticalChange;

                newX = Math.Max(0, Math.Min(newX, LiveCropCanvas.Width));
                newY = Math.Max(0, Math.Min(newY, LiveCropCanvas.Height));

                _cropPoints[index] = new System.Windows.Point(newX, newY);
                System.Windows.Controls.Canvas.SetLeft(thumb, newX - thumb.Width / 2);
                System.Windows.Controls.Canvas.SetTop(thumb, newY - thumb.Height / 2);

                UpdatePolygon();
            }
        }

        private void UpdatePolygon()
        {
            LiveCropPolygon.Points.Clear();
            foreach (var p in _cropPoints) LiveCropPolygon.Points.Add(p);
        }

        // ─── Tasvir Tiniqligini Oshirish (Image Enhancement) ─────────
        private Mat EnhanceDocumentClarity(Mat src)
        {
            // 1. Matnni aniqroq qilish uchun Unsharp Mask (Sharpening)
            using var blurred = new Mat();
            Cv2.GaussianBlur(src, blurred, new OpenCvSharp.Size(0, 0), 3);
            
            var sharpened = new Mat();
            Cv2.AddWeighted(src, 1.5, blurred, -0.5, 0, sharpened);

            // 2. Yoritilishni to'g'irlash uchun CLAHE (Adaptive Histogram Equalization)
            // L*a*b rang fazosiga o'tib, faqat L (Yorug'lik) kanalini optimallashtiramiz
            using var lab = new Mat();
            Cv2.CvtColor(sharpened, lab, ColorConversionCodes.BGR2Lab);
            
            var labChannels = Cv2.Split(lab);
            
            using var clahe = Cv2.CreateCLAHE(clipLimit: 2.0, tileGridSize: new OpenCvSharp.Size(8, 8));
            clahe.Apply(labChannels[0], labChannels[0]);
            
            Cv2.Merge(labChannels, lab);
            Cv2.CvtColor(lab, sharpened, ColorConversionCodes.Lab2BGR);

            // Xotira siqilishini oldini olish
            foreach (var ch in labChannels) ch.Dispose();

            return sharpened;
        }

        // ─── OpenCV Kesish Mantiqi (Avto) ────────────────────────────
        private Point2f[] FindDocumentCorners(Mat img)
        {
            using var gray = new Mat();
            Cv2.CvtColor(img, gray, ColorConversionCodes.BGR2GRAY);
            
            // Xira shovqinni ketkazish (Bilateral Filter chegaralarni qoldirib ichini tekislaydi)
            using var blurred = new Mat();
            Cv2.BilateralFilter(gray, blurred, 9, 75, 75);
            
            // Qora fon va oq qog'ozni ajratib olish
            using var edged = new Mat();
            Cv2.Canny(blurred, edged, 30, 100);

            // Matn sabab qog'oz ichki maydoni yorilib kontur buzilmasligi uchun, yopish (Close)
            using var kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new OpenCvSharp.Size(15, 15));
            using var closed = new Mat();
            Cv2.MorphologyEx(edged, closed, MorphTypes.Close, kernel);

            // RetrievalModes.External -> eng zo'r yechim: FAQAT TASHQI chekkalarni aniqlaydi, ichki yozuvlar bekor qilinadi
            Cv2.FindContours(closed, out var contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
            
            if (contours.Length > 0)
            {
                var contours_sorted = contours.OrderByDescending(c => Cv2.ContourArea(c)).Take(5);
                foreach (var c in contours_sorted)
                {
                    var peri = Cv2.ArcLength(c, true);
                    // Haqiqiy qog'ozning shaklini qidirish (0.04-0.05 gacha qattiqlik qog'oz chekkalarini kesishi osonroq qiladi)
                    var approx = Cv2.ApproxPolyDP(c, 0.04 * peri, true);
                    
                    // Katta formatli va 4 burchakli figura bo'lsa darhol uzatish 
                    if (approx.Length == 4 && Cv2.ContourArea(approx) > (img.Width * img.Height * 0.1))
                    {
                        var pts = approx.Select(p => new Point2f(p.X, p.Y)).ToArray();
                        return OrderPoints(pts);
                    }
                }

                // Fallback: Agar hujjatning qaysidir burchagi qiyshiq bo'lib 5 burchak hosil qilgan bo'lsa (Poligon o'xshamasa)
                // Eng katta aniqlangan maydon ustiga to'la to'rtburchak tortib yuboramiz (MinAreaRect)
                var largestContour = contours_sorted.FirstOrDefault();
                if (largestContour != null && Cv2.ContourArea(largestContour) > (img.Width * img.Height * 0.1))
                {
                    var rect = Cv2.MinAreaRect(largestContour);
                    var pts = rect.Points();
                    return OrderPoints(pts);
                }
            }
            
            // Xech qanday hujjat topilmasa o'zini qirqaladigan maydon (Padding bilan) ko'rsatamiz
            int marginX = (int)(img.Width * 0.1);
            int marginY = (int)(img.Height * 0.1);
            return new[] {
                new Point2f(marginX, marginY),
                new Point2f(img.Width - marginX, marginY),
                new Point2f(img.Width - marginX, img.Height - marginY),
                new Point2f(marginX, img.Height - marginY)
            };
        }

        private Point2f[] OrderPoints(Point2f[] pts)
        {
            var ordered = new Point2f[4];
            var sum = pts.Select(p => p.X + p.Y).ToArray();
            ordered[0] = pts[Array.IndexOf(sum, sum.Min())]; // Top-Left
            ordered[2] = pts[Array.IndexOf(sum, sum.Max())]; // Bottom-Right

            var diff = pts.Select(p => p.Y - p.X).ToArray();
            ordered[1] = pts[Array.IndexOf(diff, diff.Min())]; // Top-Right
            ordered[3] = pts[Array.IndexOf(diff, diff.Max())]; // Bottom-Left
            
            return ordered;
        }

        // ─── PDF qilib Saqlash ─────────────────────────────────────────
        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            if (_scannedPages.Count == 0) return;

            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Title = "PDF qilib saqlash",
                Filter = "PDF hujjat|*.pdf",
                FileName = $"CZUR_Document_{DateTime.Now:yyyyMMdd_HHmmss}",
                DefaultExt = ".pdf"
            };

            if (dlg.ShowDialog() == true)
            {
                try
                {
                    using (var document = new PdfDocument())
                    {
                        foreach (var matPage in _scannedPages)
                        {
                            // Vaqtincha fayl yaratamiz (Maksimal sifat bilan - PNG losslless)
                            var tempFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".png");
                            
                            // To'g'ridan to'g'ri OpenCV orqali kompressiyasiz chiroyli saqlash
                            Cv2.ImWrite(tempFile, matPage);

                            using (var xImage = XImage.FromFile(tempFile))
                            {
                                var page = document.AddPage();
                                // XImage.PixelWidth bu original o'lcham
                                // Sahifani aynan rasmning haqiqiy kattaligidek sozlaymiz
                                page.Width = XUnit.FromPoint(xImage.PointWidth * (72.0 / xImage.HorizontalResolution));
                                page.Height = XUnit.FromPoint(xImage.PointHeight * (72.0 / xImage.VerticalResolution));

                                using var gfx = XGraphics.FromPdfPage(page);
                                // Maksimal original o'lchamga chizish
                                gfx.DrawImage(xImage, 0, 0, page.Width, page.Height);
                            }
                            
                            File.Delete(tempFile);
                        }
                        
                        document.Save(dlg.FileName);
                    }

                    Log($"💾 PDF saqlandi: {dlg.FileName}");
                    MessageBox.Show($"Hujjat muvaffaqiyatli PDF qilib saqlandi!\nJami sahifalar: {_scannedPages.Count}", "Saqlandi",
                                    MessageBoxButton.OK, MessageBoxImage.Information);
                    
                    foreach (var mat in _scannedPages) mat.Dispose();
                    _scannedPages.Clear();
                    _captureCount = 0;
                    TxtCaptureCount.Text = "0";
                    BtnSave.IsEnabled = false;
                    CapturedImage.Source = null;
                }
                catch (Exception ex)
                {
                    Log($"❌ Saqlash xatosi: {ex.Message}");
                }
            }
        }

        // ─── Yordamchi Metodlar ───────────────────────────────────────

        /// <summary>
        /// OpenCV Mat → WPF BitmapSource
        /// </summary>
        private static BitmapSource MatToBitmapSource(Mat mat)
        {
            using var rgbMat = new Mat();
            Cv2.CvtColor(mat, rgbMat, ColorConversionCodes.BGR2RGB);

            return BitmapSource.Create(
                rgbMat.Cols, rgbMat.Rows,
                300, 300, // 96 DPI o'rniga 300 DPI UI da ishlatish uchungina, asosiysi .png qilib yozilganda yo'qolmasligi muhim
                System.Windows.Media.PixelFormats.Rgb24,
                null,
                rgbMat.Data,
                rgbMat.Rows * rgbMat.Cols * 3,
                rgbMat.Cols * 3);
        }

        private void SetStatus(string text, string colorHex)
        {
            TxtStatus.Text = text;
            TxtStatus.Foreground = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(colorHex));
        }

        private void Log(string message)
        {
            TxtLog.Text = $"[{DateTime.Now:HH:mm:ss}] {message}";
        }

        protected override async void OnClosed(EventArgs e)
        {
            _isRunning = false;
            _cts?.Cancel();
            if (_captureTask != null) await _captureTask;
            _capture?.Dispose();
            base.OnClosed(e);
        }
    }
}