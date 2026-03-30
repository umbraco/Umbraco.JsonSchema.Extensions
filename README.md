# Umbraco.JsonSchema.Extensions

Extensions for Umbraco to add JSON schema references and update JSON properties using MSBuild tasks.

## JsonSchemaAddReferences

Adds references to a JSON schema file.

```xml
<Target Name="AddJsonSchemaReferences" BeforeTargets="Build">
  <ItemGroup>
    <_References Include="https://json.schemastore.org/appsettings.json" />
    <_References Include="appsettings-schema.Umbraco.Cms.json#" />
  </ItemGroup>
  <JsonSchemaAddReferences JsonSchemaFile="$(MSBuildProjectDirectory)\appsettings-schema.json" References="@(_References)" />
</Target>
```

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

> **Note:** This task requires .NET Core MSBuild (i.e. `dotnet build`). It is not available when building with .NET Framework MSBuild.

| Parameter | Required | Description |
|-----------|----------|-------------|
| `AssemblyPath` | Yes | Path to the assembly file containing the type |
| `TypeName` | Yes | Fully qualified type name to generate the schema for |
| `OutputPath` | Yes | Output file path for the generated JSON schema |
| `IncludeObsoleteProperties` | No | Whether to include properties marked with `[Obsolete]` (default: `false`) |

```xml
<!-- Add JSON schema file to package output -->
<PropertyGroup>
  <_JsonSchemaFile>appsettings-schema.MyPackage.json</_JsonSchemaFile>
</PropertyGroup>
<ItemGroup>
  <Content Include="$(_JsonSchemaFile)" PackagePath="" Visible="false" />
</ItemGroup>

<!-- Generate JSON schema on build (skipped when file already exists) -->
<Target Name="GenerateAppsettingsSchema" AfterTargets="Build" Condition="!Exists('$(_JsonSchemaFile)')">
  <Message Text="Generating $(_JsonSchemaFile) because it doesn't exist" Importance="high" />
  <JsonSchemaGenerate AssemblyPath="$(TargetPath)" TypeName="MyPackage.MyPackageSchema" OutputPath="$(MSBuildThisFileDirectory)$(_JsonSchemaFile)" />
</Target>

<!-- Remove generated JSON schema on clean -->
<Target Name="RemoveAppsettingsSchema" AfterTargets="Clean" Condition="Exists('$(_JsonSchemaFile)')">
  <Delete Files="$(_JsonSchemaFile)" />
</Target>
```
