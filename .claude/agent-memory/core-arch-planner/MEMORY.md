# Memory Index

- [P-001/P-002 Phase Design Decisions](project_phase_p001_p002.md) — locked type shapes for Result\<T\>, ValidationResult, ErrorCodes, SmartEnum, async railway rules
- [P-003 Phase Design Decisions](project_phase_p003.md) — locked design for SharedKernel.Guards: IGuardClause marker, two-path API, cached Regex, EqualityComparer\<T\>.Default, Guard.Throw wraps Against
- [P-042 Phase Design Decisions](project_phase_p042.md) — ErrorType.BusinessRule (HTTP 422, domain invariant), Error.BusinessRule factory, ErrorCodes.Domain.RuleViolated constant; fixes 03.Domain misclassification
- [WO-033 Cryptography Phase Sequence](project_phase_wo033.md) — P-205/206 locked full contract; P-207/208/209 are execution-only, no new design — cross-reference task IDs, don't duplicate
- [WO-034 IOneWayHasher Rename](project_phase_wo034.md) — locked exact renamed names/files for P-211/212/213: IPasswordHasher→IOneWayHasher, Pbkdf2PasswordHasher→Pbkdf2OneWayHasher, PasswordVerificationResult→HashVerificationResult, version bump to 2.0.0
- [P-230 IHasSuccessFlag + IResultOfT\<T\> Application Seams](project_phase_p230.md) — locked contract: IHasSuccessFlag (zero-member, Result\<T\>+Result), IResultOfT\<T\> (Result\<T\> only, IsSuccess/IsFailure/Value); 8 tasks D-26/D-27/C-39/C-40/T-29/T-30/DO-12/DO-13; unblocks WO-038 05.Application AOT cleanup
