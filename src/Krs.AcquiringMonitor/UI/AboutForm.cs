using System.Drawing;
using System.Windows.Forms;

namespace Krs.AcquiringMonitor.UI
{
    internal sealed class AboutForm : Form
    {
        public AboutForm()
        {
            Text = "О программе";
            Font = new Font("Segoe UI", 9.5f);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(520, 270);

            var name = new Label
            {
                Text = AppConstants.ApplicationName,
                Font = new Font("Segoe UI Semibold", 15f, FontStyle.Bold),
                Left = 20,
                Top = 18,
                Width = 470,
                Height = 32
            };
            Controls.Add(name);

            var details = new Label
            {
                Text =
                    AppConstants.Description + "\r\n\r\n" +
                    "Версия: " + AppConstants.Version + "\r\n" +
                    "Разработчик: " + AppConstants.Developer + "\r\n" +
                    "Издатель и владелец: " + AppConstants.Publisher + "\r\n\r\n" +
                    "Программа читает журналы UPOS и вызывает только функцию текущей статистики " +
                    "_get_statistics. ККТ и закрытие банковской смены не используются.",
                Left = 20,
                Top = 62,
                Width = 475,
                Height = 155
            };
            Controls.Add(details);

            var close = new Button
            {
                Text = "Закрыть",
                Left = 400,
                Top = 225,
                Width = 95,
                Height = 30,
                DialogResult = DialogResult.OK
            };
            Controls.Add(close);
            AcceptButton = close;
            CancelButton = close;
        }
    }
}
