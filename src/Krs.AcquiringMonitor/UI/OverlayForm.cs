using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Text;
using System.Windows.Forms;

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
        private readonly Label _refreshControl;
        private bool _dragging;
        private Point _dragCursorOrigin;
        private Point _dragFormOrigin;
        private Rectangle _frontolBounds;
        private bool _dataStale;
        private bool _refreshDeferred;
        private bool _refreshing;

        public OverlayForm()
        {
            AutoScaleMode = AutoScaleMode.Dpi;
            BackColor = Color.Fuchsia;
            TransparencyKey = Color.Fuchsia;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            Width = 470;
            Height = 48;

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
                    Left = 4,
                    Top = 4 + index * 36,
                    Width = 268,
                    Height = 32,
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
                    Left = 276,
                    Top = 4 + index * 36,
                    Width = 160,
                    Height = 32,
                    TextAlign = ContentAlignment.MiddleLeft
                };
                Controls.Add(amount);
                _amountControls[index] = amount;
            }

            _refreshControl = new OverlayTextLabel
            {
                AutoSize = false,
                BackColor = Color.Transparent,
                ForeColor = Color.White,
                Text = "↻",
                Font = new Font("Segoe UI Symbol", 17f, FontStyle.Bold),
                Left = 440,
                Top = 7,
                Width = 28,
                Height = 30,
                Cursor = Cursors.Hand,
                TextAlign = ContentAlignment.MiddleCenter,
                AccessibleName = "Обновить суммы из терминала",
                AccessibleRole = AccessibleRole.PushButton
            };
            _refreshControl.Click += delegate
            {
                EventHandler handler = RefreshRequested;
                if (handler != null)
                {
                    handler(this, EventArgs.Empty);
                }
            };
            Controls.Add(_refreshControl);
        }

        public event EventHandler RefreshRequested;

        public event EventHandler PositionCommitted;

        public bool IsUserDragging
        {
            get { return _dragging; }
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
            Height = 10 + count * 36;
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
                _amountControls[index].ForeColor = Color.White;
                dataStale = dataStale || row.IsStale;
            }

            SetDataStale(dataStale);
            _refreshControl.Top = Math.Max(5, (Height - _refreshControl.Height) / 2);
        }

        public void SetDataStale(bool dataStale)
        {
            _dataStale = dataStale;
            UpdateRefreshControl();
        }

        public void SetRefreshDeferred(bool refreshDeferred)
        {
            _refreshDeferred = refreshDeferred;
            UpdateRefreshControl();
        }

        public void SetRefreshing(bool refreshing)
        {
            _refreshing = refreshing;
            UpdateRefreshControl();
        }

        private void UpdateRefreshControl()
        {
            _refreshControl.Enabled = !_refreshing;
            _refreshControl.Text = _refreshing ? "…" : "↻";
            _refreshControl.Cursor = _refreshing
                ? Cursors.WaitCursor
                : Cursors.Hand;
            _refreshControl.ForeColor = !_refreshing && (_dataStale || _refreshDeferred)
                ? AttentionColor
                : Color.White;
        }

        public void PlaceRelativeTo(
            Rectangle frontolBounds,
            int offsetX,
            int offsetY)
        {
            _frontolBounds = frontolBounds;
            if (_dragging)
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
            EventHandler handler = PositionCommitted;
            if (handler != null)
            {
                handler(this, EventArgs.Empty);
            }
        }
    }
}
