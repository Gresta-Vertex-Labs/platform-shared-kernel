; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
SK0037 | Design | Warning | ValueObjectMissingEnsureValid
SK0038 | Design | Warning | IntegrationEventMissingAttribute
SK0039 | Design | Warning | InvalidIntegrationEventAttribute
SK0040 | Design | Warning | PipelineMarkerResponseShapeMismatch
SK0041 | Design | Warning | DuplicateCacheableQueryName
SK0042 | Security | Warning | NonConstantDapperSqlArgument

### Removed Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
SK0015 | Usage | Warning | StreamPipelineBehaviorMisregistration — no longer applies: streams have their own kernel IStreamPipelineBehavior contract and no mediator registration (WO-086/P-567)
SK0019 | Design | Warning | RetryableRequestWithoutIdempotency — target type (IRetryableRequest) removed from SharedKernel.Application.Behaviors (P-544)
