# Umbraco JSON schema extensions

MSBuild tasks that compose, update and generate the JSON (schema) files Umbraco and its packages ship, such as `appsettings-schema.json` and `umbraco-package-schema.json`.

## Language

**JSON schema file**:
The schema file a build creates or extends by adding References to it.
_Avoid_: schema document, schema output

**Reference**:
A `$ref` to another JSON schema (a URL or relative file, optionally with a fragment) added to a JSON schema file.
_Avoid_: link, include

**Weight**:
The order of a Reference among the References added together; lower comes first, absent means zero.
_Avoid_: order, priority, sort

**Combinator**:
The JSON Schema keyword References are grouped under: `allOf` (every Reference must match), `anyOf` or `oneOf`.
_Avoid_: mode, operator

**JSON path**:
An address of a value inside a JSON file: an optional `$`, then property names (dot or quoted bracket notation) and array indices (unquoted bracket notation). A quoted segment is always a property name, even when it looks like a number.
_Avoid_: selector, pointer (a JSON Pointer is a different syntax)

**Target path**:
The JSON path of the object References are added to, `$` (the schema root) by default.
_Avoid_: insertion point, location
