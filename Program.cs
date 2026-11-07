using TheVanillafiyator;

using var ui = ConsoleUi.Create();
return CommandLine.Run(args, ui, Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
