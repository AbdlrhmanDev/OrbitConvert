namespace OrbitConvert.App;

/// <summary>
/// Command-line contract used by the shell integration:
///   OrbitConvert.exe                         open the main window
///   OrbitConvert.exe --radial file1 file2    show the radial menu (context menu, Send To)
///   OrbitConvert.exe --open file1 file2      open the Convert page with these files
///   OrbitConvert.exe --background            start in the notification area only
///   OrbitConvert.exe --register-shell / --unregister-shell   (portable installs)
/// Plain file arguments (e.g. dropping files on the .exe) behave like --open.
/// </summary>
public sealed record StartupArguments(string Command, IReadOnlyList<string> Files)
{
    public const string Show = "show";
    public const string Radial = "radial";
    public const string Open = "open";
    public const string Background = "background";
    public const string RegisterShell = "register-shell";
    public const string UnregisterShell = "unregister-shell";

    public static StartupArguments Parse(IReadOnlyList<string> args)
    {
        var command = Show;
        var files = new List<string>();
        foreach (var arg in args)
        {
            switch (arg.ToLowerInvariant())
            {
                case "--radial": command = Radial; break;
                case "--open": command = Open; break;
                case "--background": command = Background; break;
                case "--register-shell": command = RegisterShell; break;
                case "--unregister-shell": command = UnregisterShell; break;
                default:
                    if (!arg.StartsWith("--", StringComparison.Ordinal))
                    {
                        files.Add(arg);
                    }
                    break;
            }
        }
        if (command == Show && files.Count > 0)
        {
            command = Open;
        }
        return new StartupArguments(command, files);
    }
}
