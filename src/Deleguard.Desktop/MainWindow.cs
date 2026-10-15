using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Deleguard.Desktop.Core;
using System.Security.Cryptography;
using System.Text.Json;

namespace Deleguard.Desktop;

public sealed class MainWindow : Window
{
    private readonly UiText t=new(); private readonly SetupService service=new();
    private readonly List<Action> translations=[];
    private readonly Dictionary<string,ProjectSessionProcess> sessions=new();
    private readonly TextBox root=new(){Watermark="C:\\Projects\\my-app"},key=new(){PasswordChar='●'},password=new(){PasswordChar='●'};
    private readonly ComboBox adapter=new(){ItemsSource=new[]{"Cursor","VS Code / Copilot","Claude Code"},SelectedIndex=0,HorizontalAlignment=HorizontalAlignment.Stretch};
    private readonly ComboBox language=new(){ItemsSource=new[]{"English","Español"},SelectedIndex=0,MinWidth=120};
    private readonly TextBlock profileLabel=new(){TextWrapping=TextWrapping.Wrap,Foreground=Brushes.DimGray};
    private readonly TextBlock status=new(){TextWrapping=TextWrapping.Wrap,FontSize=14};
    private readonly TextBlock files=new(){TextWrapping=TextWrapping.Wrap,FontFamily=FontFamily.Parse("monospace"),FontSize=11};
    private readonly CheckBox replace=new(),migrate=new();
    private readonly StackPanel actions=new(){Orientation=Orientation.Horizontal,Spacing=10};
    private readonly StackPanel checks=new(){Spacing=8};
    private readonly Border review;
    private readonly StackPanel disconnectConfirmation=new(){Spacing=12,IsVisible=false};
    private JsonElement? profile,plan; private string? boundRoot,lastMessage,lastError;private bool busy;
    private string Adapter=>new[]{"cursor","vscode","claude"}[Math.Clamp(adapter.SelectedIndex,0,2)];
    public MainWindow()
    {
        Width=1000;Height=900;MinWidth=560;MinHeight=600;Background=Brush.Parse("#F4F7FC");FontFamily="Inter";
        var page=new StackPanel{Spacing=20,Margin=new Thickness(36),MaxWidth=940,HorizontalAlignment=HorizontalAlignment.Center};
        var top=new Grid{ColumnDefinitions=new ColumnDefinitions("*,Auto")};
        top.Children.Add(new TextBlock{Text="◈  DeleGuard",FontWeight=FontWeight.Bold,FontSize=22,Foreground=Brush.Parse("#123653")});
        Grid.SetColumn(language,1);top.Children.Add(language);page.Children.Add(top);
        page.Children.Add(Text("PROJECT ONLY",11,"#1762F6"));page.Children.Add(Text("Protect this project.",32));
        page.Children.Add(Text("Connect your editor with clear limits. Everything stays inside the folder you choose.",15));
        var steps=new UniformGrid{Columns=3};foreach(var s in new[]{"1 · Choose","2 · Review","3 · Connect"})steps.Children.Add(Text(s,14,"#1762F6"));page.Children.Add(steps);
        var fields=new StackPanel{Spacing=14};
        fields.Children.Add(Text("Connection profile",16));fields.Children.Add(Text("Download it from IDE Control. It contains no API key.",13));
        fields.Children.Add(Button("Import profile",Import));fields.Children.Add(profileLabel);
        fields.Children.Add(Text("Project folder",14));var folder=new Grid{ColumnDefinitions=new ColumnDefinitions("*,Auto"),ColumnSpacing=10};folder.Children.Add(root);var browse=Button("Choose folder",ChooseFolder);Grid.SetColumn(browse,1);folder.Children.Add(browse);fields.Children.Add(folder);
        fields.Children.Add(Text("Editor",14));fields.Children.Add(adapter);
        fields.Children.Add(Text("Agent API key",14));fields.Children.Add(key);fields.Children.Add(Text("Paste the key from IDE Control. Leave empty to keep a working local key.",12));
        if(!OperatingSystem.IsWindows()){fields.Children.Add(Text("Vault password",14));fields.Children.Add(password);fields.Children.Add(Text("At least 12 characters. Used only to unlock this project; it is never sent to DeleGuard.",12));}
        translations.Add(()=>{replace.Content=t["Review customized files before replacing them"];migrate.Content=t["Migrate this existing project to local credentials"];});
        var advancedBody=new StackPanel{Spacing=12};advancedBody.Children.Add(replace);advancedBody.Children.Add(migrate);
        var advanced=new Expander{Content=advancedBody,HorizontalAlignment=HorizontalAlignment.Stretch};translations.Add(()=>advanced.Header=t["Existing installations and custom files"]);
        fields.Children.Add(advanced);fields.Children.Add(Button("Review installation",Preview,true));
        page.Children.Add(Card(fields));
        var reviewBody=new StackPanel{Spacing=12};reviewBody.Children.Add(Text("Review the changes",20));reviewBody.Children.Add(Text("Only this folder will change. Existing operation history and unrelated editor settings are preserved.",14));
        var details=new Expander{Content=files,HorizontalAlignment=HorizontalAlignment.Stretch};translations.Add(()=>details.Header=t["Files to write"]);reviewBody.Children.Add(details);
        reviewBody.Children.Add(Button("Install in this project",Install,true));review=Card(reviewBody);review.IsVisible=false;page.Children.Add(review);
        actions.Children.Add(Button("Check connection",Check));
        if(!OperatingSystem.IsWindows()){actions.Children.Add(Button("Unlock project",Unlock));actions.Children.Add(Button("Lock project",Lock));}
        page.Children.Add(actions);
        var feedback=new StackPanel{Spacing=12};feedback.Children.Add(status);feedback.Children.Add(checks);page.Children.Add(Card(feedback));
        var foot=new StackPanel{Spacing=10};foot.Children.Add(Text("No global settings or shared credentials",13));
        var buttons=new StackPanel{Orientation=Orientation.Horizontal,Spacing=12};buttons.Children.Add(Button("Another project",Reset));buttons.Children.Add(Button("Disconnect project",()=>{disconnectConfirmation.IsVisible=true;return Task.CompletedTask;}));foot.Children.Add(buttons);
        disconnectConfirmation.Children.Add(Text("Remove this installation's hooks and local key? Other projects and remote keys will not change. Operation history is archived locally.",13));
        var confirmButtons=new StackPanel{Orientation=Orientation.Horizontal,Spacing=10};confirmButtons.Children.Add(Button("Cancel",()=>{disconnectConfirmation.IsVisible=false;return Task.CompletedTask;}));confirmButtons.Children.Add(Button("Confirm disconnect",Disconnect));disconnectConfirmation.Children.Add(confirmButtons);foot.Children.Add(disconnectConfirmation);
        if(!OperatingSystem.IsWindows())foot.Children.Add(Text("Closing this window locks the project sessions unlocked here.",12));
        page.Children.Add(foot);Content=new ScrollViewer{Content=page,HorizontalScrollBarVisibility=Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled};
        language.SelectionChanged+=(_,_)=>{t.Language=language.SelectedIndex==1?"es":"en";Translate();};
        root.TextChanged+=(_,_)=>{Invalidate();key.Text="";password.Text="";if(boundRoot is not null && root.Text!=boundRoot){profile=null;boundRoot=null;profileLabel.Text=t["No profile selected"];}};
        adapter.SelectionChanged+=(_,_)=>{Invalidate();key.Text="";password.Text="";};
        key.TextChanged+=(_,_)=>Invalidate();replace.IsCheckedChanged+=(_,_)=>Invalidate();migrate.IsCheckedChanged+=(_,_)=>Invalidate();
        Closing+=(_,e)=>{if(busy)e.Cancel=true;};Closed+=async(_,_)=>{foreach(var session in sessions.Values)await session.DisposeAsync();sessions.Clear();};
        Translate();Message("Choose a folder and import its connection profile to begin.");
    }
    private void Translate(){Title="DeleGuard · "+t["Project connection"];foreach(var action in translations)action();if(profile is null)profileLabel.Text=t["No profile selected"];if(lastError is not null)status.Text=t.Error(lastError);else if(lastMessage is not null)status.Text=t[lastMessage];
        foreach(var row in checks.Children.OfType<TextBlock>())if(row.Tag is ValueTuple<string,string> parts)row.Text=t[parts.Item1]+" · "+t[parts.Item2];
        AutomationProperties.SetName(root,t["Project folder"]);AutomationProperties.SetName(key,t["Agent API key"]);AutomationProperties.SetName(password,t["Vault password"]);AutomationProperties.SetName(adapter,t["Editor"]);AutomationProperties.SetName(language,t["Language"]);}
    private TextBlock Text(string value,double size=14,string color="#18334E")
    {var label=new TextBlock{FontSize=size,TextWrapping=TextWrapping.Wrap,Foreground=Brush.Parse(color),FontWeight=size>=20?FontWeight.SemiBold:FontWeight.Normal};translations.Add(()=>label.Text=t[value]);return label;}
    private static Border Card(Control child)=>new(){Background=Brushes.White,CornerRadius=new CornerRadius(16),Padding=new Thickness(24),Child=child,BorderBrush=Brush.Parse("#E0E7F1"),BorderThickness=new Thickness(1)};
    private Button Button(string label,Func<Task> action,bool primary=false)
    {
        var b=new Button{Padding=new Thickness(16,11),CornerRadius=new CornerRadius(7)};
        if(primary){b.Background=Brush.Parse("#1762F6");b.Foreground=Brushes.White;}
        translations.Add(()=>b.Content=t[label]);b.Click+=async(_,_)=>await Run(action);return b;
    }
    private async Task Run(Func<Task> action)
    {
        if(busy)return;busy=true;var priorMessage=lastMessage;var priorError=lastError;var priorColor=status.Foreground;Message("Working…");
        root.IsEnabled=key.IsEnabled=password.IsEnabled=adapter.IsEnabled=replace.IsEnabled=migrate.IsEnabled=language.IsEnabled=false;
        try
        {
            await action();
            if(lastMessage=="Working…")
            {
                lastMessage=priorMessage;lastError=priorError;status.Foreground=priorColor;
                status.Text=lastError is not null?t.Error(lastError):lastMessage is not null?t[lastMessage]:"";
            }
        }
        catch(Exception ex)
        {
            lastError=ex is SetupException e?e.Code:ex is DirectoryNotFoundException?"invalid_project":"connection_failed";
            status.Text=t.Error(lastError);status.Foreground=Brush.Parse("#B93838");
        }
        finally{busy=false;root.IsEnabled=key.IsEnabled=password.IsEnabled=adapter.IsEnabled=replace.IsEnabled=migrate.IsEnabled=language.IsEnabled=true;}
    }
    private void Message(string text){lastError=null;lastMessage=text;status.Text=t[text];status.Foreground=Brush.Parse("#25445C");}
    private void Invalidate(){plan=null;review.IsVisible=false;disconnectConfirmation.IsVisible=false;}
    private Dictionary<string,object?> Request(string operation)
    {
        if(profile is null)throw new SetupException("invalid_profile");
        if(string.IsNullOrWhiteSpace(root.Text)||!Directory.Exists(root.Text))throw new SetupException("invalid_project");
        return new(){["operation"]=operation,["profile"]=profile,["root"]=root.Text,["adapter"]=Adapter,["language"]=t.Language,["replace"]=replace.IsChecked==true,["migrate"]=migrate.IsChecked==true};
    }
    private string ConfigPath()=>ProjectFiles.SafePath(root.Text??"", ".deleguard/"+Adapter+"/config.json");
    private async Task Import()
    {
        var selected=await StorageProvider.OpenFilePickerAsync(new(){Title=t["Import profile"],AllowMultiple=false,FileTypeFilter=[new("DeleGuard profile"){Patterns=["*.deleguard.json","*.json"]}]});
        if(selected.Count==0)return;var path=selected[0].TryGetLocalPath()??throw new SetupException("invalid_profile");
        profile=SetupService.ReadProfile(path);adapter.SelectedIndex=Array.IndexOf(new[]{"cursor","vscode","claude"},profile.Value.GetProperty("adapter").GetString());
        ShowProfile();Invalidate();Message("Choose a folder and import its connection profile to begin.");
    }
    private void ShowProfile()
    {
        if(profile is not {} p)return;
        profileLabel.Text=p.GetProperty("server_url").GetString()+"\n"+p.GetProperty("organization_id").GetString()+" / "+p.GetProperty("application_id").GetString()+" · "+p.GetProperty("project_id").GetString();
    }
    private async Task ChooseFolder()
    {
        var selected=await StorageProvider.OpenFolderPickerAsync(new(){Title=t["Project folder"],AllowMultiple=false});
        if(selected.Count==0)return;root.Text=selected[0].TryGetLocalPath();LoadExisting();
    }
    private void LoadExisting()
    {
        if(!File.Exists(ConfigPath()))return;
        var current=ProjectFiles.ReadConnection(ConfigPath());profile=SetupService.ProfileFor(current);boundRoot=current.Root;ShowProfile();Message("Existing connection loaded. Check it, unlock it, or enter a replacement key and review the update.");
    }
    private async Task Preview()
    {
        if(profile is null && !string.IsNullOrWhiteSpace(root.Text) && Directory.Exists(root.Text))LoadExisting();
        plan=await service.Run(Request("preview"));boundRoot=Path.TrimEndingDirectorySeparator(Path.GetFullPath(root.Text!));
        files.Text=string.Join("\n",plan.Value.GetProperty("files").EnumerateArray().Select(x=>x.GetString()));review.IsVisible=true;
        Message(plan.Value.GetProperty("migration").GetBoolean()?"Migration copies this project's connection. The old external credential requires separate ownership review before removal.":"Only this folder will change. Existing operation history and unrelated editor settings are preserved.");
    }
    private async Task Install()
    {
        if(plan is not {} reviewed)throw new SetupException("invalid_profile");
        var connection=ProjectFiles.ParseConnection(reviewed.GetProperty("config"));var request=Request("install");var pass=password.Text;
        request["key"]=string.IsNullOrWhiteSpace(key.Text)?null:key.Text.Trim();
        request["vault"]=request["key"] is string secret?ProjectVault.Seal(secret,connection.CredentialId,pass):File.ReadAllText(connection.VaultPath);
        // Reuse still verifies the actual local credential against this server and scope.
        var check=ProjectVault.Open((string)request["vault"]!,connection.CredentialId,pass);
        request["key"] ??=System.Text.Encoding.UTF8.GetString(check);CryptographicOperations.ZeroMemory(check);
        var outcome=await service.Run(request);ProjectFiles.PrivateFile(connection.VaultPath);
        key.Text="";password.Text="";Invalidate();
        if(!OperatingSystem.IsWindows()){await DropSession(connection.CredentialId);sessions[connection.CredentialId]=await ProjectSessionProcess.Unlock(connection,pass);}
        Message(outcome.GetProperty("migration").GetBoolean()?"Connected with a project-local vault. Review the previous credential in your system store before removing it; shared credentials must be kept until their other connections are migrated.":"Connected. Next: restart your editor and test an approval from IDE Control.");
    }
    private async Task Check()
    {
        var report=await service.Run(new(){["operation"]="doctor",["config_path"]=ConfigPath()});checks.Children.Clear();
        foreach(var check in report.GetProperty("checks").EnumerateArray())
        {
            var name=check.GetProperty("id").GetString() switch{"installation"=>"Files and hooks","credential"=>"Local credential","project_connection"=>"Server connection","policy"=>"Policy test","runtime_coverage"=>"Hook coverage","mcp"=>"MCP connection",_=>"Hook timeout"};
            var state=check.GetProperty("status").GetString() switch{"ok"=>"OK","error"=>"Needs attention","partial"=>"Partial coverage","not_configured"=>"Not configured",_=>"Not verified"};
            checks.Children.Add(new TextBlock{Tag=(name,state),Text=t[name]+" · "+t[state],TextWrapping=TextWrapping.Wrap});
        }
        Message(report.GetProperty("status").GetString()=="configuration_incomplete"?"Connection unavailable. Check the server, project and key, then retry.":"Connection checked. Restart the editor and complete the approval test in IDE Control.");
    }
    private async Task Unlock(){var c=ProjectFiles.ReadConnection(ConfigPath());await DropSession(c.CredentialId);sessions[c.CredentialId]=await ProjectSessionProcess.Unlock(c,password.Text);password.Text="";Message("Unlocked for this session.");}
    private async Task DropSession(string id){if(sessions.Remove(id,out var session))await session.DisposeAsync();}
    private async Task Lock(){var c=ProjectFiles.ReadConnection(ConfigPath());await DropSession(c.CredentialId);Message("Project locked.");}
    private async Task Disconnect(){var c=ProjectFiles.ReadConnection(ConfigPath());await DropSession(c.CredentialId);await service.Run(new(){["operation"]="disconnect",["config_path"]=c.ConfigPath});Invalidate();checks.Children.Clear();Message("Project disconnected. Other installations are unchanged.");}
    private Task Reset(){profile=null;boundRoot=null;root.Text="";key.Text="";password.Text="";profileLabel.Text=t["No profile selected"];Invalidate();checks.Children.Clear();Message("Choose a folder and import its connection profile to begin.");return Task.CompletedTask;}
}
