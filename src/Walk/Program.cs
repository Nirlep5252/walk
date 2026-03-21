using Velopack;
using Walk.Services;

namespace Walk;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        var singleInstanceManager = new SingleInstanceManager("Walk");
        if (!singleInstanceManager.IsPrimaryInstance)
        {
            singleInstanceManager.SignalPrimaryInstance();
            singleInstanceManager.Dispose();
            return;
        }

        try
        {
            VelopackApp.Build()
                .SetArgs(args)
                .SetAutoApplyOnStartup(true)
                .Run();
        }
        catch
        {
            // Local debug runs should still start even if Velopack is unavailable.
        }

        var app = new App(singleInstanceManager);
        app.InitializeComponent();
        app.Run();
    }
}
