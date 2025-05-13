namespace OpenWhoop.MauiApp
{
    public partial class App : Application
    {
        public static IServiceProvider Services { get; private set; }

        public App()
        {
            InitializeComponent();
        }

        protected override Window CreateWindow(IActivationState? activationState)
        {
            return new Window(new AppShell());
        }

        internal static void SetServices(IServiceProvider services)
        {
            Services = services;
        }
    }
}