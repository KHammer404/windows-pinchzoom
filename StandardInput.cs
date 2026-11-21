using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

internal static partial class PinchZoom {
    private const int WmInput = 0xff;
    private const uint RidInput = 0x10000003;
    private const uint RidiPreparsedData = 0x20000005;
    private const int HidSuccess = 0x00110000;
    private const uint HidDigitizer = 0x0d;

    [StructLayout(LayoutKind.Sequential)]
    private struct RawRegistration {
        public ushort UsagePage, Usage;
        public uint Flags;
        public IntPtr Window;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawHeader {
        public uint Type, Size;
        public IntPtr Device, WParam;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HidCollection {
        public ushort Usage, UsagePage, Parent, NumberOfChildren, NextSibling, FirstChild;
        public uint Flags;
        public IntPtr Context;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterRawInputDevices(RawRegistration[] devices, uint count, uint size);

    [DllImport("user32.dll")]
    private static extern uint GetRawInputData(IntPtr input, uint command, IntPtr data, ref uint size, uint headerSize);

    [DllImport("user32.dll", EntryPoint = "GetRawInputDeviceInfoW")]
    private static extern uint GetRawInputDeviceInfo(IntPtr device, uint command, IntPtr data, ref uint size);

    [DllImport("hid.dll")]
    private static extern int HidP_GetLinkCollectionNodes([Out] HidCollection[] nodes, ref uint count, IntPtr descriptor);

    [DllImport("hid.dll")]
    private static extern int HidP_GetUsageValue(int kind, ushort page, ushort collection, ushort usage,
        out uint value, IntPtr descriptor, IntPtr packet, uint length);

    [DllImport("hid.dll")]
    private static extern int HidP_GetScaledUsageValue(int kind, ushort page, ushort collection, ushort usage,
        out int value, IntPtr descriptor, IntPtr packet, uint length);

    [DllImport("hid.dll")]
    private static extern int HidP_GetUsages(int kind, ushort page, ushort collection, [Out] ushort[] usages,
        ref uint usageCount, IntPtr descriptor, IntPtr packet, uint length);

    private sealed class PrecisionDevice : IDisposable {
        private IntPtr descriptor;
        private readonly List<ushort> fingerCollections = new List<ushort>();
        private readonly Dictionary<uint, bool> seen = new Dictionary<uint, bool>();
        private readonly List<Point> tips = new List<Point>();
        private readonly ushort[] usages = new ushort[16];
        private int remaining;

        public PrecisionDevice(IntPtr device) {
            uint bytes = 0;
            GetRawInputDeviceInfo(device, RidiPreparsedData, IntPtr.Zero, ref bytes);
            if (bytes == 0 || bytes > 65536) return;
            descriptor = Marshal.AllocHGlobal((int)bytes);
            if (GetRawInputDeviceInfo(device, RidiPreparsedData, descriptor, ref bytes) == uint.MaxValue) return;
            uint count = 0;
            HidP_GetLinkCollectionNodes(null, ref count, descriptor);
            if (count == 0 || count > 256) return;
            var nodes = new HidCollection[count];
            if (HidP_GetLinkCollectionNodes(nodes, ref count, descriptor) != HidSuccess) return;
            for (ushort i = 1; i < count; i++) {
                if (nodes[i].UsagePage == HidDigitizer && nodes[i].Usage == 0x22)
                    fingerCollections.Add(i);
            }
        }

        public void Reset() {
            remaining = 0;
            seen.Clear();
            tips.Clear();
        }

        public bool Read(IntPtr packet, uint length, out double span) {
            span = 0;
            if (fingerCollections.Count == 0) return false;
            uint count;
            if (HidP_GetUsageValue(0, 0x0d, 0, 0x54, out count, descriptor, packet, length) != HidSuccess)
                return false;
            if (count != 0) {
                Reset();
                if (count > 2) return true;
                remaining = (int)count;
            } else if (remaining == 0) {
                return true;
            }
            foreach (ushort collection in fingerCollections) {
                uint id;
                if (HidP_GetUsageValue(0, 0x0d, collection, 0x51, out id, descriptor, packet, length) != HidSuccess ||
                    seen.ContainsKey(id)) continue;
                uint usageCount = (uint)usages.Length;
                if (HidP_GetUsages(0, 0x0d, collection, usages, ref usageCount, descriptor, packet, length) != HidSuccess)
                    continue;
                seen.Add(id, true);
                if (Array.IndexOf(usages, (ushort)0x42, 0, (int)usageCount) >= 0) {
                    int x, y;
                    if (HidP_GetScaledUsageValue(0, 1, collection, 0x30, out x, descriptor, packet, length) == HidSuccess &&
                        HidP_GetScaledUsageValue(0, 1, collection, 0x31, out y, descriptor, packet, length) == HidSuccess) {
                        tips.Add(new Point(x, y));
                    } else {
                        uint rawX, rawY;
                        if (HidP_GetUsageValue(0, 1, collection, 0x30, out rawX, descriptor, packet, length) == HidSuccess &&
                            HidP_GetUsageValue(0, 1, collection, 0x31, out rawY, descriptor, packet, length) == HidSuccess)
                            tips.Add(new Point((int)rawX, (int)rawY));
                    }
                }
                if (--remaining == 0) {
                    if (tips.Count == 2) {
                        double dx = tips[0].X - tips[1].X, dy = tips[0].Y - tips[1].Y;
                        span = Math.Sqrt(dx * dx + dy * dy);
                    }
                    Reset();
                    return true;
                }
            }
            return false;
        }

        public void Dispose() {
            if (descriptor != IntPtr.Zero) Marshal.FreeHGlobal(descriptor);
            descriptor = IntPtr.Zero;
        }
    }

    private const int WmWheel = 0x020a;
    private const int WmForwardWheel = 0x8001;
    private const uint CtrlAlt = 0x4003;
    private const int ZoomInId = 2, ZoomOutId = 3, ResetId = 4, NumpadInId = 5, NumpadOutId = 6;

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate IntPtr MouseHook(int code, IntPtr message, IntPtr data);

    [DllImport("user32.dll", SetLastError = true, EntryPoint = "SetWindowsHookExW")]
    private static extern IntPtr SetWindowsHookEx(int kind, MouseHook procedure, IntPtr module, uint thread);

    [DllImport("user32.dll")]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);

    [DllImport("user32.dll", EntryPoint = "PostMessageW")]
    private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", EntryPoint = "PostThreadMessageW")]
    private static extern bool PostThreadMessage(uint thread, uint message, UIntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", EntryPoint = "GetMessageW")]
    private static extern int GetMessage(out NativeMessage message, IntPtr window, uint first, uint last);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string name);

    private sealed class WheelInput : IDisposable {
        private readonly IntPtr receiver;
        private readonly Thread worker;
        private readonly ManualResetEvent ready = new ManualResetEvent(false);
        private readonly MouseHook procedure;
        private uint threadId;
        public bool Active { get; private set; }

        public WheelInput(IntPtr receiver) {
            this.receiver = receiver;
            procedure = OnMouse;
            worker = new Thread(Receive);
            worker.IsBackground = true;
            worker.Start();
            ready.WaitOne();
        }

        private void Receive() {
            threadId = GetCurrentThreadId();
            NativeMessage message;
            PeekMessage(out message, IntPtr.Zero, 0, 0, 0);
            IntPtr hook = SetWindowsHookEx(14, procedure, GetModuleHandle(null), 0);
            Active = hook != IntPtr.Zero;
            ready.Set();
            if (!Active) return;
            try {
                while (GetMessage(out message, IntPtr.Zero, 0, 0) > 0) { }
            } finally { UnhookWindowsHookEx(hook); }
        }

        private IntPtr OnMouse(int code, IntPtr message, IntPtr data) {
            // The callback never paints, and it ignores every event except Alt + vertical wheel.
            if (code == 0 && message.ToInt64() == WmWheel && AltHeld() &&
                (GetAsyncKeyState(0x11) & 0x8000) == 0) {
                int delta = (short)(Marshal.ReadInt32(data, 8) >> 16);
                if (delta != 0 && PostMessage(receiver, WmForwardWheel, new IntPtr(delta), IntPtr.Zero))
                    return new IntPtr(1);
            }
            return CallNextHookEx(IntPtr.Zero, code, message, data);
        }

        public void Dispose() {
            if (Active) PostThreadMessage(threadId, 0x0012, UIntPtr.Zero, IntPtr.Zero);
            worker.Join();
            ready.Dispose();
        }
    }

    private sealed class StandardWindow : NativeWindow, IDisposable {
        private readonly Dictionary<IntPtr, PrecisionDevice> devices = new Dictionary<IntPtr, PrecisionDevice>();
        private readonly Gesture gesture = new Gesture();
        private readonly Viewport viewport;
        private readonly System.Windows.Forms.Timer timer;
        private readonly Stopwatch lastPinch = Stopwatch.StartNew();
        private readonly int headerSize = Marshal.SizeOf(typeof(RawHeader));
        private readonly List<int> hotkeys = new List<int>();
        private WheelInput wheel;
        private IntPtr buffer;
        private uint bufferLength;
        private bool pinching;
        public Exception Failure { get; private set; }

        public StandardWindow(bool wheelOnly) {
            viewport = new Viewport(Screen.AllScreens.Length == 1);
            timer = new System.Windows.Forms.Timer();
            timer.Interval = 12;
            timer.Tick += Render;
            try {
                CreateHandle(new CreateParams { Caption = "Windows PinchZoom Input", Parent = new IntPtr(-3) });
                AddHotkey(HotkeyId, 0x4006, 0x7b);
                AddHotkey(ZoomInId, CtrlAlt, 0xbb);
                AddHotkey(ZoomOutId, CtrlAlt, 0xbd);
                AddHotkey(ResetId, CtrlAlt, 0x30);
                if (RegisterHotKey(Handle, NumpadInId, CtrlAlt, 0x6b)) hotkeys.Add(NumpadInId);
                if (RegisterHotKey(Handle, NumpadOutId, CtrlAlt, 0x6d)) hotkeys.Add(NumpadOutId);
                bool raw = !wheelOnly && SetRawTarget(Handle, 0x100);
                wheel = new WheelInput(Handle);
                Console.WriteLine("Standard Windows mode. Precision input registered: " + raw + "; wheel hook: " + wheel.Active);
                Console.WriteLine("Input window handle: " + Handle.ToInt64());
                timer.Start();
            } catch { Dispose(); throw; }
        }

        private void AddHotkey(int id, uint modifiers, uint key) {
            if (!RegisterHotKey(Handle, id, modifiers, key))
                throw new InvalidOperationException("A Windows PinchZoom shortcut is already in use.");
            hotkeys.Add(id);
        }

        private static bool SetRawTarget(IntPtr handle, uint flags) {
            var entry = new RawRegistration { UsagePage = 0x0d, Usage = 5, Flags = flags, Window = handle };
            return RegisterRawInputDevices(new RawRegistration[] { entry }, 1, (uint)Marshal.SizeOf(typeof(RawRegistration)));
        }

        private void ReadInput(IntPtr message) {
            if (!AltHeld()) return;
            uint length = 0;
            GetRawInputData(message, RidInput, IntPtr.Zero, ref length, (uint)headerSize);
            if (length < headerSize + 8 || length > 65536) return;
            if (length > bufferLength) {
                if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer);
                buffer = Marshal.AllocHGlobal((int)length);
                bufferLength = length;
            }
            uint read = GetRawInputData(message, RidInput, buffer, ref length, (uint)headerSize);
            if (read != length || Marshal.ReadInt32(buffer) != 2) return;
            IntPtr device = Marshal.ReadIntPtr(buffer, 8);
            int size = Marshal.ReadInt32(buffer, headerSize);
            int count = Marshal.ReadInt32(buffer, headerSize + 4);
            if (size <= 0 || count <= 0 || (long)size * count > length - headerSize - 8) return;
            PrecisionDevice reader;
            if (!devices.TryGetValue(device, out reader)) {
                reader = new PrecisionDevice(device);
                devices.Add(device, reader);
            }
            lastPinch.Restart();
            for (int i = 0; i < count; i++) {
                double span;
                if (!reader.Read(IntPtr.Add(buffer, headerSize + 8 + size * i), (uint)size, out span)) continue;
                pinching = span > 0;
                gesture.Update(span);
            }
        }

        private void Scale(double steps) {
            if (pinching) return;
            double zoom = gesture.Zoom * Math.Pow(1.2, steps);
            gesture.SetZoom(zoom < 1.03 ? 1 : zoom);
        }

        private void Render(object sender, EventArgs args) {
            try {
                if (pinching && (!AltHeld() || lastPinch.ElapsedMilliseconds > 180)) {
                    pinching = false;
                    gesture.Update(0);
                    foreach (var device in devices.Values) device.Reset();
                }
                viewport.Update(gesture.Zoom, Screen.FromPoint(Cursor.Position).Bounds, pinching);
            } catch (Exception error) {
                Failure = error;
                System.Windows.Forms.Application.ExitThread();
            }
        }

        protected override void WndProc(ref Message message) {
            if (message.Msg == WmInput) {
                ReadInput(message.LParam);
            } else if (message.Msg == WmForwardWheel) {
                Scale(message.WParam.ToInt64() / 120.0);
            } else if (message.Msg == WmHotkey) {
                switch (message.WParam.ToInt32()) {
                    case HotkeyId: System.Windows.Forms.Application.ExitThread(); break;
                    case ZoomInId:
                    case NumpadInId: Scale(1); break;
                    case ZoomOutId:
                    case NumpadOutId: Scale(-1); break;
                    case ResetId: pinching = false; gesture.SetZoom(1); break;
                }
            }
            base.WndProc(ref message);
        }

        public void Dispose() {
            timer.Dispose();
            if (wheel != null) wheel.Dispose();
            foreach (var device in devices.Values) device.Dispose();
            if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer);
            if (Handle != IntPtr.Zero) {
                foreach (int id in hotkeys) UnregisterHotKey(Handle, id);
                SetRawTarget(IntPtr.Zero, 1);
                DestroyHandle();
            }
            viewport.Dispose();
        }
    }

    private static void RunStandard(bool wheelOnly) {
        if (!MagInitialize()) throw new InvalidOperationException("Could not initialize Windows magnification.");
        try {
            float initial; int x, y;
            if (!MagGetFullscreenTransform(out initial, out x, out y) || initial > 1.001)
                throw new InvalidOperationException("Turn off Windows Magnifier before starting Windows PinchZoom.");
            using (var window = new StandardWindow(wheelOnly)) {
                System.Windows.Forms.Application.Run();
                if (window.Failure != null) throw window.Failure;
            }
        } finally { MagUninitialize(); }
    }
}
