# Umbraco.JsonSchema.Extensions

Extensions for Umbraco to add JSON schema references and update JSON properties using MSBuild tasks.

## JsonSchemaAddReferences

Adds references to a JSON schema file as an `allOf` array. Each reference may carry a `Weight` metadata value to control its order (ascending, default `0`). The references are merged using union semantics, so re-running the task does not create duplicates.

```xml
<Target Name="AddJsonSchemaReferences" BeforeTargets="Build">
  <ItemGroup>
    <_References Include="https://json.schemastore.org/appsettings.json" />
    <_References Include="appsettings-schema.Umbraco.Cms.json#" />
  </ItemGroup>
  <JsonSchemaAddReferences JsonSchemaFile="$(MSBuildProjectDirectory)\appsettings-schema.json" References="@(_References)" />
</Target>
```

| Parameter | Required | Description |
|-----------|----------|-------------|
| `JsonSchemaFile` | Yes | Path to the JSON schema file to create or update |
| `References` | Yes | The references to add as `$ref` entries (the `Weight` metadata orders them) |
| `TargetPath` | No | JSON path to the object the `allOf` is added to (default: the schema root) |

By default the `allOf` is added at the root, so every reference constrains the whole schema. Set `TargetPath` to compose references into a nested location instead — for example to only extend a single property while leaving the rest of the schema owned by a base reference:

```xml
<Target Name="AddPackageSchemaReferences" BeforeTargets="Build">
  <ItemGroup>
    <!-- Base schema owns the top-level shape -->
    <_BaseReference Include="umbraco-package-schema.Umbraco.Cms.json#" />
    <!-- Package fragments only extend the extensions array items -->
    <_ExtensionReference Include="acme.umbraco-package-schema.json#/properties/extensions/items" />
  </ItemGroup>
  <JsonSchemaAddReferences JsonSchemaFile="$(MSBuildProjectDirectory)\umbraco-package-schema.json" References="@(_BaseReference)" />
  <JsonSchemaAddReferences JsonSchemaFile="$(MSBuildProjectDirectory)\umbraco-package-schema.json" References="@(_ExtensionReference)" TargetPath="$.properties.extensions.items" />
</Target>
```

`TargetPath` supports object property segments using dot or bracket notation, with an optional leading `$` (e.g. `$.properties.extensions.items` or `properties['extensions'].items`). Intermediate objects are created when they do not exist; array indices are not supported.

## JsonPathUpdateValue

Updates the value of a property in a JSON file using a JSON path expression.

```xml
<Target Name="UpdatePackageManifestVersion" DependsOnTargets="Build" AfterTargets="GetBuildVersion;GetUmbracoBuildVersion">
  <ItemGroup>
    <_PackageManifestFiles Include="**\package.manifest" />
  </ItemGroup>
  <JsonPathUpdateValue JsonFile="%(_PackageManifestFiles.FullPath)" Path="$.version" Value="&quot;$(PackageVersion)&quot;" />
</Target>
```

## JsonSchemaGenerate

Generates a JSON schema from a C# type in an assembly. XML documentation comments are included as `description` fields in the generated schema, providing IntelliSense tooltips in editors.

> **Note:** This task requires .NET Core MSBuild (i.e. `dotnet build`) or Visual Studio 2026+ (MSBuild 18.0+), which supports running .NET Core tasks via the TaskHost. It is not available when building with Visual Studio 2022 or earlier.

> **Note:** When executing this task from a class library (`Microsoft.NET.Sdk` with `<OutputType>Library</OutputType>`, the default) and the type being generated depends on assemblies from referenced NuGet packages (e.g. `Umbraco.Core`), set `<CopyLocalLockFileAssemblies>true</CopyLocalLockFileAssemblies>` in the project file. This ensures the dependency DLLs are copied next to the target assembly so they can be resolved at build time. This is already the default for executable projects, including the Web (`Microsoft.NET.Sdk.Web`), Worker (`Microsoft.NET.Sdk.Worker`), and Blazor WebAssembly (`Microsoft.NET.Sdk.BlazorWebAssembly`) SDKs.

| Parameter | Required | Description |
|-----------|----------|-------------|
| `AssemblyPath` | Yes | Path to the assembly file containing the type |
| `TypeName` | Yes | Fully qualified type name to generate the schema for |
| `OutputPath` | Yes | Output file path for the generated JSON schema |
| `IncludeObsoleteProperties` | No | Whether to include properties marked with `[Obsolete]` (default: `false`) |

```xml
<!-- Add JSON schema file to package output and remove on clean -->
<PropertyGroup>
  <_JsonSchemaFile>appsettings-schema.MyPackage.json</_JsonSchemaFile>
</PropertyGroup>
<ItemGroup>
  <Content Include="$(_JsonSchemaFile)" PackagePath="" Visible="false" />
  <Clean Include="$(_JsonSchemaFile)" />
</ItemGroup>

<!-- Generate JSON schema on build (regenerated when assembly is newer) -->
<Target Name="GenerateAppsettingsSchema" AfterTargets="Build" Inputs="$(TargetPath)" Outputs="$(_JsonSchemaFile)">
  <Message Text="Generating $(_JsonSchemaFile)" Importance="high" />
  <JsonSchemaGenerate AssemblyPath="$(TargetPath)" TypeName="MyPackage.MyPackageSchema" OutputPath="$(MSBuildThisFileDirectory)$(_JsonSchemaFile)" />
  <ItemGroup>
    <FileWrites Include="$(_JsonSchemaFile)" />
  </ItemGroup>
</Target>
```
