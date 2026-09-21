# AUDITv3 — audit ledger byte format

This document specifies, byte for byte, what `SharedKernel.Persistence.EfCore.Auditing` authenticates, so
that a ledger can be verified independently of this package (by an auditor's own tool, in another
language, years later). The test vectors at the end are checked by the package's test suite against this
very file; if the code and this document ever disagree, the build fails.

## 1. Model

* A **record** (`audit_records`) is written by the request path with a plain `INSERT`. It is not chained yet.
* A background **sealer** later appends a **link** (`audit_chain_links`) for each record: the record's
  position (`sequence`) in its **chain**, the MAC of the previous link in that chain (`previous_mac`) and its
  own MAC (`mac`).
* A chain is identified by `(tenant_id, resource_type)`; `tenant_id` is absent for the system chain.
  Sequences start at 1 and are contiguous.
* The before/after snapshots (the **payload**) are stored with a random 32-byte **salt** in
  `audit_record_payloads`. The record stores only `payload_hash = SHA-256(salt ‖ payload)`, so the payload
  row can be erased (GDPR/KVKK) while every MAC stays verifiable.
* A **checkpoint** is an asymmetric signature over a chain head, stored outside the chain.

## 2. Primitive encodings

| Primitive | Encoding |
|---|---|
| `int32` | 4 bytes, big-endian, two's complement |
| `int64` | 8 bytes, big-endian, two's complement |
| `bytes` | `int32` length ‖ raw bytes |
| `string` | `bytes` of the UTF-8 encoding (no BOM, no terminator) |
| `guid` | 16 bytes in RFC 9562 network order: the hex digits of the canonical `8-4-4-4-12` form, left to right (`00112233-4455-6677-8899-aabbccddeeff` → `00 11 22 … ff`) |
| `presence` | 1 byte: `00` absent, `01` present |
| `opt<T>` | `presence`, followed by `T` only when present |
| `timestamp` | `int64` microseconds since 1970-01-01T00:00:00Z (UTC). Values are truncated to whole microseconds **before** they are stored, so the stored `timestamptz` and the encoded value are identical |

`null` and the empty string are different values: `opt<string>` of `null` is `00`; of `""` it is `01 00000000`.

## 3. Payload commitment

```
payload     = bytes("AUDITv3-PAYLOAD")
            ‖ opt<string>(before_snapshot)
            ‖ opt<string>(after_snapshot)
commitment  = SHA-256(salt ‖ payload)          -- salt: the 32 raw bytes, no length prefix
```

`commitment` is stored as `audit_records.payload_hash`. After erasure the salt is gone with the payload,
so a low-entropy snapshot cannot be brute-forced from the commitment.

## 4. Link message and MAC

The MAC of a link is `MAC_key(message)`, where the default algorithm is HMAC-SHA256 (FIPS 198-1) with a
key of at least 32 bytes, and `message` is:

| # | Field | Encoding | Source |
|---|---|---|---|
| 1 | domain separator | `bytes("AUDITv3")` | constant |
| 2 | format version | `int32` = 3 | `audit_records.format_version` |
| 3 | algorithm | `string` | `audit_chain_links.algorithm` (e.g. `HMAC-SHA256`) |
| 4 | key id | `string` | `audit_chain_links.key_id` |
| 5 | tenant | `opt<guid>` | `audit_records.tenant_id` |
| 6 | resource type | `string` | `audit_records.resource_type` |
| 7 | sequence | `int64` | `audit_chain_links.sequence` |
| 8 | previous MAC | `opt<bytes>` | `audit_chain_links.previous_mac` (absent exactly for sequence 1) |
| 9 | record id | `guid` | `audit_records.id` |
| 10 | occurred on | `timestamp` | `audit_records.occurred_on` |
| 11 | resource id | `string` | `audit_records.resource_id` |
| 12 | action | `string` | `audit_records.action` |
| 13 | outcome | `int32` (0 Succeeded, 1 Failed) | `audit_records.outcome` |
| 14 | error code | `opt<string>` | `audit_records.error_code` |
| 15 | actor id | `string` | `audit_records.actor_id` |
| 16 | actor kind | `int32` (0 User, 1 Service, 2 System) | `audit_records.actor_kind` |
| 17 | client id | `opt<string>` | `audit_records.client_id` |
| 18 | session id | `opt<string>` | `audit_records.session_id` |
| 19 | impersonator id | `opt<string>` | `audit_records.impersonator_id` |
| 20 | source service | `string` | `audit_records.source_service` |
| 21 | correlation id | `opt<string>` | `audit_records.correlation_id` |
| 22 | trace id | `opt<string>` | `audit_records.trace_id` (W3C, 32 lowercase hex characters) |
| 23 | approval id | `opt<string>` | `audit_records.approval_id` |
| 24 | idempotency key | `opt<string>` | `audit_records.idempotency_key` |
| 25 | payload commitment | `bytes` (32) | `audit_records.payload_hash` |

The key id and algorithm are inside the MAC, so relabelling a link with another key id or algorithm breaks it.

## 5. Chain verification

Walk the links of one chain in ascending `sequence`, starting at 1 (or at a checkpoint's sequence):

1. `sequence` must equal the expected value → otherwise **SequenceGap** (or **AnchorMismatch** for a missing checkpoint anchor).
2. The record's `(tenant_id, resource_type)` must be the chain's → otherwise **HashMismatch**.
3. The key id and algorithm must be known to the verifier → otherwise **UnknownKey** (status *Unverifiable*).
4. The key's rotation order must not be lower than the highest order already seen in the chain → otherwise **KeyRegression**.
5. For a checkpoint anchor, the stored MAC must equal the checkpoint's `head_mac` → otherwise **AnchorMismatch**; the anchor is then authenticated like every other record (step 6).
6. The recomputed MAC must equal the stored `mac` (constant-time compare) → otherwise **HashMismatch**.
7. Except for the anchor, `previous_mac` must equal the `mac` of the link before it (absent for sequence 1) → otherwise **LinkMismatch**.
8. If the payload row exists, `SHA-256(salt ‖ payload)` must equal `payload_hash` → otherwise **HashMismatch**. An erased payload is counted, and reported as **PayloadErased** only when the caller requires payloads.

With an expected head checkpoint, the chain must reach its sequence (**TailTruncated** otherwise) and the MAC
at that sequence must equal its `head_mac` (**AnchorMismatch** otherwise).

## 6. Checkpoint signature

```
checkpoint_message = bytes("AUDITv3-CHECKPOINT")
                   ‖ int32(3)
                   ‖ guid(checkpoint id)
                   ‖ opt<guid>(tenant)
                   ‖ string(resource type)
                   ‖ int64(head sequence)
                   ‖ bytes(head MAC)
                   ‖ timestamp(created on)
                   ‖ string(signing key id)
```

The signature is produced by the configured asymmetric key (RSA-PSS/RSA-PKCS1/ECDSA per the key). A verifier
must only accept key ids it pins in advance (`AcceptedCheckpointSigningKeyIds`), never the id the checkpoint names.

## 7. Test vectors

Inputs:

* key (`k1`): 32 bytes of `0x42`; salt: bytes `0x01 … 0x20`.
* Vector 1 — record `0190a1b2-c3d4-7e5f-8a9b-0c1d2e3f4a5b`, tenant `11111111-2222-3333-4444-555555555555`,
  resource type `Order`, resource id `order-42`, action `OrderApproved`, outcome Succeeded, actor `user-7`
  (User), source service `orders`, occurred on `2026-01-01T00:00:00.123456Z`, no other optional field,
  before `{"status":"pending"}`, after `{"status":"approved"}`; sequence 1, no previous MAC.
* Vector 2 — record `0190a1b2-c3d4-7e5f-8a9b-0c1d2e3f4a5c`, system chain (no tenant), resource type `Order`,
  resource id `order-43`, action `OrderRejected`, outcome Failed, error code `order.limit_exceeded`, actor
  `svc-batch` (System), client `client-1`, session `session-1`, impersonator `admin-1`, source service `orders`,
  correlation `corr-1`, trace `0af7651916cd43dd8448eb211c80319c`, approval `approval-1`, idempotency key
  `idem-1`, occurred on `2026-01-01T00:00:01Z`, no snapshots; sequence 2, previous MAC = vector 1's MAC.
* Checkpoint — id `0190a1b2-c3d4-7e5f-8a9b-0c1d2e3f4a60`, vector 1's chain, head sequence 1, head MAC = vector
  1's MAC, created on `2026-01-01T01:00:00Z`, signing key id `checkpoints-2026`. (Signatures are
  randomized for ECDSA/PSS, so the message and its SHA-256 are the vectors.)

Outputs (hex):

| Vector | Value |
|---|---|
| `v1.payload` | `0000000f415544495476332d5041594c4f414401000000147b22737461747573223a2270656e64696e67227d01000000157b22737461747573223a22617070726f766564227d` |
| `v1.commitment` | `90b181e58e6cceb0f352beeb2b7008bb6bcacd7f39099e9a089c9eae8f837187` |
| `v1.message` | `0000000741554449547633000000030000000b484d41432d534841323536000000026b310111111111222233334444555555555555000000054f726465720000000000000001000190a1b2c3d47e5f8a9b0c1d2e3f4a5b0006474846222240000000086f726465722d34320000000d4f72646572417070726f766564000000000000000006757365722d3700000000000000000000066f7264657273000000000000002090b181e58e6cceb0f352beeb2b7008bb6bcacd7f39099e9a089c9eae8f837187` |
| `v1.mac` | `6202a8633319b28076d64d1ef95fef632b16c6360b0d99d25e85422458a6dc31` |
| `v2.payload` | `0000000f415544495476332d5041594c4f41440000` |
| `v2.commitment` | `7ae08a98cd6f7154a752b4634296d528b23240bc972dfaed4814b32e379ba249` |
| `v2.message` | `0000000741554449547633000000030000000b484d41432d534841323536000000026b3100000000054f72646572000000000000000201000000206202a8633319b28076d64d1ef95fef632b16c6360b0d99d25e85422458a6dc310190a1b2c3d47e5f8a9b0c1d2e3f4a5c00064748462f8240000000086f726465722d34330000000d4f7264657252656a65637465640000000101000000146f726465722e6c696d69745f6578636565646564000000097376632d6261746368000000020100000008636c69656e742d31010000000973657373696f6e2d31010000000761646d696e2d31000000066f72646572730100000006636f72722d3101000000203061663736353139313663643433646438343438656232313163383033313963010000000a617070726f76616c2d3101000000066964656d2d31000000207ae08a98cd6f7154a752b4634296d528b23240bc972dfaed4814b32e379ba249` |
| `v2.mac` | `ee3ed1b5f62ef5bdddfbe4832bcf86d8ebf10b6874fcc77ad54bbc65439ab6e5` |
| `cp.message` | `00000012415544495476332d434845434b504f494e54000000030190a1b2c3d47e5f8a9b0c1d2e3f4a600111111111222233334444555555555555000000054f726465720000000000000001000000206202a8633319b28076d64d1ef95fef632b16c6360b0d99d25e85422458a6dc31000647491cb3e40000000010636865636b706f696e74732d32303236` |
| `cp.message.sha256` | `b786a7a3e9142f15dda96971b73f15bdcc84dc81ce9b9951ef362458db68d435` |

## 8. Versioning

Any change to an encoding above is a new format version with a new domain separator (`AUDITv4`, …).
Records and links carry `format_version`; a verifier that does not know a version reports the record as
*Unverifiable* instead of guessing.
