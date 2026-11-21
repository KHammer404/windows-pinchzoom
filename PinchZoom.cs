using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Microsoft.Win32.SafeHandles;

internal static partial class PinchZoom {
    private const int ReportLength = 62;
    private const int HotkeyId = 1;
    private const int WmHotkey = 0x0312;
    private const double MaximumZoom = 100;
    private const double PixelGridZoom = 15;
    private const double PixelGridExitZoom = 14.5;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMessage {
        public IntPtr Window;
        public uint Id;
        public UIntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public Point Cursor;
        public uint Reserved;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security,
        uint disposition, uint flags, IntPtr template);

    [DllImport("kernel32.dll")]
    private static extern bool CancelIoEx(SafeFileHandle file, IntPtr overlapped);

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr window, int id);

    [DllImport("user32.dll")]
    private static extern bool PeekMessage(out NativeMessage message, IntPtr window, uint min, uint max, uint remove);

    [DllImport("user32.dll")]
    private static extern bool SetProcessDpiAwarenessContext(IntPtr context);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int key);

    [DllImport("Magnification.dll")]
    private static extern bool MagInitialize();

    [DllImport("Magnification.dll")]
    private static extern bool MagUninitialize();

    [DllImport("Magnification.dll")]
    private static extern bool MagGetFullscreenTransform(out float zoom, out int x, out int y);

    [DllImport("Magnification.dll")]
    private static extern bool MagSetFullscreenTransform(float zoom, int x, int y);

    [DllImport("user32.dll")]
    private static extern bool SetLayeredWindowAttributes(IntPtr window, int key, byte alpha, int flags);

    [DllImport("user32.dll")]
    private static extern bool SetWindowDisplayAffinity(IntPtr window, uint affinity);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr window);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr window, IntPtr dc);

    [DllImport("gdi32.dll")]
    private static extern bool BitBlt(IntPtr destination, int dx, int dy, int width, int height,
        IntPtr source, int sx, int sy, uint operation);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateCompatibleDC(IntPtr dc);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int width, int height);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr dc, IntPtr item);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr item);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr dc);

    [DllImport("gdi32.dll")]
    private static extern int SetStretchBltMode(IntPtr dc, int mode);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool StretchBlt(IntPtr destination, int dx, int dy, int dw, int dh,
        IntPtr source, int sx, int sy, int sw, int sh, uint operation);

    [DllImport("gdi32.dll")]
    private static extern bool PatBlt(IntPtr target, int x, int y, int width, int height, uint operation);

    private static bool quit;

    private sealed class Gesture {
        private double startDistance;
        private double startZoom;
        private bool engaged;
        public double Zoom { get; private set; }

        public Gesture() { Zoom = 1; }

        public void SetZoom(double zoom) {
            Zoom = Math.Max(1, Math.Min(MaximumZoom, zoom));
            Update(0);
        }

        public bool Update(double distance) {
            if (distance <= 0) {
                startDistance = 0;
                engaged = false;
                return false;
            }
            if (startDistance == 0) {
                startDistance = distance;
                startZoom = Zoom;
                return false;
            }
            double ratio = distance / startDistance;
            if (!engaged) {
                if (Math.Abs(ratio - 1) < 0.11) return false;
                engaged = true;
            }
            double target = Math.Max(1, Math.Min(MaximumZoom, startZoom * ratio));
            double remaining = target - Zoom;
            double next = Math.Abs(remaining) < 0.004 ? target : Zoom + remaining * 0.26;
            if (Math.Abs(next - Zoom) < 0.001 && next != 1) return false;
            if (Math.Abs(next - Zoom) < 0.00001) return false;
            Zoom = next;
            return true;
        }
    }

    private static double Span(byte[] report, int required) {
        if (report.Length < ReportLength || report[0] != 0x0c || report[1] != 0x18) return 0;
        var contacts = new Point[5];
        int count = 0;
        for (int i = 0; i < contacts.Length; i++) {
            int offset = 2 + 8 * i;
            if (report[offset] == 0) continue;
            if (report[offset] != 1) return 0;
            contacts[count++] = new Point(report[offset + 1] | (report[offset + 2] << 8),
                report[offset + 3] | (report[offset + 4] << 8));
        }
        if (count != required) return 0;
        double total = 0;
        for (int a = 0; a < count; a++) {
            for (int b = a + 1; b < count; b++) {
                double x = contacts[a].X - contacts[b].X, y = contacts[a].Y - contacts[b].Y;
                total += Math.Sqrt(x * x + y * y);
            }
        }
        return total / (count * (count - 1) / 2);
    }

    private static string FindDevice() {
        var info = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "pnputil.exe"),
            "/enum-interfaces /enabled /class {4d1e55b2-f16f-11cf-88cb-001111000030}");
        info.UseShellExecute = false;
        info.CreateNoWindow = true;
        info.RedirectStandardOutput = true;
        using (var process = Process.Start(info)) {
            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            Match match = Regex.Match(output, @"\\\\\?\\HID#SYNA0102&Col02#[^\s]+", RegexOptions.IgnoreCase);
            return process.ExitCode == 0 && match.Success ? match.Value : null;
        }
    }

    private static bool ShouldStop(Viewport viewport) {
        bool screenshot = false;
        NativeMessage message;
        while (PeekMessage(out message, IntPtr.Zero, WmHotkey, WmHotkey, 1)) {
            ulong id = message.WParam.ToUInt64();
            if (id == HotkeyId) quit = true;
            if (id == ScreenshotHotkeyId) screenshot = true;
        }
        if (!quit && screenshot) CaptureScreenshot(viewport);
        return quit;
    }

    private sealed class ZoomSurface : Form {
        private IntPtr sourceDc, sourceBitmap, previousBitmap;
        private Rectangle source;
        private Rectangle destination;
        private double scale;

        public ZoomSurface(Rectangle screen) {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            Bounds = screen;
            TopMost = true;
            ShowInTaskbar = false;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.Opaque, true);
            try {
                IntPtr desktop = GetDC(IntPtr.Zero);
                if (desktop == IntPtr.Zero) throw new InvalidOperationException("Could not open the desktop for capture.");
                try {
                    sourceDc = CreateCompatibleDC(desktop);
                    sourceBitmap = CreateCompatibleBitmap(desktop, screen.Width, screen.Height);
                    if (sourceDc == IntPtr.Zero || sourceBitmap == IntPtr.Zero)
                        throw new InvalidOperationException("Could not allocate the desktop frame buffer.");
                    previousBitmap = SelectObject(sourceDc, sourceBitmap);
                    if (previousBitmap == IntPtr.Zero || previousBitmap == new IntPtr(-1))
                        throw new InvalidOperationException("Could not select the desktop frame buffer.");
                } finally { ReleaseDC(IntPtr.Zero, desktop); }
                if (!SetLayeredWindowAttributes(Handle, 0, 255, 2) ||
                    !SetWindowDisplayAffinity(Handle, 0x11))
                    throw new InvalidOperationException("Could not create the click-through zoom surface.");
            } catch { Dispose(); throw; }
        }

        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams {
            get {
                var parameters = base.CreateParams;
                // Layered, mouse transparent, tool window, never activate.
                parameters.ExStyle |= 0x080800A0;
                return parameters;
            }
        }

        public void RenderFrame(double zoom, Point cursor, Rectangle screen) {
            double offsetRatio = 1 - 1 / zoom;
            CaptureFrame(zoom, cursor.X * offsetRatio, cursor.Y * offsetRatio, screen);
            if (!Visible) Show();
            Refresh();
        }

        public void CaptureFrame(double zoom, double originX, double originY, Rectangle screen) {
            int left = (int)Math.Floor(originX), top = (int)Math.Floor(originY);
            int right = Math.Min(screen.Width, (int)Math.Ceiling(originX + screen.Width / zoom));
            int bottom = Math.Min(screen.Height, (int)Math.Ceiling(originY + screen.Height / zoom));
            int width = right - left, height = bottom - top;
            IntPtr desktop = GetDC(IntPtr.Zero);
            if (desktop == IntPtr.Zero) throw new InvalidOperationException("Could not open the desktop for capture.");
            try {
                if (!BitBlt(sourceDc, 0, 0, width, height, desktop, screen.Left + left,
                    screen.Top + top, 0x40cc0020))
                    throw new InvalidOperationException("Could not capture the zoom source.");
            } finally { ReleaseDC(IntPtr.Zero, desktop); }

            scale = zoom;
            source = new Rectangle(0, 0, width, height);
            int dx = (int)Math.Round((left - originX) * zoom), dy = (int)Math.Round((top - originY) * zoom);
            destination = new Rectangle(dx, dy, (int)Math.Ceiling(width * zoom), (int)Math.Ceiling(height * zoom));
        }

        protected override void OnPaint(PaintEventArgs e) {
            DrawFrame(e.Graphics);
        }

        public Bitmap Snapshot() {
            var image = new Bitmap(ClientSize.Width, ClientSize.Height, System.Drawing.Imaging.PixelFormat.Format32bppRgb);
            try {
                using (Graphics graphics = Graphics.FromImage(image)) DrawFrame(graphics);
                return image;
            } catch { image.Dispose(); throw; }
        }

        private void DrawFrame(Graphics graphics) {
            if (source.IsEmpty || sourceDc == IntPtr.Zero) {
                graphics.Clear(Color.Black);
                return;
            }
            IntPtr target = graphics.GetHdc();
            try {
                SetStretchBltMode(target, 3); // COLORONCOLOR
                if (!StretchBlt(target, destination.Left, destination.Top, destination.Width, destination.Height,
                    sourceDc, 0, 0, source.Width, source.Height, 0x00cc0020))
                    throw new InvalidOperationException("Could not paint the magnified frame.");
                if (scale >= PixelGridZoom) PaintGrid(target);
            } finally { graphics.ReleaseHdc(target); }
        }

        private void PaintGrid(IntPtr target) {
            const uint Blackness = 0x00000042;
            Size canvas = ClientSize;

            for (int column = 0; column <= source.Width; column++) {
                int x = (int)Math.Round(destination.Left + column * (double)destination.Width / source.Width);
                if (x >= 0 && x < canvas.Width && !PatBlt(target, x, 0, 1, canvas.Height, Blackness))
                    throw new InvalidOperationException("Could not paint a zoom grid column.");
            }
            for (int row = 0; row <= source.Height; row++) {
                int y = (int)Math.Round(destination.Top + row * (double)destination.Height / source.Height);
                if (y >= 0 && y < canvas.Height && !PatBlt(target, 0, y, canvas.Width, 1, Blackness))
                    throw new InvalidOperationException("Could not paint a zoom grid row.");
            }
        }

        protected override void Dispose(bool disposing) {
            if (disposing && sourceDc != IntPtr.Zero) {
                if (previousBitmap != IntPtr.Zero && previousBitmap != new IntPtr(-1))
                    SelectObject(sourceDc, previousBitmap);
                DeleteDC(sourceDc);
                sourceDc = IntPtr.Zero;
            }
            if (disposing && sourceBitmap != IntPtr.Zero) {
                DeleteObject(sourceBitmap);
                sourceBitmap = IntPtr.Zero;
            }
            base.Dispose(disposing);
        }
    }

    private sealed class Viewport : IDisposable {
        private readonly Stopwatch refresh = Stopwatch.StartNew();
        private readonly bool useNativeZoom;
        private ZoomSurface surface;
        private Rectangle surfaceBounds;
        private Rectangle nativeBounds;
        private Point? pinchAnchor;
        private Point lastCursor;
        private double lastZoom;
        private float nativeZoom = 1;
        private Point nativeOffset;

        public Viewport(bool useNativeZoom = true) {
            this.useNativeZoom = useNativeZoom;
        }

        private void SetNativeZoom(double zoom, Point cursor) {
            float scale = (float)zoom;
            double offsetRatio = 1 - 1 / zoom;
            var offset = new Point((int)Math.Round(cursor.X * offsetRatio),
                (int)Math.Round(cursor.Y * offsetRatio));
            if (scale == nativeZoom && offset == nativeOffset) return;
            if (!MagSetFullscreenTransform(scale, offset.X, offset.Y))
                throw new InvalidOperationException("Windows rejected the full-screen zoom change.");
            nativeZoom = scale;
            nativeOffset = offset;
        }

        public void Update(double zoom, Rectangle screen, bool pinching) {
            zoomActive = zoom > 1;
            if (zoom == 1) {
                if (surface != null && surface.Visible) surface.Hide();
                SetNativeZoom(1, Point.Empty);
                if (!pinching) pinchAnchor = null;
                return;
            }
            Point cursor = Cursor.Position;
            cursor.X = Math.Max(0, Math.Min(screen.Width - 1, cursor.X - screen.Left));
            cursor.Y = Math.Max(0, Math.Min(screen.Height - 1, cursor.Y - screen.Top));
            if (pinching) {
                if (!pinchAnchor.HasValue) pinchAnchor = cursor;
                cursor = pinchAnchor.Value;
            } else {
                pinchAnchor = null;
            }

            if (surface != null && surfaceBounds != screen) {
                surface.Dispose();
                surface = null;
                pinchAnchor = null;
            }
            bool gridVisible = surface != null && surface.Visible;
            // Keep the current rendering path through tiny movements near 15x.
            if (useNativeZoom && zoom < (gridVisible ? PixelGridExitZoom : PixelGridZoom)) {
                SetNativeZoom(zoom, cursor);
                nativeBounds = screen;
                if (gridVisible) surface.Hide();
                return;
            }

            if (surface == null) {
                surface = new ZoomSurface(screen);
                surfaceBounds = screen;
            }
            SetNativeZoom(1, Point.Empty);
            bool changed = zoom != lastZoom || cursor != lastCursor;
            if (surface.Visible && refresh.ElapsedMilliseconds < (changed ? 15 : 50)) return;
            refresh.Restart();
            surface.RenderFrame(zoom, cursor, screen);
            lastCursor = cursor;
            lastZoom = zoom;
        }

        public void SelectZoomedArea() {
            bool surfaceVisible = surface != null && surface.Visible;
            if (!surfaceVisible && nativeZoom <= 1) return;

            Rectangle bounds = surfaceVisible ? surfaceBounds : nativeBounds;
            using (Bitmap image = surfaceVisible ? surface.Snapshot() : CaptureNativeFrame(bounds))
            using (var selection = new ScreenshotSelection(image, bounds)) {
                bool suspended = false;
                if (!surfaceVisible) {
                    selection.Shown += delegate {
                        // Paint the replacement before removing the native transform.
                        selection.Update();
                        suspended = MagSetFullscreenTransform(1, 0, 0);
                        if (!suspended) selection.DialogResult = DialogResult.Cancel;
                    };
                }

                Application.AddMessageFilter(selection);
                try {
                    // An owned selection stays above the frozen overlay throughout the capture.
                    DialogResult result = surfaceVisible ? selection.ShowDialog(surface) : selection.ShowDialog();
                    if (result == DialogResult.OK) {
                        using (Bitmap cropped = image.Clone(selection.SelectedArea, image.PixelFormat)) {
                            var data = new DataObject();
                            data.SetImage(cropped);
                            Clipboard.SetDataObject(data, true, 5, 40);
                        }
                    }
                } finally {
                    Application.RemoveMessageFilter(selection);
                    if (suspended && !quit && !MagSetFullscreenTransform(nativeZoom, nativeOffset.X, nativeOffset.Y)) {
                        nativeZoom = 1;
                        nativeOffset = Point.Empty;
                    }
                }
            }
        }

        private Bitmap CaptureNativeFrame(Rectangle screen) {
            double x = Math.Min(nativeOffset.X, screen.Width - screen.Width / (double)nativeZoom);
            double y = Math.Min(nativeOffset.Y, screen.Height - screen.Height / (double)nativeZoom);
            using (var snapshot = new ZoomSurface(screen)) {
                snapshot.CaptureFrame(nativeZoom, x, y, screen);
                return snapshot.Snapshot();
            }
        }

        public void Dispose() {
            zoomActive = false;
            try {
                if (surface != null) surface.Dispose();
            } finally {
                SetNativeZoom(1, Point.Empty);
            }
        }
    }

    private static void RunSynaptics(string path, int fingers, bool checkOnly) {
        using (var device = CreateFile(path, 0x80000000, 3, IntPtr.Zero, 3, 0x40000000, IntPtr.Zero)) {
            if (device.IsInvalid)
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            using (var input = new FileStream(device, FileAccess.Read, ReportLength, true)) {
                if (checkOnly) {
                    var first = new byte[ReportLength];
                    var read = input.BeginRead(first, 0, first.Length, null, null);
                    using (var ready = read.AsyncWaitHandle) {
                        if (!ready.WaitOne(1500)) {
                            CancelIoEx(device, IntPtr.Zero);
                            throw new IOException("No input report received.");
                        }
                        if (input.EndRead(read) != ReportLength || first[0] != 0x0c)
                            throw new IOException("Unexpected input report.");
                    }
                    if (!MagInitialize()) throw new InvalidOperationException("Magnifier initialization failed.");
                    try {
                        float scale; int x, y;
                        if (!MagGetFullscreenTransform(out scale, out x, out y))
                            throw new InvalidOperationException("Could not read the full-screen transform.");
                        Console.WriteLine("Synaptics report received. Windows full-screen magnification is available.");
                    } finally { MagUninitialize(); }
                    return;
                }
                if (Screen.AllScreens.Length != 1)
                    throw new InvalidOperationException("The trial supports one active display.");
                if (!RegisterHotKey(IntPtr.Zero, HotkeyId, 0x4006, 0x7b))
                    throw new InvalidOperationException("Ctrl+Shift+F12 is unavailable. No zoom was applied.");
                bool initialized = false;
                SnippingShortcut snipping = null;
                var viewport = new Viewport();
                try {
                    initialized = MagInitialize();
                    if (!initialized) throw new InvalidOperationException("Magnifier initialization failed.");
                    float initialZoom; int initialX, initialY;
                    if (!MagGetFullscreenTransform(out initialZoom, out initialX, out initialY) || initialZoom > 1.001)
                        throw new InvalidOperationException("Turn off Windows Magnifier before starting the trial.");
                    snipping = new SnippingShortcut(IntPtr.Zero);
                    Console.WriteLine("Hold Alt and spread or pinch " + fingers + " fingers. Ctrl+Shift+F12 quits and resets zoom.");
                    Console.WriteLine("Native low zoom; single-frame grid at 15x. Maximum zoom: 100x.");
                    var screen = Screen.PrimaryScreen.Bounds;
                    var gesture = new Gesture();
                    var packet = new byte[ReportLength];
                    bool pinching = false;
                    while (!ShouldStop(viewport)) {
                        viewport.Update(gesture.Zoom, screen, pinching && AltHeld());
                        var read = input.BeginRead(packet, 0, packet.Length, null, null);
                        using (var ready = read.AsyncWaitHandle) {
                            while (!ready.WaitOne(12) && !ShouldStop(viewport))
                                viewport.Update(gesture.Zoom, screen, pinching && AltHeld());
                            if (quit) { CancelIoEx(device, IntPtr.Zero); break; }
                            if (input.EndRead(read) != packet.Length) break;
                        }
                        double distance = AltHeld() ? Span(packet, fingers) : 0;
                        pinching = distance > 0;
                        gesture.Update(distance);
                    }
                } finally {
                    try {
                        if (snipping != null) snipping.Dispose();
                        viewport.Dispose();
                    } finally {
                        if (initialized) MagUninitialize();
                        UnregisterHotKey(IntPtr.Zero, HotkeyId);
                    }
                }
            }
        }
    }

    private static bool AltHeld() {
        return (GetAsyncKeyState(0x12) & 0x8000) != 0;
    }

    private static void Replay(string path) {
        var gesture = new Gesture();
        int frames = 0, updates = 0;
        double low = double.MaxValue, high = 0, peak = 1;
        foreach (string line in File.ReadLines(path)) {
            string[] bytes = line.Substring(line.IndexOf(',') + 1).Split('-');
            var report = Array.ConvertAll(bytes, item => Convert.ToByte(item, 16));
            double distance = Span(report, 2);
            if (distance > 0) { frames++; low = Math.Min(low, distance); high = Math.Max(high, distance); }
            if (gesture.Update(distance)) { updates++; peak = Math.Max(peak, gesture.Zoom); }
        }
        Console.WriteLine("Two-finger frames: {0}; distance range: {1:F1}..{2:F1}; zoom updates: {3}; peak: {4:F2}x",
            frames, low, high, updates, peak);
        if (frames < 10 || high <= low || updates < 10 || peak <= 1.1)
            throw new InvalidOperationException("The sample did not exercise continuous scaling.");
    }

    [STAThread]
    public static int Main(string[] args) {
        try {
            SetProcessDpiAwarenessContext(new IntPtr(-4));
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
            Console.CancelKeyPress += delegate(object sender, ConsoleCancelEventArgs eventArgs) {
                eventArgs.Cancel = true;
                quit = true;
            };
            if (args.Length == 2 && args[0] == "--replay") {
                Replay(args[1]);
            } else {
                bool wheelOnly = Array.IndexOf(args, "--wheel") >= 0;
                bool forceStandard = wheelOnly || Array.IndexOf(args, "--standard") >= 0;
                bool check = Array.IndexOf(args, "--check") >= 0;
                string device = forceStandard || Screen.AllScreens.Length != 1 ? null : FindDevice();
                if (device != null) {
                    RunSynaptics(device, Array.IndexOf(args, "--three") >= 0 ? 3 : 2, check);
                } else if (check) {
                    Console.WriteLine("Standard Windows input mode.");
                } else {
                    RunStandard(wheelOnly);
                }
            }
            return 0;
        } catch (Exception error) {
            Console.Error.WriteLine(error.Message);
            if (args.Length == 0) {
                MessageBox.Show(error.Message, "Windows PinchZoom", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            return 1;
        }
    }
}
