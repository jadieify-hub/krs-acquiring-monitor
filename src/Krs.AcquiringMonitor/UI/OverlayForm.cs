using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Text;
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
                graphics.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;
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
        private static readonly Color AttentionColor = Color.FromArgb(255, 190, 90);
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
        private float _layoutScale = 1f;
        private bool _resizing;
        private int _resizeWidthOrigin;

        public OverlayForm()
        {
            // Measure and arrange with the same DPI as DrawString; do not scale twice.
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Color.Fuchsia;
            TransparencyKey = Color.Fuchsia;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;

            var nameFont = new Font("Segoe UI", 15.5f, FontStyle.Regular);
            var amountFont = new Font("Segoe UI Semibold", 16.5f, FontStyle.Regular);
            _nameControls = new Label[2];
            _amountControls = new Label[2];

            for (int index = 0; index < 2; index++)
            {
                var name = new OverlayTextLabel
                {
                    AutoSize = false,
                    BackColor = Color.Transparent,
                    ForeColor = Color.White,
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
                    ForeColor = Color.White,
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
                ForeColor = Color.White,
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
                Font oldNameFont = _nameControls[0].Font;
                Font oldAmountFont = _amountControls[0].Font;
                var nameFont = new Font("Segoe UI", fontSize, FontStyle.Regular);
                var amountFont = new Font("Segoe UI Semibold", fontSize + 1f, FontStyle.Regular);
                for (int i = 0; i < 2; i++)
                {
                    _nameControls[i].Font = nameFont;
                    _amountControls[i].Font = amountFont;
                }
                oldNameFont.Dispose();
                oldAmountFont.Dispose();
                _fontSize = fontSize;
            }
            LayoutRows();
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
                    ExtendedStyleNoActivate;
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
                    continue;
                }

                OverlayRow row = rows[index];
                _nameControls[index].Text = row.OrganizationName;
                _nameControls[index].ForeColor = Color.White;
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
            graphics.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;
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
                amount.ForeColor = _dataStale || _refreshDeferred || _refreshFailed
                    ? AttentionColor : Color.White;
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
