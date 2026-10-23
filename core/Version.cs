using System.Reflection;

namespace Core
{
    /// <summary>Version of the core library, read from the assembly metadata set in Directory.Build.props.</summary>
    public static class Version
    {
        public static string Get()
        {
            var assembly = typeof(Version).Assembly;
            var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
            return informational?.InformationalVersion ?? assembly.GetName().Version?.ToString() ?? "0.0.0";
        }
    }
}
