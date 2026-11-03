namespace TerminalLink;
internal static class Program {
 [STAThread] static void Main(string[] args) {
  if(args.Length==1&&args[0]=="--register-ssh-read"){try{RemoteCodexFeed.Register();}catch{Console.Error.WriteLine("SSH registration failed.");Environment.ExitCode=1;}return;}
  if(args.Length==3&&args[0]=="--publish-files"){SharedFiles.PublishCommand(args[1],args[2]);return;}
  if(args.Length==5&&args[0]=="--new-tab"){NewTerminal.Agent(int.Parse(args[1]),long.Parse(args[2]),long.Parse(args[3]),int.Parse(args[4]));return;}
  if(args.Length==5&&args[0]=="--close-tab"){CloseTerminal.Agent(int.Parse(args[1]),long.Parse(args[2]),long.Parse(args[3]),int.Parse(args[4]));return;}
  if(args.Length==1&&args[0]=="--console-titles"){ConsoleNative.TitleAgent();return;}
  if(args.Length==3&&args[0]=="--console-agent"){ConsoleNative.Agent(int.Parse(args[1]),long.Parse(args[2]));return;}

#if TESTING
  if(args.Length==2&&args[0]=="--delivery-read-probe"){ReadOnlyProbe.Delivery(args[1]).GetAwaiter().GetResult();return;}
  if(args.Length==2&&args[0]=="--hub-read-probe"){ReadOnlyProbe.Hub(args[1]);return;}
  if(args.Length==3&&args[0]=="--read-only-probe"){ReadOnlyProbe.Run(args[1],int.Parse(args[2]));return;}
  if(args.Length==1&&args[0]=="--public-checks"){try{PublicChecks.Run();}catch(Exception e){File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"public-checks.txt"),e.ToString());Environment.ExitCode=1;}return;}
  if(args.Length==1&&args[0]=="--self-test"){SelfTest.Run().GetAwaiter().GetResult();return;}
#endif
  ApplicationConfiguration.Initialize();

Application.Run(new MainForm(args));
 }
}
