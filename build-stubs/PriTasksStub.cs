using Microsoft.Build.Framework;

namespace Microsoft.Build.Packaging.Pri.Tasks;

public abstract class PriTaskBase : Microsoft.Build.Utilities.Task
{
    public ITaskItem[] Inputs { get; set; } = [];
    public string MakePriExeFullPath { get; set; } = "";
    public string MakePriExtensionPath { get; set; } = "";
    public string IntermediateDirectory { get; set; } = "";
    public string AdditionalMakepriExeParameters { get; set; } = "";
    public string ExcludeXamlFromLibraryLayoutsWhenXbfIsPresent { get; set; } = "";
    public string VsTelemetrySession { get; set; } = "";
    public string ProjectName { get; set; } = "";
    public string Platform { get; set; } = "";
    public string PriConfigXmlPath { get; set; } = "";
    public string OutputPath { get; set; } = "";
    public string DefaultResourceLanguage { get; set; } = "";
    public string UnfilteredLayoutResfilesPath { get; set; } = "";
    public string FilteredLayoutResfilesPath { get; set; } = "";
    public string ExcludedLayoutResfilesPath { get; set; } = "";
    public string ResourcesResfilesPath { get; set; } = "";
    public string PriResfilesPath { get; set; } = "";
    public string EmbedFileResfilePath { get; set; } = "";
    public string IntermediateExtension { get; set; } = "";
    public string PriInitialPath { get; set; } = "";
    public string DefaultResourceQualifiers { get; set; } = "";
    public string PriConfigXmlDefaultSnippetPath { get; set; } = "";
    public string TargetPlatformIdentifier { get; set; } = "";
    public string TargetPlatformVersion { get; set; } = "";
    public string TargetPlatformResourceVersion { get; set; } = "";
    public string LayoutResfilesPath { get; set; } = "";
    public string ProjectDirectory { get; set; } = "";
    public string MakePriExePath { get; set; } = "";
    public string PriOutputPath { get; set; } = "";
    public string SourcePriFile { get; set; } = "";
    public string OutputPriFile { get; set; } = "";
    public string MainPackageFileMapPath { get; set; } = "";
    public string PackageFileMapPath { get; set; } = "";
    public string QualifiersPath { get; set; } = "";
    public string AppxBundleSplittingPriPath { get; set; } = "";
    public string AppxBundlePlatformSpecificPriPath { get; set; } = "";
    public string IndexFilesForQualifiersCollection { get; set; } = "";
    public string ProjectPriIndexName { get; set; } = "";
    public string InsertReverseMap { get; set; } = "";
    public string OutputFileName { get; set; } = "";
    public string AppxBundleAutoResourcePackageQualifiers { get; set; } = "";
    public string MultipleQualifiersPerDimensionFoundPath { get; set; } = "";
    public ITaskItem[] LayoutFiles { get; set; } = [];
    public ITaskItem[] PRIResourceFiles { get; set; } = [];
    public ITaskItem[] PriFiles { get; set; } = [];
    public ITaskItem[] EmbedFiles { get; set; } = [];
    public ITaskItem[] UnprocessedResourceFiles_OtherLanguages { get; set; } = [];
    public ITaskItem[] AdditionalResourceResFilesInput { get; set; } = [];
    public ITaskItem[] SourceAppxManifest { get; set; } = [];
    public ITaskItem[] ProjectArchitecture { get; set; } = [];
    public ITaskItem[] RecursiveProjectArchitecture { get; set; } = [];
    public ITaskItem[] PRIResource { get; set; } = [];

    [Output]
    public ITaskItem[] Expanded { get; set; } = [];
    [Output]
    public ITaskItem[] OutputResources { get; set; } = [];
    [Output]
    public ITaskItem[] ModifiedPriFiles { get; set; } = [];
    [Output]
    public ITaskItem[] IntermediateFileWrites { get; set; } = [];
    [Output]
    public ITaskItem[] Filtered { get; set; } = [];
    [Output]
    public ITaskItem[] AdditionalResourceResFiles { get; set; } = [];
    [Output]
    public string? PackageArchitecture { get; set; }
    [Output]
    public ITaskItem? PriConfigXml { get; set; }
}

public class ExpandPriContent : PriTaskBase { /// <summary>
/// Completes the task successfully.
/// </summary>
/// <returns><c>true</c>.</returns>
public override bool Execute() => true; }
public class CreatePriConfigXmlForSplitting : PriTaskBase { /// <summary>
/// Completes the task successfully.
/// </summary>
/// <returns><c>true</c>.</returns>
public override bool Execute() => true; }
public class CreatePriConfigXmlForMainPackageFileMap : PriTaskBase { /// <summary>
/// Completes the task successfully.
/// </summary>
/// <returns><c>true</c>.</returns>
public override bool Execute() => true; }
public class CreatePriConfigXmlForFullIndex : PriTaskBase { /// <summary>
/// Completes the task successfully.
/// </summary>
/// <returns><c>true</c>.</returns>
public override bool Execute() => true; }
public class CreatePriFilesForPortableLibraries : PriTaskBase { /// <summary>
/// Completes the task successfully.
/// </summary>
/// <returns><c>true</c>.</returns>
public override bool Execute() => true; }
public class GenerateMainPriConfigurationFile : PriTaskBase { /// <summary>
/// Completes the task successfully.
/// </summary>
/// <returns><c>true</c>.</returns>
public override bool Execute() => true; }
public class GeneratePriConfigurationFiles : PriTaskBase { /// <summary>
/// Completes the task successfully.
/// </summary>
/// <returns><c>true</c>.</returns>
public override bool Execute() => true; }
public class GenerateProjectPriFile : PriTaskBase { /// <summary>
/// Completes the task successfully.
/// </summary>
/// <returns><c>true</c>.</returns>
public override bool Execute() => true; }
public class RemoveDuplicatePriFiles : PriTaskBase { /// <summary>
/// Completes the task successfully.
/// </summary>
/// <returns><c>true</c>.</returns>
public override bool Execute() => true; }
public class UpdateMainPackageFileMap : PriTaskBase { /// <summary>
/// Completes the task successfully.
/// </summary>
/// <returns><c>true</c>.</returns>
public override bool Execute() => true; }
public class MergePriFiles : PriTaskBase { /// <summary>
/// Completes the task successfully.
/// </summary>
/// <returns><c>true</c>.</returns>
public override bool Execute() => true; }
