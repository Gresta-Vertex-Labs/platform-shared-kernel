---
name: feedback_record_list_equality_pitfall
description: A sealed record's compiler-generated Equals does NOT deep-compare an IReadOnlyList<T>/List<T> field — two structurally-identical instances built from separate List<T> objects are NOT Equals-equal
metadata:
  type: feedback
---

C# record-generated `Equals`/`GetHashCode` compare each field with
`EqualityComparer<TField>.Default`. For a field typed `IReadOnlyList<T>` (or any collection
interface) whose runtime backing instance is a plain `List<T>`, `List<T>` has no `Equals`
override — `EqualityComparer<T>.Default` falls back to reference equality for it. Two
records built from two DIFFERENT `List<T>` instances containing IDENTICAL elements are
therefore NOT `Equals`-equal via the record's own generated comparison, even though every
other field matches and the record "looks" value-equal.

**Why this matters:** hit while writing a parity test in `16.Testing` (WO-055/P-355,
`InMemorySearchIndexTests.cs`) comparing two `SearchBulkReceipt` instances (a `sealed record`
with an `IReadOnlyList<SearchItemFailure> Failures` field) produced by two separate calls.
`Assert.Equal(receiptA, receiptB)` on the whole record would silently fail to prove what the
test intended, or pass/fail unpredictably depending on whether `Failures` happened to be the
same list-empty singleton vs. two separately-allocated lists.

**How to apply:** when a test needs to assert two records are "the same" and one of the
record's fields is a collection type, compare that field explicitly with `.SequenceEqual(...)`
(works correctly here BECAUSE the collection's *element* type, e.g. `SearchItemFailure`, is
itself a record with real generated value-equality) and compare the remaining scalar/nested-
record fields either individually or via the whole-record `Equals` if none of those remaining
fields are themselves collections. Never assume `Assert.Equal(recordA, recordB)` proves
collection-field equality just because the type is `sealed record`.
