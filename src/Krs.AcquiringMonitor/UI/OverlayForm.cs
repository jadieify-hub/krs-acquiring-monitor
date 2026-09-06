using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Krs.AcquiringMonitor.Configuration;

namespace Krs.AcquiringMonitor.UI
{
    internal sealed class OverlayTextLabel : Label
    {
        internal static void DrawText(
            Graphics graphics,
            string text,
            Font font,
            Color color,
            Rectangle bounds,
            ContentAlignment alignment)
        {
            TextRenderingHint previousHint = graphics.TextRenderingHint;
            try
            {
                graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                using (var brush = new SolidBrush(color))
                using (var format = new StringFormat())
                {
                    format.FormatFlags = StringFormatFlags.NoWrap;
                    format.LineAlignment = StringAlignment.Center;
                    format.Trimming = StringTrimming.EllipsisCharacter;
                    format.Alignment = alignment == ContentAlignment.MiddleCenter
                        ? StringAlignment.Center
                        : alignment == ContentAlignment.MiddleRight
                            ? StringAlignment.Far
                            : StringAlignment.Near;
                    graphics.DrawString(text ?? string.Empty, font, brush, bounds, format);
                }
            }
            finally
            {
                graphics.TextRenderingHint = previousHint;
            }
        }

        protected override void OnPaint(PaintEventArgs eventArgs)
        {
            DrawText(
                eventArgs.Graphics,
                Text,
                Font,
                ForeColor,
                ClientRectangle,
                TextAlign);
        }
    }

    internal sealed class OverlayForm : Form
    {
        private const int ExtendedStyleToolWindow = 0x00000080;
        private const int ExtendedStyleNoActivate = 0x08000000;
        private const int ExtendedStyleLayered = 0x00080000;
        private Color _textColor = AppSettings.DefaultOverlayTextColor;
        private Color _attentionColor = AppSettings.DefaultOverlayAttentionColor;
        private bool _previewAttentionColor;
        private readonly Label[] _nameControls;
        private readonly Label[] _amountControls;
        private readonly Label _resizeGrip;
        private readonly ToolTip _toolTip = new ToolTip { ShowAlways = true, AutoPopDelay = 15000 };
        private int _rowCount = 1;
        private string _refreshStatus = string.Empty;
        private bool _refreshFailed;
        private bool _dragging;
        private Point _dragCursorOrigin;
        private Point _dragFormOrigin;
        private Rectangle _frontolBounds;
        private bool _dataStale;
        private bool _refreshDeferred;
        private bool _refreshing;
        private int _preferredWidth = AppSettings.DefaultOverlayWidth;
        private float _fontSize = AppSettings.DefaultOverlayFontSize;
        private string _fontFamily = AppSettings.DefaultOverlayFontFamily;
        private bool _namesBold;
        private bool _amountsBold = true;
        private float _layoutScale = 1f;
        private bool _resizing;
        private int _resizeWidthOrigin;
        private bool _renderQueued;

        public OverlayForm()
        {
            // Measure and arrange with the same DPI as DrawString; do not scale twice.
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Color.Black;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;

            var nameFont = new Font(_fontFamily, _fontSize, FontStyle.Regular);
            var amountFont = new Font(_fontFamily, _fontSize + 1f, FontStyle.Bold);
            _nameControls = new Label[2];
            _amountControls = new Label[2];

            for (int index = 0; index < 2; index++)
            {
                var name = new OverlayTextLabel
                {
                    AutoSize = false,
                    BackColor = Color.Transparent,
                    ForeColor = _textColor,
                    Font = nameFont,
                    Cursor = Cursors.SizeAll,
                    TextAlign = ContentAlignment.MiddleLeft
                };
                name.MouseDown += BeginDrag;
                name.MouseMove += ContinueDrag;
                name.MouseUp += EndDrag;
                Controls.Add(name);
                _nameControls[index] = name;

                var amount = new OverlayTextLabel
                {
                    AutoSize = false,
                    BackColor = Color.Transparent,
                    ForeColor = _textColor,
                    Font = amountFont,
                    Cursor = Cursors.Hand,
                    TextAlign = ContentAlignment.MiddleLeft
                };
                amount.MouseDoubleClick += RequestRefresh;
                Controls.Add(amount);
                _amountControls[index] = amount;
            }

            _resizeGrip = new OverlayTextLabel
            {
                Text = "⋮",
                Font = new Font("Segoe UI Symbol", 12f, FontStyle.Regular),
                ForeColor = _textColor,
                BackColor = Color.Transparent,
                Cursor = Cursors.SizeWE,
                TextAlign = ContentAlignment.MiddleCenter,
                AccessibleName = "Изменить ширину оверлея",
                AccessibleRole = AccessibleRole.Grip
            };
            _resizeGrip.MouseDown += BeginResize;
            _resizeGrip.MouseMove += ContinueResize;
            _resizeGrip.MouseUp += EndResize;
            Controls.Add(_resizeGrip);
            _toolTip.SetToolTip(_resizeGrip, "Потяните край, чтобы изменить ширину. Шрифт не меняется.");
            foreach (Control part in Controls)
            {
                part.TextChanged += QueueRender;
                part.FontChanged += QueueRender;
                part.ForeColorChanged += QueueRender;
                part.SizeChanged += QueueRender;
                part.LocationChanged += QueueRender;
                part.VisibleChanged += QueueRender;
            }
            LayoutRows();
            UpdateRefreshState();
        }

        public event EventHandler RefreshRequested;

        public event EventHandler PositionCommitted;

        public bool IsUserDragging
        {
            get { return _dragging || _resizing; }
        }

        public int PreferredWidth
        {
            get { return _preferredWidth; }
        }

        public void CancelDrag()
        {
            _dragging = false;
            _resizing = false;
            foreach (Label name in _nameControls) name.Capture = false;
            _resizeGrip.Capture = false;
        }

        public void SetAppearance(int width, float fontSize)
        {
            _preferredWidth = AppSettings.NormalizeOverlayWidth(width);
            fontSize = AppSettings.NormalizeOverlayFontSize(fontSize);
            if (_fontSize != fontSize)
            {
                _fontSize = fontSize;
                ApplyFonts();
            }
            LayoutRows();
        }

        public void SetTypography(string family, bool namesBold, bool amountsBold)
        {
            family = AppSettings.NormalizeOverlayFontFamily(family);
            if (_fontFamily == family && _namesBold == namesBold && _amountsBold == amountsBold) return;
            _fontFamily = family;
            _namesBold = namesBold;
            _amountsBold = amountsBold;
            ApplyFonts();
            LayoutRows();
        }

        private void ApplyFonts()
        {
            ApplyFont(_nameControls, new Font(_fontFamily, _fontSize, _namesBold ? FontStyle.Bold : FontStyle.Regular));
            ApplyFont(_amountControls, new Font(_fontFamily, _fontSize + 1f, _amountsBold ? FontStyle.Bold : FontStyle.Regular));
        }

        private static void ApplyFont(Label[] controls, Font font)
        {
            Font previous = controls[0].Font;
            // WinForms keeps the previous instance when the assigned font is value-equal.
            if (previous.Equals(font))
            {
                font.Dispose();
                return;
            }
            foreach (Label control in controls) control.Font = font;
            previous.Dispose();
        }

        public void SetColors(int textArgb, int attentionArgb, bool previewAttention = false)
        {
            _textColor = AppSettings.NormalizeOverlayColor(textArgb, AppSettings.DefaultOverlayTextColor);
            _attentionColor = AppSettings.NormalizeOverlayColor(attentionArgb, AppSettings.DefaultOverlayAttentionColor);
            _previewAttentionColor = previewAttention;
            foreach (Label name in _nameControls) name.ForeColor = _textColor;
            _resizeGrip.ForeColor = _textColor;
            UpdateRefreshState();
        }

        public Point RelativeOffset
        {
            get
            {
                return new Point(
                    Left - _frontolBounds.Left,
                    Top - _frontolBounds.Top);
            }
        }

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams parameters = base.CreateParams;
                parameters.ExStyle |=
                    ExtendedStyleToolWindow |
                    ExtendedStyleNoActivate |
                    ExtendedStyleLayered;
                return parameters;
            }
        }

        public void SetRows(IReadOnlyList<OverlayRow> rows)
        {
            int count = Math.Max(1, Math.Min(2, rows == null ? 0 : rows.Count));
            bool dataStale = false;
            _rowCount = count;
            for (int index = 0; index < 2; index++)
            {
                bool visible = index < count && rows != null && index < rows.Count;
                _nameControls[index].Visible = visible;
                _amountControls[index].Visible = visible;
                if (!visible)
                {
                    _nameControls[index].Text = string.Empty;
                    _amountControls[index].Text = string.Empty;
                    continue;
                }

                OverlayRow row = rows[index];
                _nameControls[index].Text = row.OrganizationName;
                _amountControls[index].Text = row.AmountText;
                dataStale = dataStale || row.IsStale;
            }

            SetDataStale(dataStale);
            LayoutRows();
        }

        private void LayoutRows()
        {
            using (Graphics graphics = CreateGraphics())
            {
                LayoutRows(graphics);
            }
        }

        internal void LayoutRows(Graphics graphics)
        {
            float scale = graphics.DpiX / 96f;
            _layoutScale = scale;
            Func<int, int> pixels = value => (int)Math.Ceiling(value * scale);
            graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            int amountWidth = 0;
            for (int index = 0; index < _rowCount; index++)
            {
                amountWidth = Math.Max(amountWidth, (int)Math.Ceiling(
                    graphics.MeasureString(_amountControls[index].Text,
                        _amountControls[index].Font).Width) + pixels(8));
            }

            int nameWidth = Math.Max(pixels(64), pixels(_preferredWidth) - amountWidth - pixels(24));
            int rowHeight = Math.Max(pixels(32),
                (int)Math.Ceiling(_amountControls[0].Font.GetHeight(graphics)) + pixels(2));
            int rowStep = rowHeight + pixels(4);
            for (int index = 0; index < 2; index++)
            {
                _nameControls[index].Bounds = new Rectangle(
                    pixels(4), pixels(4) + index * rowStep, nameWidth, rowHeight);
                _amountControls[index].Bounds = new Rectangle(
                    _nameControls[index].Right + pixels(4),
                    pixels(4) + index * rowStep, amountWidth, rowHeight);
            }
            Height = pixels(10) + _rowCount * rowStep;
            _resizeGrip.Bounds = new Rectangle(
                _amountControls[0].Right + pixels(2), 0, pixels(12), Height);
            Width = _resizeGrip.Right + pixels(2);
        }

        internal Bitmap RenderBitmap()
        {
            var bitmap = new Bitmap(Width, Height, PixelFormat.Format32bppPArgb);
            bitmap.SetResolution(96f * _layoutScale, 96f * _layoutScale);
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(Color.Transparent);
                for (int index = 0; index < _rowCount; index++)
                {
                    DrawPart(graphics, _nameControls[index]);
                    DrawPart(graphics, _amountControls[index]);
                }
                DrawPart(graphics, _resizeGrip);
            }
            return bitmap;
        }

        private static void DrawPart(Graphics graphics, Label part)
        {
            OverlayTextLabel.DrawText(graphics, part.Text, part.Font, part.ForeColor, part.Bounds, part.TextAlign);
        }

        private void QueueRender(object sender, EventArgs eventArgs)
        {
            if (_renderQueued || !IsHandleCreated || !Visible || Disposing || IsDisposed) return;
            _renderQueued = true;
            BeginInvoke(new Action(() =>
            {
                _renderQueued = false;
                if (!Disposing && !IsDisposed && Visible) RenderSurface();
            }));
        }

        protected override void OnVisibleChanged(EventArgs eventArgs)
        {
            base.OnVisibleChanged(eventArgs);
            QueueRender(this, eventArgs);
        }

        protected override void OnHandleDestroyed(EventArgs eventArgs)
        {
            _renderQueued = false;
            base.OnHandleDestroyed(eventArgs);
        }

        private void RenderSurface()
        {
            // UpdateLayeredWindow consumes premultiplied alpha. No color key or background sampling.
            using (Bitmap bitmap = RenderBitmap())
            {
                IntPtr dc = NativeMethods.CreateCompatibleDC(IntPtr.Zero);
                if (dc == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
                IntPtr image = IntPtr.Zero;
                IntPtr previous = IntPtr.Zero;
                try
                {
                    image = bitmap.GetHbitmap(Color.FromArgb(0));
                    previous = NativeMethods.SelectObject(dc, image);
                    if (previous == IntPtr.Zero || previous == new IntPtr(-1))
                        throw new Win32Exception(Marshal.GetLastWin32Error());
                    Point destination = Location;
                    Point source = Point.Empty;
                    Size size = bitmap.Size;
                    var blend = new NativeMethods.BlendFunction { SourceConstantAlpha = 255, AlphaFormat = 1 };
                    if (!NativeMethods.UpdateLayeredWindow(Handle, IntPtr.Zero, ref destination, ref size,
                        dc, ref source, 0, ref blend, 2))
                        throw new Win32Exception(Marshal.GetLastWin32Error());
                }
                finally
                {
                    if (previous != IntPtr.Zero && previous != new IntPtr(-1)) NativeMethods.SelectObject(dc, previous);
                    if (image != IntPtr.Zero) NativeMethods.DeleteObject(image);
                    NativeMethods.DeleteDC(dc);
                }
            }
        }

        private static class NativeMethods
        {
            [StructLayout(LayoutKind.Sequential, Pack = 1)]
            internal struct BlendFunction
            {
                internal byte BlendOp;
                internal byte BlendFlags;
                internal byte SourceConstantAlpha;
                internal byte AlphaFormat;
            }

            [DllImport("user32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool UpdateLayeredWindow(IntPtr window, IntPtr destinationDc,
                ref Point destination, ref Size size, IntPtr sourceDc, ref Point source,
                uint colorKey, ref BlendFunction blend, uint flags);

            [DllImport("gdi32.dll", SetLastError = true)]
            internal static extern IntPtr CreateCompatibleDC(IntPtr dc);

            [DllImport("gdi32.dll", SetLastError = true)]
            internal static extern IntPtr SelectObject(IntPtr dc, IntPtr image);

            [DllImport("gdi32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool DeleteObject(IntPtr image);

            [DllImport("gdi32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool DeleteDC(IntPtr dc);
        }

        protected override void OnDpiChanged(DpiChangedEventArgs eventArgs)
        {
            base.OnDpiChanged(eventArgs);
            LayoutRows();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _toolTip.Dispose();
                _nameControls[0].Font.Dispose();
                _amountControls[0].Font.Dispose();
                _resizeGrip.Font.Dispose();
            }
            base.Dispose(disposing);
        }

        public void SetRefreshStatus(string status, bool failed = false)
        {
            _refreshStatus = status;
            _refreshFailed = failed;
            UpdateRefreshState();
        }

        public void SetDataStale(bool dataStale)
        {
            _dataStale = dataStale;
            UpdateRefreshState();
        }

        public void SetRefreshDeferred(bool refreshDeferred)
        {
            _refreshDeferred = refreshDeferred;
            UpdateRefreshState();
        }

        public void SetRefreshing(bool refreshing)
        {
            _refreshing = refreshing;
            if (refreshing)
            {
                _refreshStatus = string.Empty;
                _refreshFailed = false;
            }
            UpdateRefreshState();
        }

        private void RequestRefresh(object sender, MouseEventArgs eventArgs)
        {
            if (_refreshing || eventArgs.Button != MouseButtons.Left) return;
            EventHandler handler = RefreshRequested;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        private void UpdateRefreshState()
        {
            string status = _refreshing
                ? "Запрашиваются итоги терминала…"
                : !string.IsNullOrEmpty(_refreshStatus)
                    ? _refreshStatus
                    : _refreshDeferred
                        ? "Запрос ожидает завершения операции и восстановления журнала."
                        : _dataStale
                            ? "Данные устарели: журнал недоступен или закрытие смены ещё не завершено."
                            : string.Empty;
            string hint = "Двойной щелчок по сумме — обновить итоги всех организаций из терминала.";
            if (!string.IsNullOrEmpty(status)) hint = status + Environment.NewLine + hint;
            foreach (Label amount in _amountControls)
            {
                amount.Cursor = _refreshing ? Cursors.WaitCursor : Cursors.Hand;
                amount.ForeColor = _previewAttentionColor || _dataStale || _refreshDeferred || _refreshFailed
                    ? _attentionColor : _textColor;
                amount.AccessibleDescription = hint;
                _toolTip.SetToolTip(amount, hint);
            }
        }

        public void PlaceRelativeTo(
            Rectangle frontolBounds,
            int offsetX,
            int offsetY)
        {
            _frontolBounds = frontolBounds;
            if (IsUserDragging)
            {
                return;
            }

            int maximumX = Math.Max(frontolBounds.Left, frontolBounds.Right - Width);
            int maximumY = Math.Max(frontolBounds.Top, frontolBounds.Bottom - Height);
            int x = Math.Max(
                frontolBounds.Left,
                Math.Min(frontolBounds.Left + offsetX, maximumX));
            int y = Math.Max(
                frontolBounds.Top,
                Math.Min(frontolBounds.Top + offsetY, maximumY));
            Location = new Point(x, y);
        }

        private void BeginDrag(object sender, MouseEventArgs eventArgs)
        {
            if (eventArgs.Button != MouseButtons.Left)
            {
                return;
            }

            _dragging = true;
            _dragCursorOrigin = Cursor.Position;
            _dragFormOrigin = Location;
            ((Control)sender).Capture = true;
        }

        private void ContinueDrag(object sender, MouseEventArgs eventArgs)
        {
            if (!_dragging || eventArgs.Button != MouseButtons.Left)
            {
                return;
            }

            Point cursor = Cursor.Position;
            Location = new Point(
                _dragFormOrigin.X + cursor.X - _dragCursorOrigin.X,
                _dragFormOrigin.Y + cursor.Y - _dragCursorOrigin.Y);
        }

        private void EndDrag(object sender, MouseEventArgs eventArgs)
        {
            if (!_dragging)
            {
                return;
            }

            ((Control)sender).Capture = false;
            _dragging = false;
            CommitPosition();
        }

        private void BeginResize(object sender, MouseEventArgs eventArgs)
        {
            if (eventArgs.Button != MouseButtons.Left) return;
            _resizing = true;
            _resizeWidthOrigin = Width;
            _dragCursorOrigin = _resizeGrip.PointToScreen(eventArgs.Location);
            _resizeGrip.Capture = true;
        }

        private void ContinueResize(object sender, MouseEventArgs eventArgs)
        {
            if (!_resizing || eventArgs.Button != MouseButtons.Left) return;
            int width = (int)Math.Round(
                (_resizeWidthOrigin + _resizeGrip.PointToScreen(eventArgs.Location).X -
                    _dragCursorOrigin.X) / _layoutScale);
            SetAppearance(Math.Max(AppSettings.MinimumOverlayWidth, width), _fontSize);
        }

        private void EndResize(object sender, MouseEventArgs eventArgs)
        {
            if (!_resizing) return;
            _resizeGrip.Capture = false;
            _resizing = false;
            CommitPosition();
        }

        private void CommitPosition()
        {
            EventHandler handler = PositionCommitted;
            if (handler != null)
            {
                handler(this, EventArgs.Empty);
            }
        }
    }
}
