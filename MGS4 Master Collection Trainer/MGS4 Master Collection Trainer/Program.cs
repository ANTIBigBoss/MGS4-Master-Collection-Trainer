using System;
using System.Windows.Forms;

namespace MGS4_Master_Collection_Trainer
{
    internal static class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            // The unelevated process must leave before creating forms or memory
            // managers, including the cleanup path for an active trainer session.
            if (!AdministratorManager.EnsureAdministrator()) return;

            try
            {
                Application.Run(new MainForm());
            }
            finally
            {
                try
                {
                    if (!EffectManager.Instance.DisableAll())
                        LoggingManager.Instance.Log("Table effect cleanup is incomplete: " + EffectManager.Instance.LastError);
                }
                catch (Exception ex) { LoggingManager.Instance.Log("Table effect cleanup failed: " + ex.Message); }
                try
                {
                    if (!StressManager.Instance.Disable())
                        LoggingManager.Instance.Log("Stress cleanup is incomplete: " + StressManager.Instance.LastError);
                }
                catch (Exception ex) { LoggingManager.Instance.Log("Stress cleanup failed: " + ex.Message); }
                finally
                {
                    try
                    {
                        if (!PlayerHookManager.Instance.Disable())
                            LoggingManager.Instance.Log("Player hook cleanup is incomplete: " + PlayerHookManager.Instance.LastError);
                    }
                    catch (Exception ex) { LoggingManager.Instance.Log("Player hook cleanup failed: " + ex.Message); }
                }
            }
        }
    }
}
