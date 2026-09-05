using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Krs.AcquiringMonitor.Configuration;

namespace Krs.AcquiringMonitor.UI
{
    internal sealed class SettingsForm : Form
    {
        private readonly AppSettings _settings;
        private readonly TextBox _directoryTextBox;
        private readonly CheckBox _autoStartCheckBox;
        private readonly Dictionary<int, TextBox> _nameEditors =
            new Dictionary<int, TextBox>();
        private readonly Dictionary<int, string> _originalNames =
            new Dictionary<int, string>();

        private SettingsForm(
            AppSettings settings,
            IEnumerable<int> discoveredDepartments)
        {
            _settings = settings;
            Text = AppConstants.ApplicationName + " — настройки";
            Font = new Font("Segoe UI", 9.5f);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(610, 320);

            var directoryLabel = new Label
            {
                Text = "Папка Сбербанка (UPOS / SC552):",
                Left = 18,
                Top = 18,
                Width = 350,
                Height = 22
            };
            Controls.Add(directoryLabel);

            _directoryTextBox = new TextBox
            {
                Left = 18,
                Top = 43,
                Width = 475,
                Text = settings.UposDirectory ?? string.Empty
            };
            Controls.Add(_directoryTextBox);

            var browseButton = new Button
            {
                Text = "Обзор…",
                Left = 502,
                Top = 41,
                Width = 90,
                Height = 28
            };
            browseButton.Click += BrowseDirectory;
            Controls.Add(browseButton);

            var namesGroup = new GroupBox
            {
                Text = "Названия организаций",
                Left = 18,
                Top = 82,
                Width = 574,
                Height = 138
            };
            Controls.Add(namesGroup);

            int[] departments = settings.Organizations
                .Where(item => item != null && item.Department > 0)
                .Select(item => item.Department)
                .Concat(discoveredDepartments ?? new int[0])
                .Where(value => value > 0)
                .Distinct()
                .OrderBy(value => value)
                .Take(2)
                .ToArray();
            if (departments.Length == 0)
            {
                departments = new[] { 1, 2 };
            }
            else if (departments.Length == 1)
            {
                int second = departments[0] == 1 ? 2 : 1;
                departments = departments.Concat(new[] { second }).OrderBy(value => value).ToArray();
            }

            for (int index = 0; index < departments.Length; index++)
            {
                int department = departments[index];
                OrganizationSetting existing = settings.Organizations
                    .LastOrDefault(item => item != null && item.Department == department);
                string existingName = existing == null
                    ? string.Empty
                    : existing.DisplayName ?? string.Empty;
                _originalNames[department] = existingName;

                var departmentLabel = new Label
                {
                    Text = "Отдел " + department + ":",
                    Left = 14,
                    Top = 30 + index * 42,
                    Width = 80,
                    Height = 24,
                    TextAlign = ContentAlignment.MiddleLeft
                };
                namesGroup.Controls.Add(departmentLabel);

                var editor = new TextBox
                {
                    Left = 95,
                    Top = 30 + index * 42,
                    Width = 455,
                    Text = existingName
                };
                namesGroup.Controls.Add(editor);
                _nameEditors.Add(department, editor);
            }

            var hint = new Label
            {
                Text = "Пустое поле заполняется из банковского отчёта. Ручное название не перезаписывается.",
                Left = 18,
                Top = 228,
                Width = 574,
                Height = 35,
                ForeColor = Color.DimGray
            };
            Controls.Add(hint);

            _autoStartCheckBox = new CheckBox
            {
                Text = "Запускать вместе с Windows",
                Left = 18,
                Top = 267,
                Width = 260,
                Checked = settings.AutoStart
            };
            Controls.Add(_autoStartCheckBox);

            var okButton = new Button
            {
                Text = "Сохранить",
                Left = 392,
                Top = 265,
                Width = 96,
                Height = 32,
                DialogResult = DialogResult.OK
            };
            okButton.Click += SaveAndClose;
            Controls.Add(okButton);

            var cancelButton = new Button
            {
                Text = "Отмена",
                Left = 496,
                Top = 265,
                Width = 96,
                Height = 32,
                DialogResult = DialogResult.Cancel
            };
            Controls.Add(cancelButton);

            AcceptButton = okButton;
            CancelButton = cancelButton;
        }

        public static bool ShowEditor(
            IWin32Window owner,
            AppSettings settings,
            IEnumerable<int> discoveredDepartments)
        {
            using (var form = new SettingsForm(settings, discoveredDepartments))
            {
                DialogResult result = owner == null
                    ? form.ShowDialog()
                    : form.ShowDialog(owner);
                return result == DialogResult.OK;
            }
        }

        private void BrowseDirectory(object sender, EventArgs eventArgs)
        {
            using (var dialog = new FolderBrowserDialog
            {
                Description = "Выберите папку, где находятся pilot_nt.dll и sbkernelYYMM.log",
                ShowNewFolderButton = false,
                SelectedPath = _directoryTextBox.Text
            })
            {
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    _directoryTextBox.Text = dialog.SelectedPath;
                }
            }
        }

        private void SaveAndClose(object sender, EventArgs eventArgs)
        {
            _settings.UposDirectory = _directoryTextBox.Text.Trim();
            _settings.AutoStart = _autoStartCheckBox.Checked;

            foreach (KeyValuePair<int, TextBox> item in _nameEditors)
            {
                int department = item.Key;
                string name = item.Value.Text.Trim();
                OrganizationSetting existing = _settings.Organizations
                    .LastOrDefault(value => value != null && value.Department == department);

                if (name.Length == 0)
                {
                    _settings.Organizations.RemoveAll(
                        value => value != null && value.Department == department);
                    continue;
                }

                bool changed = !string.Equals(
                    name,
                    _originalNames[department],
                    StringComparison.Ordinal);
                if (existing == null)
                {
                    _settings.Organizations.Add(
                        new OrganizationSetting
                        {
                            Department = department,
                            DisplayName = name,
                            IsManual = true
                        });
                }
                else
                {
                    existing.DisplayName = name;
                    existing.IsManual = existing.IsManual || changed;
                }
            }
        }
    }
}
