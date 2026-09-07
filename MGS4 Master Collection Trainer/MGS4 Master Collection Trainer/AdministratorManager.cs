using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;
using System.Windows.Forms;

namespace MGS4_Master_Collection_Trainer
{
    internal static class AdministratorManager
    {
        internal static bool EnsureAdministrator()
        {
            try
            {
                if (IsAdministrator()) return true;
                RestartElevated();
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                ReportFailure("Administrator access was cancelled. The trainer will close. " +
                    "Start it again and approve the Windows prompt to use it.");
            }
            catch (Exception ex)
            {
                ReportFailure("The trainer could not start with administrator access and will close. " +
                    "Try right-clicking the trainer and choosing Run as administrator.\n\n" + ex.Message);
            }

            // After a successful restart only the elevated process continues.
            // A cancelled or failed request must never fall through to the game.
            return false;
        }

        private static bool IsAdministrator()
        {
            using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
                return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }

        private static void ReportFailure(string message)
        {
            LoggingManager.Instance.Log(message);
            MessageBox.Show(message, "MGS4 Trainer - Administrator access",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private static void RestartElevated()
        {
            using (Process process = Process.Start(new ProcessStartInfo
            {
                UseShellExecute = true,
                WorkingDirectory = Application.StartupPath,
                FileName = Application.ExecutablePath,
                Verb = "runas"
            }))
            {
                if (process == null)
                    throw new InvalidOperationException("Windows did not start the elevated trainer.");
            }
        }
    }
}
