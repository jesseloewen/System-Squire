using System.Windows;

namespace SystemSquire
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            
            // Ensure only one instance is running
            var mutex = new System.Threading.Mutex(true, "SystemSquireSingleInstance", out bool createdNew);
            
            if (!createdNew)
            {
                MessageBox.Show("System Squire is already running!", "System Squire", 
                    MessageBoxButton.OK, MessageBoxImage.Information);
                Current.Shutdown();
                return;
            }
        }
    }
}
