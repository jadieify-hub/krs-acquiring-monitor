using System;
using System.Collections;
using Krs.AcquiringMonitor.UI;

namespace Krs.AcquiringMonitor.Tests
{
    internal static class SupportConfigurationTests
    {
        private const string ApprovedSupportUrl =
            "https://pay.cloudtips.ru/p/2f23e8c9";

        public static void DisplaysApprovedCloudTipsPage()
        {
            Type supportFormType = typeof(OverlayPresentation).Assembly.GetType(
                "Krs.AcquiringMonitor.UI.SupportForm",
                true);

            object form = Activator.CreateInstance(supportFormType);
            try
            {
                IEnumerable controls = (IEnumerable)supportFormType
                    .GetProperty("Controls")
                    .GetValue(form, null);
                string displayedUrl = null;
                foreach (object control in controls)
                {
                    Type controlType = control.GetType();
                    if (controlType.FullName != "System.Windows.Forms.TextBox")
                    {
                        continue;
                    }

                    bool readOnly = (bool)controlType
                        .GetProperty("ReadOnly")
                        .GetValue(control, null);
                    if (readOnly)
                    {
                        displayedUrl = (string)controlType
                            .GetProperty("Text")
                            .GetValue(control, null);
                        break;
                    }
                }

                TestAssert.Equal(ApprovedSupportUrl, displayedUrl);
            }
            finally
            {
                ((IDisposable)form).Dispose();
            }
        }
    }
}
