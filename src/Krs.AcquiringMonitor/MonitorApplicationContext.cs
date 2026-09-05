using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Krs.AcquiringMonitor.Configuration;
using Krs.AcquiringMonitor.Core.Monitoring;
using Krs.AcquiringMonitor.Core.Reports;
using Krs.AcquiringMonitor.Diagnostics;
using Krs.AcquiringMonitor.Frontol;
using Krs.AcquiringMonitor.Monitoring;
using Krs.AcquiringMonitor.Terminal;
using Krs.AcquiringMonitor.UI;
using Krs.AcquiringMonitor.Updates;

namespace Krs.AcquiringMonitor
{
    internal sealed class MonitorApplicationContext : ApplicationContext
    {
        private readonly SettingsStore _settingsStore;
        private readonly SafeLogger _logger;
        private readonly FrontolWindowTracker _frontolTracker;
        private readonly TerminalStatisticsClient _terminalClient;
        private readonly StartupUpdater _startupUpdater;
        private readonly CancellationTokenSource _lifetimeCancellation;
        private readonly OverlayForm _overlay;
        private readonly NotifyIcon _trayIcon;
        private readonly System.Windows.Forms.Timer _frontolTimer;
        private readonly System.Windows.Forms.Timer _maintenanceTimer;
        private readonly AutomaticNameRefreshPolicy _automaticNameRefreshPolicy;
        private System.Windows.Forms.Timer _updateTimer;
        private System.Windows.Forms.Timer _firstRunTimer;
        private AppSettings _settings;
        private BankLogMonitor _logMonitor;
        private Rectangle _lastFrontolBounds;
        private bool _refreshing;
        private bool _manualRefreshPending;
        private bool _manualRefreshNoticeShown;
        private bool _settingsEditing;
        private bool _exiting;

        public MonitorApplicationContext()
        {
            _settingsStore = new SettingsStore();
            _settings = _settingsStore.LoadSettings();
            string locatedDirectory = UposLocator.Find(_settings.UposDirectory);
            if (!string.IsNullOrEmpty(locatedDirectory))
            {
                _settings.UposDirectory = locatedDirectory;
            }

            _logger = new SafeLogger(_settingsStore.BaseDirectory);
            _startupUpdater = new StartupUpdater(_logger);
            _frontolTracker = new FrontolWindowTracker();
            _terminalClient = new TerminalStatisticsClient();
            _automaticNameRefreshPolicy = new AutomaticNameRefreshPolicy(
                DateTimeOffset.UtcNow.AddSeconds(30));
            _lifetimeCancellation = new CancellationTokenSource();
            _overlay = new OverlayForm();
            _overlay.RefreshRequested += RefreshFromTerminal;
            _overlay.PositionCommitted += SaveOverlayPosition;
            IntPtr unusedHandle = _overlay.Handle;

            _trayIcon = new NotifyIcon
            {
                Text = AppConstants.ApplicationName,
                Icon = SystemIcons.Information,
                ContextMenuStrip = CreateTrayMenu(),
                Visible = true
            };
            _trayIcon.DoubleClick += ShowSettings;

            _frontolTimer = new System.Windows.Forms.Timer
            {
                Interval = 250
            };
            _frontolTimer.Tick += TrackFrontol;
            _frontolTimer.Start();

            _maintenanceTimer = new System.Windows.Forms.Timer
            {
                Interval = 1000
            };
            _maintenanceTimer.Tick += CheckScheduledTerminalRefresh;
            _maintenanceTimer.Start();

            _updateTimer = new System.Windows.Forms.Timer
            {
                Interval = 3000
            };
            _updateTimer.Tick += CheckForUpdate;
            _updateTimer.Start();

            ApplyAutoStart();
            RestartLogMonitor();
            _logger.Write(
                SafeLogEvent.ApplicationStarted,
                "version=" + AppConstants.Version,
                null);

            if (!UposLocator.IsUposDirectory(_settings.UposDirectory))
            {
                _firstRunTimer = new System.Windows.Forms.Timer
                {
                    Interval = 500
                };
                _firstRunTimer.Tick += ShowFirstRunSettings;
                _firstRunTimer.Start();
            }
        }

        protected override void ExitThreadCore()
        {
            if (_exiting)
            {
                return;
            }

            _exiting = true;
            _lifetimeCancellation.Cancel();
            _terminalClient.CancelActiveQuery();
            _frontolTimer.Stop();
            _frontolTimer.Dispose();
            _maintenanceTimer.Stop();
            _maintenanceTimer.Dispose();
            if (_updateTimer != null)
            {
                _updateTimer.Stop();
                _updateTimer.Tick -= CheckForUpdate;
                _updateTimer.Dispose();
                _updateTimer = null;
            }

            if (_firstRunTimer != null)
            {
                _firstRunTimer.Stop();
                _firstRunTimer.Dispose();
                _firstRunTimer = null;
            }

            if (_logMonitor != null)
            {
                _logMonitor.SnapshotChanged -= LogSnapshotChanged;
                _logMonitor.Dispose();
                _logMonitor = null;
            }

            _overlay.Hide();
            _overlay.Dispose();
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _lifetimeCancellation.Dispose();
            base.ExitThreadCore();
        }

        private ContextMenuStrip CreateTrayMenu()
        {
            var menu = new ContextMenuStrip();
            menu.Items.Add("Настройки", null, ShowSettings);
            menu.Items.Add("Обновить", null, RefreshFromTerminal);
            menu.Items.Add("Сбросить положение", null, ResetOverlayPosition);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Справка", null, ShowAbout);
            menu.Items.Add("Поддержать разработку", null, ShowSupport);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Выход", null, ExitApplication);
            return menu;
        }

        private void RestartLogMonitor()
        {
            if (_logMonitor != null)
            {
                _logMonitor.SnapshotChanged -= LogSnapshotChanged;
                _logMonitor.Dispose();
            }

            RuntimeState loadedState = _settingsStore.LoadRuntimeState();
            RuntimeState savedState =
                loadedState != null &&
                loadedState.MatchesSourceDirectory(_settings.UposDirectory)
                    ? loadedState
                    : null;
            BankLogSnapshot fallback = savedState == null
                ? null
                : savedState.ToSnapshot(true);
            _logMonitor = new BankLogMonitor(
                _settings.UposDirectory,
                fallback,
                _logger,
                savedState == null ? string.Empty : savedState.ActiveLogFileName,
                savedState == null ? 0L : savedState.ActiveLogOffset,
                savedState == null ? string.Empty : savedState.ActiveLogPrefixHash,
                _settings.Organizations
                    .Where(item => item != null && item.Department > 0)
                    .Select(item => item.Department));
            _logMonitor.SnapshotChanged += LogSnapshotChanged;
            _logMonitor.Start();
            UpdateOverlay(_logMonitor.CurrentSnapshot);
        }

        private void LogSnapshotChanged(object sender, BankLogSnapshotEventArgs eventArgs)
        {
            var monitor = sender as BankLogMonitor;
            if (_exiting || _overlay.IsDisposed || monitor == null)
            {
                return;
            }

            Action update = delegate
            {
                if (_exiting || !ReferenceEquals(monitor, _logMonitor))
                {
                    return;
                }

                string activeLogFileName;
                long activeLogOffset;
                string activeLogPrefixHash;
                BankLogSnapshot snapshot = monitor.CaptureCheckpoint(
                    out activeLogFileName,
                    out activeLogOffset,
                    out activeLogPrefixHash);
                UpdateOverlay(snapshot);
                if (!RuntimeState.CanPersistSnapshot(snapshot))
                {
                    return;
                }

                try
                {
                    _settingsStore.SaveRuntimeState(
                        RuntimeState.FromSnapshot(
                            snapshot,
                            activeLogFileName,
                            activeLogOffset,
                            activeLogPrefixHash,
                            _settings.UposDirectory));
                }
                catch (IOException exception)
                {
                    _logger.Write(
                        SafeLogEvent.SettingsFailure,
                        "state",
                        exception);
                }
                catch (UnauthorizedAccessException exception)
                {
                    _logger.Write(
                        SafeLogEvent.SettingsFailure,
                        "state",
                        exception);
                }
            };

            if (_overlay.InvokeRequired)
            {
                try
                {
                    _overlay.BeginInvoke(update);
                }
                catch (InvalidOperationException)
                {
                }
            }
            else
            {
                update();
            }
        }

        private void UpdateOverlay(BankLogSnapshot snapshot)
        {
            _overlay.SetRows(
                OverlayPresentation.BuildRows(
                    snapshot,
                    _settings.GetOrganizationNames()));
        }

        private void TrackFrontol(object sender, EventArgs eventArgs)
        {
            FrontolWindowInfo info;
            if (!_frontolTracker.TryGetActive(out info))
            {
                if (_overlay.Visible)
                {
                    _overlay.Hide();
                }

                return;
            }

            _lastFrontolBounds = info.Bounds;
            _overlay.PlaceRelativeTo(
                info.Bounds,
                _settings.OverlayOffsetX,
                _settings.OverlayOffsetY);
            if (!_overlay.Visible)
            {
                _overlay.Show();
            }
        }

        private void RefreshFromTerminal(object sender, EventArgs eventArgs)
        {
            if (_refreshing || _manualRefreshPending)
            {
                return;
            }

            _manualRefreshPending = true;
            _manualRefreshNoticeShown = false;
            _overlay.SetRefreshDeferred(true);
            TryStartScheduledTerminalRefresh();
        }

        private void CheckScheduledTerminalRefresh(
            object sender,
            EventArgs eventArgs)
        {
            TryStartScheduledTerminalRefresh();
        }

        private async void CheckForUpdate(object sender, EventArgs eventArgs)
        {
            if (_settingsEditing || _refreshing || _updateTimer == null)
            {
                return;
            }

            System.Windows.Forms.Timer timer = _updateTimer;
            _updateTimer = null;
            timer.Stop();
            timer.Tick -= CheckForUpdate;
            timer.Dispose();

            bool installerStarted = await _startupUpdater.CheckAndInstallAsync(
                AppConstants.ApplicationVersion,
                _lifetimeCancellation.Token);
            if (installerStarted && !_exiting)
            {
                ExitThread();
            }
        }

        private void TryStartScheduledTerminalRefresh()
        {
            if (_exiting || _settingsEditing)
            {
                return;
            }

            BankLogMonitor monitor = _logMonitor;
            if (monitor == null)
            {
                ShowDeferredRefreshNotice();
                return;
            }

            bool interactive = _manualRefreshPending;
            if (_refreshing)
            {
                if (interactive)
                {
                    ShowDeferredRefreshNotice();
                }

                return;
            }

            DateTimeOffset now = DateTimeOffset.UtcNow;
            if (!interactive &&
                !_automaticNameRefreshPolicy.ShouldAttempt(
                    _settings,
                    monitor.CurrentSnapshot,
                    now))
            {
                return;
            }

            monitor.RefreshNow();
            long logRevision = monitor.CaptureRevision();
            if (monitor.HasPendingOperation || monitor.CurrentSnapshot.IsStale)
            {
                if (interactive)
                {
                    ShowDeferredRefreshNotice();
                }

                return;
            }

            _automaticNameRefreshPolicy.RecordAttempt(now);
            if (interactive)
            {
                _manualRefreshPending = false;
                _manualRefreshNoticeShown = false;
                _overlay.SetRefreshDeferred(false);
            }

            QueryTerminalAsync(monitor, logRevision, interactive);
        }

        private void ShowDeferredRefreshNotice()
        {
            if (!_manualRefreshPending || _manualRefreshNoticeShown)
            {
                return;
            }

            _manualRefreshNoticeShown = true;
            ShowBalloon(
                "Обновление запланировано",
                "Суммы обновятся автоматически сразу после завершения текущей операции и восстановления журнала UPOS.",
                ToolTipIcon.Warning);
        }

        private async void QueryTerminalAsync(
            BankLogMonitor monitor,
            long logRevision,
            bool interactive)
        {
            _refreshing = true;
            _overlay.SetRefreshing(true);
            _logger.Write(
                SafeLogEvent.TerminalQueryStarted,
                interactive ? "manual" : "automatic",
                null);

            try
            {
                TerminalStatisticsResult result = await _terminalClient.QueryAsync(
                    _settings.UposDirectory,
                    TimeSpan.FromSeconds(90),
                    _lifetimeCancellation.Token);
                if (_exiting)
                {
                    return;
                }

                if (!result.Success)
                {
                    _logger.Write(
                        SafeLogEvent.TerminalQueryFailed,
                        "helper",
                        null);
                    if (interactive)
                    {
                        ShowBalloon(
                            "Не удалось получить итоги",
                            result.ErrorMessage,
                            ToolTipIcon.Error);
                    }

                    return;
                }

                IReadOnlyList<OrganizationReport> reports =
                    StatisticsReportParser.Parse(result.ReportText);
                if (reports.Count < 1 || reports.Count > 2)
                {
                    _logger.Write(
                        SafeLogEvent.TerminalQueryFailed,
                        "report-format",
                        null);
                    if (interactive)
                    {
                        ShowBalloon(
                            "Отчёт не применён",
                            "Не удалось однозначно определить суммы организаций.",
                            ToolTipIcon.Warning);
                    }

                    return;
                }

                int[] departments = GetExpectedDepartments(reports.Count);
                IReadOnlyDictionary<int, DepartmentTotal> merged;
                if (!StatisticsSnapshotMerger.TryMerge(
                        departments,
                        _settings.GetBankOrganizationNames(),
                        reports,
                        out merged))
                {
                    _logger.Write(
                        SafeLogEvent.TerminalQueryFailed,
                        "partial-report",
                        null);
                    if (interactive)
                    {
                        ShowBalloon(
                            "Отчёт не применён",
                            "Терминал вернул неполный или неоднозначный отчёт.",
                            ToolTipIcon.Warning);
                    }

                    return;
                }

                var totals = merged.ToDictionary(
                    item => item.Key,
                    item => item.Value.AmountKopeks);
                monitor.RefreshNow();
                if (!ReferenceEquals(monitor, _logMonitor) ||
                    !monitor.TryApplyAuthoritativeTotals(totals, logRevision))
                {
                    if (interactive)
                    {
                        ShowBalloon(
                            "Отчёт не применён",
                            "Во время запроса началась банковская операция. Повторите позже.",
                            ToolTipIcon.Warning);
                    }

                    return;
                }

                List<OrganizationSetting> previousOrganizations =
                    _settings.Organizations
                        .Select(item => item == null
                            ? null
                            : new OrganizationSetting
                            {
                                Department = item.Department,
                                DisplayName = item.DisplayName,
                                IsManual = item.IsManual,
                                BankName = item.BankName
                            })
                        .ToList();
                RememberAutomaticNames(merged);
                if (!SaveSettings(interactive))
                {
                    _settings.Organizations = previousOrganizations;
                }
                UpdateOverlay(monitor.CurrentSnapshot);
                _logger.Write(
                    SafeLogEvent.TerminalQuerySucceeded,
                    "organizations=" + merged.Count,
                    null);
                if (interactive)
                {
                    ShowBalloon(
                        "Итоги обновлены",
                        "Суммы получены непосредственно из текущего отчёта терминала.",
                        ToolTipIcon.Info);
                }
            }
            catch (Exception exception)
            {
                _logger.Write(
                    SafeLogEvent.TerminalQueryFailed,
                    "unexpected",
                    exception);
                if (interactive && !_exiting)
                {
                    ShowBalloon(
                        "Не удалось получить итоги",
                        "Произошла техническая ошибка. Предыдущие суммы сохранены.",
                        ToolTipIcon.Error);
                }
            }
            finally
            {
                _refreshing = false;
                if (!_overlay.IsDisposed)
                {
                    _overlay.SetRefreshing(false);
                }
            }
        }

        private int[] GetExpectedDepartments(int reportCount)
        {
            IEnumerable<int> knownDepartments = _logMonitor.CurrentSnapshot.Departments
                .Concat(
                    _settings.Organizations
                        .Where(item => item != null && item.Department > 0)
                        .Select(item => item.Department));
            return ExpectedDepartmentResolver
                .Resolve(knownDepartments, reportCount)
                .ToArray();
        }

        private void RememberAutomaticNames(
            IReadOnlyDictionary<int, DepartmentTotal> totals)
        {
            foreach (KeyValuePair<int, DepartmentTotal> item in totals)
            {
                OrganizationSetting existing = _settings.Organizations
                    .LastOrDefault(value =>
                        value != null &&
                        value.Department == item.Key);
                if (existing == null)
                {
                    _settings.Organizations.Add(
                        new OrganizationSetting
                        {
                            Department = item.Key,
                            BankName = item.Value.OrganizationName,
                            DisplayName = item.Value.OrganizationName,
                            IsManual = false
                        });
                }
                else if (!existing.IsManual)
                {
                    existing.BankName = item.Value.OrganizationName;
                    existing.DisplayName = item.Value.OrganizationName;
                }
                else
                {
                    existing.BankName = item.Value.OrganizationName;
                }
            }
        }

        private void ShowSettings(object sender, EventArgs eventArgs)
        {
            if (_refreshing)
            {
                ShowBalloon(
                    "Настройки пока недоступны",
                    "Дождитесь завершения запроса итогов.",
                    ToolTipIcon.Warning);
                return;
            }

            bool accepted;
            _settingsEditing = true;
            try
            {
                accepted = SettingsForm.ShowEditor(
                    null,
                    _settings,
                    _logMonitor == null
                        ? new int[0]
                        : _logMonitor.CurrentSnapshot.Departments);
            }
            finally
            {
                _settingsEditing = false;
            }

            if (!accepted)
            {
                return;
            }

            SaveSettings();
            ApplyAutoStart();
            RestartLogMonitor();
        }

        private void ShowFirstRunSettings(object sender, EventArgs eventArgs)
        {
            _firstRunTimer.Stop();
            _firstRunTimer.Dispose();
            _firstRunTimer = null;
            ShowSettings(this, EventArgs.Empty);
        }

        private bool SaveSettings(bool notifyUser = true)
        {
            try
            {
                _settingsStore.SaveSettings(_settings);
                return true;
            }
            catch (IOException exception)
            {
                _logger.Write(
                    SafeLogEvent.SettingsFailure,
                    "settings",
                    exception);
                if (notifyUser)
                {
                    ShowBalloon(
                        "Настройки не сохранены",
                        "Не удалось записать настройки текущего пользователя.",
                        ToolTipIcon.Warning);
                }

                return false;
            }
            catch (UnauthorizedAccessException exception)
            {
                _logger.Write(
                    SafeLogEvent.SettingsFailure,
                    "settings",
                    exception);
                if (notifyUser)
                {
                    ShowBalloon(
                        "Настройки не сохранены",
                        "Нет доступа к папке настроек текущего пользователя.",
                        ToolTipIcon.Warning);
                }

                return false;
            }
        }

        private void ApplyAutoStart()
        {
            try
            {
                AutoStartManager.Apply(
                    _settings.AutoStart,
                    Application.ExecutablePath);
            }
            catch (Exception exception)
            {
                _logger.Write(
                    SafeLogEvent.SettingsFailure,
                    "autostart",
                    exception);
            }
        }

        private void SaveOverlayPosition(object sender, EventArgs eventArgs)
        {
            if (_lastFrontolBounds.Width <= 0)
            {
                return;
            }

            Point offset = _overlay.RelativeOffset;
            _settings.OverlayOffsetX = offset.X;
            _settings.OverlayOffsetY = offset.Y;
            _settings.HasCustomPosition = true;
            SaveSettings();
        }

        private void ResetOverlayPosition(object sender, EventArgs eventArgs)
        {
            AppSettings defaults = AppSettings.CreateDefault();
            _settings.OverlayOffsetX = defaults.OverlayOffsetX;
            _settings.OverlayOffsetY = defaults.OverlayOffsetY;
            _settings.HasCustomPosition = false;
            SaveSettings();
            if (_lastFrontolBounds.Width > 0)
            {
                _overlay.PlaceRelativeTo(
                    _lastFrontolBounds,
                    _settings.OverlayOffsetX,
                    _settings.OverlayOffsetY);
            }
        }

        private static void ShowAbout(object sender, EventArgs eventArgs)
        {
            using (var form = new AboutForm())
            {
                form.ShowDialog();
            }
        }

        private static void ShowSupport(object sender, EventArgs eventArgs)
        {
            using (var form = new SupportForm())
            {
                form.ShowDialog();
            }
        }

        private void ExitApplication(object sender, EventArgs eventArgs)
        {
            ExitThread();
        }

        private void ShowBalloon(
            string title,
            string text,
            ToolTipIcon icon)
        {
            _trayIcon.BalloonTipTitle = title;
            _trayIcon.BalloonTipText = text;
            _trayIcon.BalloonTipIcon = icon;
            _trayIcon.ShowBalloonTip(5000);
        }
    }
}
