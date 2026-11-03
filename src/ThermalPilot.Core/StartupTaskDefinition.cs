using System.Xml.Linq;

namespace ThermalPilot.Core;

public static class StartupTaskDefinition
{
    public static string Create(string executable, string userSid)
    {
        if (!Path.IsPathFullyQualified(executable)) throw new ArgumentException("Un chemin absolu est nécessaire.", nameof(executable));
        ArgumentException.ThrowIfNullOrWhiteSpace(userSid);
        XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
        XElement Element(string name, object value) => new(ns + name, value);
        var task = new XElement(ns + "Task", new XAttribute("version", "1.2"),
            Element("RegistrationInfo", new[] {
                Element("Author", "Thermal Pilot"),
                Element("Description", "Démarre Thermal Pilot et sa régulation à l’ouverture de session de cet utilisateur.") }),
            Element("Triggers", Element("LogonTrigger", new[] {
                Element("Enabled", true), Element("UserId", userSid), Element("Delay", "PT10S") })),
            Element("Principals", new XElement(ns + "Principal", new XAttribute("id", "ThermalPilotUser"),
                Element("UserId", userSid), Element("LogonType", "InteractiveToken"), Element("RunLevel", "HighestAvailable"))),
            Element("Settings", new[] {
                Element("MultipleInstancesPolicy", "IgnoreNew"),
                Element("DisallowStartIfOnBatteries", false), Element("StopIfGoingOnBatteries", false),
                Element("AllowHardTerminate", false), Element("StartWhenAvailable", true),
                Element("RunOnlyIfNetworkAvailable", false), Element("AllowStartOnDemand", true),
                Element("Enabled", true), Element("Hidden", false), Element("RunOnlyIfIdle", false),
                Element("WakeToRun", false), Element("ExecutionTimeLimit", "PT0S"), Element("Priority", 7) }),
            new XElement(ns + "Actions", new XAttribute("Context", "ThermalPilotUser"),
                Element("Exec", new[] { Element("Command", executable), Element("Arguments", "--startup"),
                    Element("WorkingDirectory", Path.GetDirectoryName(executable)!) })));
        return new XDocument(task).ToString();
    }
}
