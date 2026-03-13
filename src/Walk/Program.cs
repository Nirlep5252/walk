using Velopack;

namespace Walk;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
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

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
