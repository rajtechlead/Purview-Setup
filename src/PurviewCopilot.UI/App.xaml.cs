using System.Windows;
using System.Windows.Threading;
using PurviewCopilot.UI.ViewModels;

namespace PurviewCopilot.UI;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) => ShowFatal(args.ExceptionObject as Exception);

        // Windows Server 2019 / Windows 10 1809 (build 17763) is the supported floor.
        // Older, end-of-life builds are refused rather than allowed to fail unpredictably.
        if (Environment.OSVersion.Version.Build < 17763)
        {
            MessageBox.Show(
                "Purview Governance Copilot requires Windows 10 version 1809 or later, " +
                "Windows 11, or Windows Server 2019/2022/2025. End-of-life operating systems are not supported.",
                "Unsupported operating system", MessageBoxButton.OK, MessageBoxImage.Stop);
            Shutdown(1);
            return;
        }

        try
        {
            var services = AppServices.Create();

            var pages = new PageViewModel[]
            {
                new DashboardViewModel(services.Workspace, services.Store, services.Store),
                new AssessmentViewModel(services.Workspace, services.Store, services.Clock),
                new DeploymentViewModel(services.Workspace, services.Store, services.Clock),
                new MonitoringViewModel(services.Workspace, services.Store, services.Store),
                new ReportsViewModel(services.Workspace),
                new AiAssistantViewModel(services.Copilot, services.Workspace, services.Usage, services.Store),
                new SettingsViewModel(services.Workspace, services.Store, services.Certificates, services.Verifier, services.Store),
                new HelpViewModel(),
                new AboutViewModel()
            };

            var shell = new ShellViewModel(services.Workspace, pages);
            var window = new MainWindow { DataContext = shell };
            MainWindow = window;
            window.Show();

            _ = shell.InitializeAsync();
        }
        catch (Exception ex)
        {
            ShowFatal(ex);
            Shutdown(1);
        }
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        MessageBox.Show(
            $"An unexpected error occurred.\n\n{e.Exception.Message}\n\nDetails were written to the local audit log where possible.",
            "Purview Governance Copilot",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }

    private static void ShowFatal(Exception? exception)
    {
        MessageBox.Show(
            $"Purview Governance Copilot could not start.\n\n{exception?.Message}\n\n{exception?.StackTrace}",
            "Startup failure",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }
}
