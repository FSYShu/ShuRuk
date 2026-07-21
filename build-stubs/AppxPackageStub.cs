using Microsoft.Build.Framework;

namespace Microsoft.Build.AppxPackage;

public abstract class AppxPackageTaskBase : Microsoft.Build.Utilities.Task
{
    public ITaskItem[] Inputs { get; set; } = [];
    public string ProjectName { get; set; } = "";
    public string Platform { get; set; } = "";
    public string Configuration { get; set; } = "";
    public string VsTelemetrySession { get; set; } = "";
    public ITaskItem[] TargetDirsToExclude { get; set; } = [];
    public ITaskItem[] TargetFilesToExclude { get; set; } = [];
    public ITaskItem[] Payload { get; set; } = [];
    public string DefaultLanguage { get; set; } = "";
    public ITaskItem[] SourceAppxManifest { get; set; } = [];
    public string MakePriExeFullPath { get; set; } = "";
    public string IntermediateOutputPath { get; set; } = "";

    [Output]
    public ITaskItem[] Expanded { get; set; } = [];
    [Output]
    public ITaskItem[] Filtered { get; set; } = [];
    [Output]
    public ITaskItem[] FilteredPayload { get; set; } = [];
    [Output]
    public ITaskItem[] PayloadWithoutDuplicates { get; set; } = [];
    [Output]
    public string? DefaultResourceLanguage { get; set; }
    [Output]
    public string? Architecture { get; set; }
    [Output]
    public string? FullPath { get; set; }
    [Output]
    public string? PropertyValue { get; set; }
}

public class ExpandPayloadDirectories : AppxPackageTaskBase { public override bool Execute() => true; }
public class GetDefaultResourceLanguage : AppxPackageTaskBase { public override bool Execute() => true; }
public class GetPackageArchitecture : AppxPackageTaskBase { public override bool Execute() => true; }
public class GetSdkFileFullPath : AppxPackageTaskBase { public override bool Execute() => true; }
public class GetSdkPropertyValue : AppxPackageTaskBase { public override bool Execute() => true; }
public class RemovePayloadDuplicates : AppxPackageTaskBase { public override bool Execute() => true; }
public class RemoveRedundantXamlFilesFromSdkPayload : AppxPackageTaskBase { public override bool Execute() => true; }
public class ValidateConfiguration : AppxPackageTaskBase { public override bool Execute() => true; }
