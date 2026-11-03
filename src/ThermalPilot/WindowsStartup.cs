using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Xml.Linq;

namespace ThermalPilot;

internal static class WindowsStartup
{
    private static string UserSid => WindowsIdentity.GetCurrent().User?.Value
        ?? throw new InvalidOperationException("Identité Windows introuvable.");
    public static string TaskName => "ThermalPilot-" + UserSid;

    public static object Inspect()
    {
        try
        {
            var task = ReadRegistration();
            return new { Name = TaskName, Enabled = task?.Enabled ?? false, Executable = task?.Executable, Error = (string?)null };
        }
        catch (Exception ex) { return new { Name = TaskName, Enabled = (bool?)null, Executable = (string?)null, Error = ex.Message }; }
    }

    public static bool IsEnabled() => ReadRegistration()?.Enabled ?? false;
    public static bool Exists() => ReadRegistration() is not null;
    public static bool TargetsCurrentInstallation()
    {
        var task = ReadRegistration();
        return task is not null && string.Equals(Path.GetFullPath(Environment.ExpandEnvironmentVariables(task.Executable)),
            Environment.ProcessPath, StringComparison.OrdinalIgnoreCase);
    }

    private sealed record Registration(bool Enabled, string Executable);
    private static Registration? ReadRegistration() => WithFolder<Registration?>(folderObject =>
    {
        dynamic folder = folderObject;
        object? task = null;
        try
        {
            task = folder.GetTask(TaskName);
            XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
            string xml = (string)((dynamic)task).Xml;
            string executable = XDocument.Parse(xml).Descendants(ns + "Exec").Single().Element(ns + "Command")!.Value;
            return new Registration((bool)((dynamic)task).Enabled, executable);
        }
        catch (Exception ex) when (IsMissing(ex)) { return null; }
        finally { Release(task); }
    });

    public static void SetEnabled(bool enabled)
    {
        if (!Recovery.IsAdmin) throw new UnauthorizedAccessException("Relance en administrateur pour modifier le démarrage Windows.");
        WithFolder(folderObject =>
        {
            dynamic folder = folderObject;
            if (enabled)
            {
                string executable = Environment.ProcessPath ?? throw new IOException("Exécutable introuvable.");
                string xml = StartupTaskDefinition.Create(executable, UserSid);
                object task = folder.RegisterTask(TaskName, xml, 6, UserSid, null, 3, null);
                Release(task);
            }
            else
            {
                try { folder.DeleteTask(TaskName, 0); }
                catch (Exception ex) when (IsMissing(ex)) { }
            }
            return true;
        });
    }

    private static T WithFolder<T>(Func<object, T> action)
    {
        object? serviceObject = null, folderObject = null;
        try
        {
            var type = Type.GetTypeFromProgID("Schedule.Service")
                ?? throw new NotSupportedException("Le Planificateur de tâches Windows est indisponible.");
            serviceObject = Activator.CreateInstance(type)
                ?? throw new NotSupportedException("Le Planificateur de tâches Windows ne répond pas.");
            dynamic service = serviceObject;
            service.Connect();
            folderObject = service.GetFolder(@"\");
            return action(folderObject);
        }
        finally { Release(folderObject); Release(serviceObject); }
    }

    // COM interop maps ERROR_FILE_NOT_FOUND to FileNotFoundException on some .NET versions.
    private static bool IsMissing(Exception ex) => ex.HResult == unchecked((int)0x80070002);
    private static void Release(object? instance)
    {
        if (instance is not null && Marshal.IsComObject(instance)) Marshal.FinalReleaseComObject(instance);
    }
}
