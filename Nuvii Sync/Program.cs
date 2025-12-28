using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;

namespace Nuvii_Sync
{
    /// <summary>
    /// Custom entry point for the application with single-instance handling.
    /// Uses Windows App SDK AppInstance API for proper single-instance in packaged apps.
    /// </summary>
    public static class Program
    {
        private const string AppInstanceKey = "NuviiSyncMainInstance";

        [STAThread]
        static int Main(string[] args)
        {
            // Initialize COM wrappers for WinRT interop (must be first)
            WinRT.ComWrappersSupport.InitializeComWrappers();

            // Check if this is the first instance
            var isMainInstance = DecideRedirection();

            if (!isMainInstance)
            {
                // Another instance is running, we've redirected to it
                Debug.WriteLine("Redirected to existing instance. Exiting.");
                return 0;
            }

            // This is the main instance, start the app
            Application.Start((p) =>
            {
                var context = new DispatcherQueueSynchronizationContext(
                    DispatcherQueue.GetForCurrentThread());
                SynchronizationContext.SetSynchronizationContext(context);
                new App();
            });

            return 0;
        }

        /// <summary>
        /// Determines if this is the main instance or if we should redirect to an existing one.
        /// Uses Windows App SDK AppInstance for proper single-instance handling in packaged apps.
        /// </summary>
        private static bool DecideRedirection()
        {
            try
            {
                // Get or register this instance with a key
                var mainInstance = AppInstance.FindOrRegisterForKey(AppInstanceKey);

                // Check if we are the main instance
                if (!mainInstance.IsCurrent)
                {
                    // We're not the main instance, redirect activation to the existing one
                    var activationArgs = AppInstance.GetCurrent().GetActivatedEventArgs();
                    RedirectActivationTo(mainInstance, activationArgs);
                    return false;
                }

                // We are the main instance, register for future activations
                mainInstance.Activated += OnActivated;
                return true;
            }
            catch (Exception ex)
            {
                // If AppInstance fails (e.g., unpackaged scenario), proceed as single instance
                Debug.WriteLine($"AppInstance check failed, proceeding: {ex.Message}");
                return true;
            }
        }

        /// <summary>
        /// Redirects activation to an existing instance.
        /// </summary>
        private static void RedirectActivationTo(AppInstance instance, AppActivationArguments args)
        {
            try
            {
                // This will activate the other instance
                instance.RedirectActivationToAsync(args).AsTask().Wait();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to redirect activation: {ex.Message}");
            }
        }

        /// <summary>
        /// Called when another instance redirects activation to us.
        /// </summary>
        private static void OnActivated(object? sender, AppActivationArguments args)
        {
            // Bring the app to foreground when another instance tries to start
            try
            {
                // Use dispatcher to update UI on the correct thread
                var dispatcherQueue = App.Current?.GetSettingsWindow() is Window window
                    ? window.DispatcherQueue
                    : null;

                dispatcherQueue?.TryEnqueue(() =>
                {
                    App.Current?.ShowMainWindow();
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error handling activation: {ex.Message}");
            }
        }
    }
}
