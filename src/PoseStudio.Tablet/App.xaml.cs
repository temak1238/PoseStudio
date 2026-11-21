namespace PoseStudio.Tablet;

public partial class App : Application
{
    private MainPage? _main;

    public App()
    {
        InitializeComponent();
        UserAppTheme = AppTheme.Dark;
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        _main = new MainPage();
        return new Window(new NavigationPage(_main) { BarBackgroundColor = Ui.Panel, BarTextColor = Colors.White });
    }

    protected override void OnSleep()
    {
        base.OnSleep();
        _main?.SaveState();
    }

    protected override void OnResume()
    {
        base.OnResume();
        _main?.OnAppResumed();
    }
}
