# Domain Phase Implementer — Memory Index

- [WO-009 Design Phase Closure Pattern](project_wo009_design_closure.md) — arch-planner pre-writes CLAUDE.md; implementer independently re-verifies claims against shipped .cs files, marks state-map ●
- [SK.03 Published Phase Pattern](project_sk03_published_pattern.md) — version bump, dotnet pack, ZipFile manifest verify, state-map update, state-map-phase, sync-brain
- [Root state-map-phase bounce](project_root_state_map_phase_bounce.md) — root Domain Summary Board tracks latest-promoted phase key only; Current Phase legitimately moves backward (Published→Design) when a new WO batches tasks across all 6 phases at once
- [ValueObject construction order](project_valueobject_construction_order.md) — field-initializer capture is visible in base Validate(); ctor-body assignment is NOT (still default) — confirmed via real failing test
- [Generic nullable erasure gotcha](project_generic_nullable_erasure.md) — TKey? erases to plain TKey for value-type closures unless TKey has `struct` constraint; found in KeysetSpecification<T,TKey>
