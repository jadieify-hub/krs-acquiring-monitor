using System;

namespace Krs.AcquiringMonitor.Updates
{
    public sealed class UpdateSchedule
    {
        private DateTimeOffset _nextCheck;
        private DateTimeOffset _installAfter;
        private long _lastRevision = -1;

        public UpdateSchedule(DateTimeOffset now)
        {
            _nextCheck = now.AddSeconds(3);
            _installAfter = now.AddSeconds(30);
        }

        public bool TryBeginCheck(DateTimeOffset now)
        {
            if (now < _nextCheck)
            {
                return false;
            }
            _nextCheck = now.AddHours(6);
            return true;
        }

        public bool CanInstall(DateTimeOffset now, long revision, bool busy)
        {
            if (busy || revision != _lastRevision)
            {
                _lastRevision = revision;
                _installAfter = now.AddSeconds(30);
            }
            return !busy && now >= _installAfter;
        }
    }
}
