using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using Deleguard.Desktop.Core;
using System.Text;
using System.Text.Json;

namespace Deleguard.Desktop;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if(args.Length==2 && args[0]=="--bridge")return Bridge(args[1]).GetAwaiter().GetResult();
        if(args.Length==2 && args[0]=="--session")return Session(args[1]).GetAwaiter().GetResult();
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }
    public static AppBuilder BuildAvaloniaApp()=>AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont();
    private static async Task<int> Session(string path)
    {
        Console.InputEncoding=Encoding.UTF8;Console.OutputEncoding=new UTF8Encoding(false);
        try
        {
            var password=await Console.In.ReadLineAsync();
            if(password is null||password.Length>8192)throw new SetupException("vault_locked");
            await using var session=await CredentialSession.Unlock(ProjectFiles.ReadConnection(path),JsonSerializer.Deserialize<string>(password));
            password=null;Console.WriteLine("ready");await Console.Out.FlushAsync();
            // EOF when the visual installer closes or exits unexpectedly locks the vault.
            await Console.In.ReadLineAsync();return 0;
        }
        catch(Exception ex){Console.WriteLine(ex is SetupException e?e.Code:"session_locked");return 2;}
    }
    private static async Task<int> Bridge(string path)
    {
        Console.InputEncoding=Encoding.UTF8;Console.OutputEncoding=new UTF8Encoding(false);
        ApiReply reply;
        try
        {
            var buffer=new char[1_048_577];var length=0;int read;
            while(length<buffer.Length && (read=await Console.In.ReadAsync(buffer.AsMemory(length)))>0)length+=read;
            if(length>1_048_576)throw new SetupException("invalid_request");
            var request=JsonSerializer.Deserialize<ApiRequest>(new string(buffer,0,length))??throw new SetupException("invalid_request");
            reply=await CredentialSession.Call(ProjectFiles.ReadConnection(path),request);
        }
        catch(Exception ex){reply=new ApiReply(0,Error:ex is SetupException e?e.Code:"connection_failed");}
        Console.WriteLine(JsonSerializer.Serialize(reply));return reply.Error is null?0:2;
    }
}
public sealed class App : Application
{
    public override void Initialize()
    {
        RequestedThemeVariant=Avalonia.Styling.ThemeVariant.Light;
        Styles.Add(new FluentTheme());
    }
    public override void OnFrameworkInitializationCompleted()
    {
        if(ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)desktop.MainWindow=new MainWindow();
        base.OnFrameworkInitializationCompleted();
    }
}
