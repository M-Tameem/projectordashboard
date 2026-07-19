using System;
using System.Runtime.InteropServices;

namespace ProjectorDash
{
    /// <summary>
    /// Owns one live DWM thumbnail relationship. Windows' compositor copies
    /// the existing source window directly, so previewing never creates a
    /// second browser tab, decoder, audio stream, or login session.
    /// </summary>
    public sealed class DwmWindowMirror : IDisposable
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeSize
        {
            public int Width;
            public int Height;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ThumbnailProperties
        {
            public uint Flags;
            public NativeRect Destination;
            public NativeRect Source;
            public byte Opacity;
            [MarshalAs(UnmanagedType.Bool)] public bool Visible;
            [MarshalAs(UnmanagedType.Bool)] public bool SourceClientAreaOnly;
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmIsCompositionEnabled(
            [MarshalAs(UnmanagedType.Bool)] out bool enabled);

        [DllImport("dwmapi.dll")]
        private static extern int DwmRegisterThumbnail(IntPtr destination,
            IntPtr source, out IntPtr thumbnail);

        [DllImport("dwmapi.dll")]
        private static extern int DwmUnregisterThumbnail(IntPtr thumbnail);

        [DllImport("dwmapi.dll")]
        private static extern int DwmQueryThumbnailSourceSize(IntPtr thumbnail,
            out NativeSize size);

        [DllImport("dwmapi.dll")]
        private static extern int DwmUpdateThumbnailProperties(IntPtr thumbnail,
            ref ThumbnailProperties properties);

        private const uint DestinationFlag = 0x00000001;
        private const uint OpacityFlag = 0x00000004;
        private const uint VisibleFlag = 0x00000008;
        private const uint SourceClientFlag = 0x00000010;

        private IntPtr _thumbnail = IntPtr.Zero;
        private IntPtr _source = IntPtr.Zero;

        public IntPtr SourceWindow { get { return _source; } }
        public bool Attached { get { return _thumbnail != IntPtr.Zero; } }

        public bool Attach(IntPtr destination, IntPtr source)
        {
            Detach();
            if (destination == IntPtr.Zero || source == IntPtr.Zero ||
                !ScreenUtil.IsWindow(source)) return false;
            try
            {
                bool enabled;
                if (DwmIsCompositionEnabled(out enabled) != 0 || !enabled)
                    return false;
                IntPtr thumbnail;
                if (DwmRegisterThumbnail(destination, source, out thumbnail) != 0 ||
                    thumbnail == IntPtr.Zero) return false;
                _thumbnail = thumbnail;
                _source = source;
                return true;
            }
            catch
            {
                Detach();
                return false;
            }
        }

        public bool Update(int left, int top, int width, int height)
        {
            if (_thumbnail == IntPtr.Zero || width < 2 || height < 2) return false;
            try
            {
                NativeSize source;
                if (DwmQueryThumbnailSourceSize(_thumbnail, out source) != 0 ||
                    source.Width < 1 || source.Height < 1) return false;

                // Letterbox rather than crop so the tablet always shows the
                // exact full projector image.
                double scale = Math.Min(width / (double)source.Width,
                    height / (double)source.Height);
                int drawWidth = Math.Max(1, (int)Math.Round(source.Width * scale));
                int drawHeight = Math.Max(1, (int)Math.Round(source.Height * scale));
                int x = left + ((width - drawWidth) / 2);
                int y = top + ((height - drawHeight) / 2);

                ThumbnailProperties properties = new ThumbnailProperties();
                properties.Flags = DestinationFlag | OpacityFlag |
                    VisibleFlag | SourceClientFlag;
                properties.Destination = new NativeRect
                {
                    Left = x,
                    Top = y,
                    Right = x + drawWidth,
                    Bottom = y + drawHeight
                };
                properties.Opacity = 255;
                properties.Visible = true;
                properties.SourceClientAreaOnly = false;
                return DwmUpdateThumbnailProperties(_thumbnail, ref properties) == 0;
            }
            catch { return false; }
        }

        public void Detach()
        {
            if (_thumbnail != IntPtr.Zero)
            {
                try { DwmUnregisterThumbnail(_thumbnail); }
                catch { }
            }
            _thumbnail = IntPtr.Zero;
            _source = IntPtr.Zero;
        }

        public void Dispose()
        {
            Detach();
            GC.SuppressFinalize(this);
        }

        ~DwmWindowMirror()
        {
            Detach();
        }
    }
}
