using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace Krs.AcquiringMonitor.UI
{
    internal sealed class SupportForm : Form
    {
        private Image _qrImage;

        public SupportForm()
        {
            Text = "Поддержать разработку";
            Font = new Font("Segoe UI", 9.5f);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(580, 245);

            var title = new Label
            {
                Text = "Добровольная поддержка разработки",
                Font = new Font("Segoe UI Semibold", 13f, FontStyle.Bold),
                Left = 18,
                Top = 18,
                Width = 390,
                Height = 28
            };
            Controls.Add(title);

            var description = new Label
            {
                Text =
                    "Если утилита оказалась полезной, проект можно поддержать через CloudTips. " +
                    "Это необязательно и никак не влияет на работу программы.",
                Left = 18,
                Top = 55,
                Width = 390,
                Height = 54
            };
            Controls.Add(description);

            var urlBox = new TextBox
            {
                Text = AppConstants.SupportUrl,
                ReadOnly = true,
                Left = 18,
                Top = 116,
                Width = 390
            };
            Controls.Add(urlBox);

            var openButton = new Button
            {
                Text = "Открыть",
                Left = 18,
                Top = 158,
                Width = 105,
                Height = 32
            };
            openButton.Click += delegate
            {
                Process.Start(
                    new ProcessStartInfo(AppConstants.SupportUrl)
                    {
                        UseShellExecute = true
                    });
            };
            Controls.Add(openButton);

            var copyButton = new Button
            {
                Text = "Копировать",
                Left = 132,
                Top = 158,
                Width = 112,
                Height = 32
            };
            copyButton.Click += delegate
            {
                Clipboard.SetText(AppConstants.SupportUrl);
            };
            Controls.Add(copyButton);

            var closeButton = new Button
            {
                Text = "Закрыть",
                Left = 253,
                Top = 158,
                Width = 105,
                Height = 32,
                DialogResult = DialogResult.OK
            };
            Controls.Add(closeButton);
            AcceptButton = closeButton;
            CancelButton = closeButton;

            string qrPath = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "support-qr.png");
            if (File.Exists(qrPath))
            {
                byte[] bytes = File.ReadAllBytes(qrPath);
                using (var stream = new MemoryStream(bytes))
                using (Image source = Image.FromStream(stream))
                {
                    _qrImage = new Bitmap(source);
                }

                var qr = new PictureBox
                {
                    Image = _qrImage,
                    SizeMode = PictureBoxSizeMode.Zoom,
                    Left = 430,
                    Top = 22,
                    Width = 128,
                    Height = 128
                };
                Controls.Add(qr);
            }

            var privacy = new Label
            {
                Text = "Открытие этого окна не отправляет никаких данных.",
                Left = 18,
                Top = 207,
                Width = 540,
                Height = 24,
                ForeColor = Color.DimGray
            };
            Controls.Add(privacy);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _qrImage != null)
            {
                _qrImage.Dispose();
                _qrImage = null;
            }

            base.Dispose(disposing);
        }
    }
}
