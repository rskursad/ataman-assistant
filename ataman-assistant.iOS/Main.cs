using UIKit;

namespace AtamanAssistant.iOS;

// This is the main entry point of the application.
public static class Application
{
    // If you want to use a different Application Delegate class from "AppDelegate"
    // you can specify it here.
    public static void Main(string[] args)
    {
        UIApplication.Main(args, null, typeof(AppDelegate));
    }
}