using System;
using System.Threading;
using System.Windows;

namespace SystemSquire
{
    public partial class App : Application
    {
        private const string SingleInstanceMutexName = "SystemSquireSingleInstance";
        private const string ReplaceInstanceEventName = "SystemSquireReplaceInstance";
        private Mutex? _singleInstanceMutex;
        private EventWaitHandle? _replaceInstanceEvent;
        private RegisteredWaitHandle? _replaceInstanceRegistration;
        private bool _ownsSingleInstanceMutex;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Keep one active instance by replacing any currently running instance.
            _singleInstanceMutex = new Mutex(true, SingleInstanceMutexName, out bool createdNew);
            _replaceInstanceEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ReplaceInstanceEventName);

            if (createdNew)
            {
                _ownsSingleInstanceMutex = true;
            }
            else
            {
                _replaceInstanceEvent.Set();

                try
                {
                    _ownsSingleInstanceMutex = _singleInstanceMutex.WaitOne(TimeSpan.FromSeconds(15));
                }
                catch (AbandonedMutexException)
                {
                    _ownsSingleInstanceMutex = true;
                }

                if (!_ownsSingleInstanceMutex)
                {
                    MessageBox.Show(
                        "Could not close the currently running System Squire instance.",
                        "System Squire",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    Current.Shutdown();
                    return;
                }
            }

            _replaceInstanceRegistration = ThreadPool.RegisterWaitForSingleObject(
                _replaceInstanceEvent,
                (_, _) =>
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (Current?.MainWindow is MainWindow mainWindow)
                        {
                            _ = mainWindow.ExitForInstanceReplacementAsync();
                            return;
                        }

                        Current?.Shutdown();
                    }));
                },
                null,
                Timeout.Infinite,
                executeOnlyOnce: false);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _replaceInstanceRegistration?.Unregister(null);
            _replaceInstanceRegistration = null;

            _replaceInstanceEvent?.Dispose();
            _replaceInstanceEvent = null;

            if (_singleInstanceMutex != null)
            {
                if (_ownsSingleInstanceMutex)
                {
                    try
                    {
                        _singleInstanceMutex.ReleaseMutex();
                    }
                    catch (ApplicationException)
                    {
                        // Ignore if ownership was already lost during shutdown.
                    }
                }

                _singleInstanceMutex.Dispose();
                _singleInstanceMutex = null;
            }

            base.OnExit(e);
        }
    }
}
