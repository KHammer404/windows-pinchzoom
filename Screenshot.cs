using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

internal static partial class PinchZoom {
    private const int ScreenshotHotkeyId = 7;
    private static volatile bool zoomActive;

    private static void CaptureScreenshot(Viewport viewport) {
        try { viewport.SelectZoomedArea(); }
        catch (Exception error) {
            Console.Error.WriteLine("Could not copy the zoomed frame: " + error.Message);
            System.Media.SystemSounds.Exclamation.Play();
        }
    }

    private sealed class SnippingShortcut : IDisposable {
        private readonly IntPtr window;
        private readonly uint thread;
        private readonly InputHook keyboard;
        private readonly bool screenshotRegistered;
        private bool capturedS;

        public SnippingShortcut(IntPtr window) {
            this.window = window;
            thread = GetCurrentThreadId();
            screenshotRegistered = RegisterHotKey(window, ScreenshotHotkeyId, 0x4006, 0x7a);
            try {
                keyboard = new InputHook(13, OnKey);
                Console.WriteLine(keyboard.Active
                    ? "Win+Shift+S captures the frozen view while zoomed."
                    : "Win+Shift+S could not be reserved for zoomed captures.");
                if (screenshotRegistered)
                    Console.WriteLine("Ctrl+Shift+F11 also captures the frozen view.");
            } catch {
                if (screenshotRegistered) UnregisterHotKey(window, ScreenshotHotkeyId);
                throw;
            }
        }

        private IntPtr OnKey(int code, IntPtr message, IntPtr data) {
            if (code == 0 && (zoomActive || capturedS) && Marshal.ReadInt32(data) == (int)Keys.S) {
                long action = message.ToInt64();
                if (capturedS) {
                    if (action == 0x0101 || action == 0x0105) capturedS = false;
                    return new IntPtr(1);
                }
                if ((action == 0x0100 || action == 0x0104) && zoomActive &&
                    (IsDown(Keys.LWin) || IsDown(Keys.RWin)) &&
                    IsDown(Keys.ShiftKey) && !IsDown(Keys.ControlKey) && !AltHeld() && QueueCapture()) {
                    capturedS = true;
                    return new IntPtr(1);
                }
            }
            return CallNextHookEx(IntPtr.Zero, code, message, data);
        }

        private static bool IsDown(Keys key) {
            return (GetAsyncKeyState((int)key) & 0x8000) != 0;
        }

        private bool QueueCapture() {
            if (window != IntPtr.Zero)
                return PostMessage(window, WmHotkey, new IntPtr(ScreenshotHotkeyId), IntPtr.Zero);
            return PostThreadMessage(thread, WmHotkey, new UIntPtr(ScreenshotHotkeyId), IntPtr.Zero);
        }

        public void Dispose() {
            try { keyboard.Dispose(); }
            finally {
                if (screenshotRegistered) UnregisterHotKey(window, ScreenshotHotkeyId);
            }
        }
    }


    private sealed class ScreenshotSelection : Form, IMessageFilter {
        private readonly Bitmap image;
        private Point start;
        private bool dragging;
        public Rectangle SelectedArea { get; private set; }

        public ScreenshotSelection(Bitmap image, Rectangle screen) {
            this.image = image;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            Bounds = screen;
            TopMost = true;
            ShowInTaskbar = false;
            KeyPreview = true;
            DoubleBuffered = true;
            Cursor = Cursors.Cross;
        }

        public bool PreFilterMessage(ref Message message) {
            if (message.Msg != WmHotkey) return false;
            int id = message.WParam.ToInt32();
            if (id == HotkeyId) {
                quit = true;
                DialogResult = DialogResult.Cancel;
                return true;
            }
            return id == ScreenshotHotkeyId;
        }

        protected override void OnMouseDown(MouseEventArgs e) {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            start = e.Location;
            SelectedArea = Rectangle.Empty;
            dragging = true;
            Capture = true;
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e) {
            base.OnMouseMove(e);
            if (dragging) SetSelection(e.Location);
        }

        protected override void OnMouseUp(MouseEventArgs e) {
            base.OnMouseUp(e);
            if (!dragging || e.Button != MouseButtons.Left) return;
            SetSelection(e.Location);
            dragging = false;
            Capture = false;
            if (SelectedArea.Width > 0 && SelectedArea.Height > 0) DialogResult = DialogResult.OK;
            else Invalidate();
        }

        protected override bool ProcessCmdKey(ref Message message, Keys keys) {
            if (keys == Keys.Escape) {
                DialogResult = DialogResult.Cancel;
                return true;
            }
            if (keys == Keys.Enter) {
                SelectedArea = ClientRectangle;
                DialogResult = DialogResult.OK;
                return true;
            }
            return base.ProcessCmdKey(ref message, keys);
        }

        private void SetSelection(Point end) {
            var area = Rectangle.FromLTRB(Math.Min(start.X, end.X), Math.Min(start.Y, end.Y),
                Math.Max(start.X, end.X), Math.Max(start.Y, end.Y));
            SelectedArea = Rectangle.Intersect(ClientRectangle, area);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e) {
            Graphics graphics = e.Graphics;
            graphics.DrawImageUnscaled(image, 0, 0);
            using (var shade = new SolidBrush(Color.FromArgb(120, 0, 0, 0)))
                graphics.FillRectangle(shade, ClientRectangle);
            if (SelectedArea.Width > 0 && SelectedArea.Height > 0) {
                graphics.DrawImage(image, SelectedArea, SelectedArea, GraphicsUnit.Pixel);
                graphics.DrawRectangle(Pens.White, SelectedArea.Left, SelectedArea.Top,
                    SelectedArea.Width - 1, SelectedArea.Height - 1);
            }
            if (!dragging) {
                const string hint = "Drag to copy an area   Enter: entire zoomed screen   Esc: cancel";
                Size label = TextRenderer.MeasureText(hint, SystemFonts.MessageBoxFont);
                graphics.FillRectangle(Brushes.Black, 12, 12, label.Width + 16, label.Height + 10);
                TextRenderer.DrawText(graphics, hint, SystemFonts.MessageBoxFont, new Point(20, 17), Color.White);
            }
        }
    }
}
