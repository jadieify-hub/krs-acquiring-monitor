using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Krs.AcquiringMonitor.UI
{
    internal sealed class DiagnosticsForm : Form
    {
        public DiagnosticsForm(Func<string> readDiagnostics)
        {
            Text = "Диагностика — " + AppConstants.ApplicationName;
            Icon = AppConstants.Icon;
            Font = new Font("Segoe UI", 9.5f);
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(740, 450);
            MinimumSize = new Size(640, 430);
            MinimizeBox = false;

            var details = new TextBox
            {
                ReadOnly = true, Multiline = true, ScrollBars = ScrollBars.Vertical,
                Bounds = new Rectangle(12, 12, 716, 338),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                Font = new Font("Consolas", 9.5f),
                Text = readDiagnostics(), AccessibleName = "Техническое состояние монитора"
            };
            Controls.Add(details);
            Controls.Add(new Label
            {
                Text = "Без чеков, сумм и реквизитов. Ничего не отправляется автоматически.\r\n" +
                    "Перед отправкой проверьте пути. Состояние Frontol фиксируется при открытии этого окна.",
                Bounds = new Rectangle(12, 358, 716, 44),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
            });
            var refresh = new Button
            {
                Text = "Обновить", Bounds = new Rectangle(342, 410, 120, 28),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right
            };
            refresh.Click += delegate { details.Text = readDiagnostics(); };
            var copy = new Button
            {
                Text = "Скопировать", Bounds = new Rectangle(470, 410, 130, 28),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right
            };
            copy.Click += delegate
            {
                try { Clipboard.SetText(details.Text); }
                catch (ExternalException)
                {
                    MessageBox.Show(this, "Буфер обмена занят. Попробуйте ещё раз.",
                        Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            };
            var close = new Button
            {
                Text = "Закрыть", Bounds = new Rectangle(608, 410, 120, 28),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right, DialogResult = DialogResult.OK
            };
            close.Click += delegate { Close(); };
            Controls.AddRange(new Control[] { refresh, copy, close });
            AcceptButton = close;
            CancelButton = close;
        }
    }
}
