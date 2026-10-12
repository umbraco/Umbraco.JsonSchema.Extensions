using System.Collections;
using System.Collections.Generic;
using Microsoft.Build.Framework;

namespace Umbraco.JsonSchema.Extensions.UnitTests;

/// <summary>
/// Lightweight stub for <see cref="IBuildEngine"/> that captures logged errors.
/// </summary>
internal sealed class FakeBuildEngine : IBuildEngine
{
    public List<string?> Errors { get; } = new();

    public bool ContinueOnError => false;

    public int LineNumberOfTaskNode => 0;

    public int ColumnNumberOfTaskNode => 0;

    public string ProjectFileOfTaskNode => string.Empty;

    public bool BuildProjectFile(string projectFileName, string[] targetNames, IDictionary globalProperties, IDictionary targetOutputs) => true;

    public void LogCustomEvent(CustomBuildEventArgs e) { }

    public void LogErrorEvent(BuildErrorEventArgs e) => Errors.Add(e.Message);

    public void LogMessageEvent(BuildMessageEventArgs e) { }

    public void LogWarningEvent(BuildWarningEventArgs e) { }
}
