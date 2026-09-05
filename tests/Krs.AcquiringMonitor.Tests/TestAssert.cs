using System;
using System.Collections.Generic;

namespace Krs.AcquiringMonitor.Tests
{
    internal static class TestAssert
    {
        public static void Equal<T>(T expected, T actual)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
            {
                throw new InvalidOperationException(
                    string.Format("Ожидалось: {0}; получено: {1}.", expected, actual));
            }
        }

        public static void True(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }

        public static void False(bool condition, string message)
        {
            True(!condition, message);
        }
    }
}
