using Microsoft.UI.Xaml;

namespace AmtPtpControlPanel.WinUI3
{
    public partial class App : Application
    {
        private Window? _window;

        public App()
        {
            InitializeComponent();
            LocalizationManager.Initialize();
        }

        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
            var window = new MainWindow();

            // Set the size before activation so the window does not visibly
            // resize shortly after it appears.
            window.AppWindow.Resize(new Windows.Graphics.SizeInt32(1121, 774));

            _window = window;
            window.Activate();
        }
    }
}
