# Memory Index

- [FluentValidation adapter patterns](fluentvalidation_adapter_patterns.md) — Custom() vs Must()+WithErrorCode(), IRuleBuilderOptionsConditions return type, PropertyPath not PropertyName
- [Verify cross-domain source before documenting it](feedback_verify_cross_domain_source.md) — read the real source of a type owned by another domain before describing its behavior in docs/design narrative
- [01.Core package/build conventions](project_01core_build_conventions.md) — MinVer versioning, NuGet metadata shape, local feed layout, global.json SDK-pin workaround
- [Reflection-absence proof technique](feedback_reflection_absence_proof.md) — PEReader/MetadataReader scan of the compiled DLL beats a source grep; exclude *Attribute TypeRefs (SDK noise), assert the exclusion actually fires
- [xunit Assert.Throws exactness gotchas](feedback_xunit_throws_exactness.md) — exact-type match trips on ArgumentException.ThrowIfNullOrWhiteSpace's null case; block-bodied lambda fixes a Func<Task> overload trap
- [IStringLocalizer has no WithCulture](project_istringlocalizer_no_withculture.md) — MS.Ext.Localization.Abstractions 10.0.11 needs a CurrentUICulture swap for per-call culture selection
