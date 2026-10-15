using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Deleguard.Desktop;

[assembly:AvaloniaTestApplication(typeof(Deleguard.Desktop.Tests.TestAppBuilder))]
namespace Deleguard.Desktop.Tests;

public class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp()=>AppBuilder.Configure<App>().WithInterFont().UseSkia().UseHeadless(new(){UseHeadlessDrawing=false});
}
public class WindowTests
{
    [AvaloniaFact]
    public void Review_error_remains_actionable_after_language_change()
    {
        var window=new MainWindow();window.Show();
        try
        {
            var review=window.GetVisualDescendants().OfType<Button>().Single(b=>Equals(b.Content,"Review installation"));
            review.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(),t=>t.Text=="Import the connection profile downloaded from IDE Control.");
            var language=window.GetVisualDescendants().OfType<ComboBox>().Single(c=>c.Items.Cast<object>().Contains("Español"));
            language.SelectedIndex=1;
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(),t=>t.Text=="Importa el perfil de conexión descargado desde IDE Control.");
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<TextBlock>(),t=>t.Text=="Procesando…");
        }
        finally{window.Close();}
    }

    [AvaloniaFact]
    public void EnglishDefaultSpanishKeyboardAndNarrowLayout()
    {
        var window=new MainWindow();window.Show();
        try
        {
            var texts=window.GetVisualDescendants().OfType<TextBlock>();
            Assert.Contains(texts,t=>t.Text=="Protect this project.");
            var language=window.GetVisualDescendants().OfType<ComboBox>().Single(c=>c.Items.Cast<object>().Contains("Español"));
            language.Focus();window.KeyPressQwerty(PhysicalKey.Tab,RawInputModifiers.None);
            Assert.NotNull(window.FocusManager?.GetFocusedElement());
            var artifacts=Environment.GetEnvironmentVariable("DELEGUARD_UI_ARTIFACTS");
            if(artifacts is not null){Directory.CreateDirectory(artifacts);window.CaptureRenderedFrame()!.Save(Path.Combine(artifacts,"installer-en.png"));}
            language.SelectedIndex=1;window.Width=560;
            Assert.Contains(texts,t=>t.Text=="Protege este proyecto.");
            Assert.DoesNotContain(texts,t=>t.Text=="Protect this project.");
            if(artifacts is not null)window.CaptureRenderedFrame()!.Save(Path.Combine(artifacts,"installer-es-narrow.png"));
            Assert.True(window.Bounds.Width<=560);
        }
        finally{window.Close();}
    }
}
