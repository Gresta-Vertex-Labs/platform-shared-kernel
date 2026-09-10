; Shipped analyzer releases
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

## Release 1.0

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
SK0001 | Usage | Warning | DirectDateTimeUsage
SK0002 | Usage | Warning | DirectMicrosoftFeatureManagerUsage
SK0003 | Design | Warning | RawExceptionThrow
SK0004 | Design | Warning | NullErrorReturn
SK0005 | Design | Warning | StringOnlyExceptionConstructor
SK0006 | Design | Warning | GuardClauseThrow
SK0007 | Design | Warning | RedisChannelServiceMessagingSubstitute
SK0008 | Design | Warning | AggregateRootDispatchCoupling
SK0009 | Design | Warning | DomainEventMissingVersionAttribute
SK0010 | Design | Warning | SpecificationOrderingConflict
SK0011 | Design | Warning | GuidFormatCodeMisuse
SK0013 | Usage | Warning | RawHttpClientConstructorInjection
SK0014 | Usage | Warning | ClosedGenericResiliencePipelineRegistration
SK0015 | Usage | Warning | StreamPipelineBehaviorMisregistration
SK0016 | Design | Warning | RequestTypeShortNameUsage
SK0017 | Design | Warning | CommandImplementsCacheableQuery
SK0018 | Design | Warning | QueryImplementsInvalidatesCache
SK0019 | Design | Warning | RetryableRequestWithoutIdempotency
SK0020 | Design | Warning | DirectILoggerExtensionMethodUsage
SK0021 | Design | Warning | HandWrittenLoggerMessageDefineDelegate
SK0022 | Usage | Warning | CrossCuttingMagicStringLiteral
SK0023 | Usage | Warning | NonSingletonAmazonS3ClientRegistration
SK0024 | Usage | Warning | RawSearchFieldNameLiteral
SK0025 | Usage | Warning | ObsoleteElasticsearchClientUsage
SK0026 | Usage | Warning | RawIntelligenceProviderClientConstructorInjection
SK0027 | Usage | Warning | RawIntelligenceIdentifierLiteral
SK0028 | Design | Warning | NonDeterministicApiUsageInsideWorkflow
SK0029 | Usage | Warning | RawTemporalClientConstructorInjection
SK0030 | Usage | Warning | ResultOutcomeDiscarded
SK0031 | Usage | Warning | RawSecurityContextConstructorInjection
SK0032 | Security | Warning | CorsWildcardOriginWithCredentials
SK0033 | Usage | Warning | ReflectionBasedObjectMapperUsage
SK0034 | Advisory | Warning | AmountCurrencyPairCoupling
SK0035 | Security | Warning | UnmaskedClassifiedDataAtLoggingCallSite
SK0036 | Usage | Warning | RawRpcExceptionConstruction
SK0201 | Design | Warning | TenantedDbContextOnModelCreatingGuard
SK0202 | Design | Warning | IgnoreQueryFiltersOutsideTenantedRepository
SK0703 | Usage | Warning | MessageBusSingletonRegistration
SK0704 | Usage | Warning | HardcodedQueueUriInGetSendEndpoint
SK0705 | Usage | Warning | FaultConsumerDirectRegistration
SK0708 | Usage | Warning | BatchConsumerRegisteredViaAddConsumer
