# Reference composition stays inside JsonSchemaAddReferences

Ordering References by Weight, grouping them under the Combinator and merging them into the schema without duplicates stay private to the `JsonSchemaAddReferences` task and are tested through its MSBuild interface. Extracting them into a separate module over plain (reference, weight) pairs was considered, but it only moves the logic rather than concentrating it: MSBuild is the only caller and the task's parameters are the interface users depend on. Revisit when a second caller appears (e.g. a CLI or source generator).
