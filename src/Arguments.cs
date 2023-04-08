using CommandLine;

namespace NugetUpdater
{
    public class Arguments
    {
        [Option('s', "storage", Required = true, HelpText = "Path to nuget packages storage")]
        public string StoragePath { get; set; }

        [Option("prerelease", Required = false, HelpText = "Include prerelease?")]
        public bool IsIncludePrerelease { get; set; }
    }
}