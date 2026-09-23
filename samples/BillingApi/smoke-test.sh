#!/usr/bin/env bash
# End-to-end smoke test of a running BillingApi (docker compose up -d). Every step asserts; the first failure exits 1.
#   ./smoke-test.sh                      # http://localhost:8080
#   BASE_URL=http://localhost:5280 ./smoke-test.sh
set -euo pipefail

BASE="${BASE_URL:-http://localhost:8080}"
TENANT_A=$(cat /proc/sys/kernel/random/uuid 2>/dev/null || powershell -NoProfile -Command "[guid]::NewGuid().ToString()" | tr -d '\r')
TENANT_B=$(cat /proc/sys/kernel/random/uuid 2>/dev/null || powershell -NoProfile -Command "[guid]::NewGuid().ToString()" | tr -d '\r')
A=(-H "X-Demo-User: alice" -H "X-Demo-Tenant: $TENANT_A" -H "X-Demo-Permissions: billing.read,billing.write")
B=(-H "X-Demo-User: bob" -H "X-Demo-Tenant: $TENANT_B" -H "X-Demo-Permissions: billing.read,billing.write")
ADMIN=(-H "X-Demo-User: back-office" -H "X-Demo-Permissions: billing.admin")
JSON=(-H "Content-Type: application/json")
STATUS=""; BODY=""; HEADERS=""

call() { # method path [curl args...]
  local method=$1 path=$2; shift 2
  local out; out=$(curl -s -D - -X "$method" "$BASE$path" "$@" -w '\n%{http_code}')
  STATUS=$(tail -n1 <<<"$out")
  HEADERS=$(sed '/^\r\{0,1\}$/q' <<<"$out")
  BODY=$(sed '1,/^\r\{0,1\}$/d' <<<"$out" | sed '$d')
}
expect() { # expected-status description
  if [[ "$STATUS" != "$1" ]]; then echo "FAIL $2: expected $1, got $STATUS: $BODY" >&2; exit 1; fi
  echo "ok   $2"
}
contains() { # text description
  if [[ "$BODY" != *"$1"* ]]; then echo "FAIL $2: '$1' not in: $BODY" >&2; exit 1; fi
  echo "ok   $2"
}
field() { sed -E "s/.*\"$1\":\"?([^\",}]*)\"?.*/\1/" <<<"$BODY"; }

call GET /health/ready;                                                     expect 200 "ready (migrations, RLS checks, audit self-check, key ring)"

call POST /customers "${A[@]}" "${JSON[@]}" -d '{"name":"Ada Lovelace","email":"Ada@Example.com","taxNumber":"TR-1234"}'
expect 201 "register customer (email + tax number encrypted)"; CUSTOMER=$(field id)
call GET "/customers?email=%20ada@EXAMPLE.com" "${A[@]}";                  expect 200 "find by encrypted email via blind index"
call POST /customers "${A[@]}" "${JSON[@]}" -d '{"name":"Dup","email":"ADA@example.com"}'
expect 409 "duplicate email rejected"

call GET "/customers/$CUSTOMER" "${A[@]}";                                  expect 200 "read customer with ETag"
ETAG=$(grep -i '^etag:' <<<"$HEADERS" | cut -d' ' -f2 | tr -d '\r')
[[ "$ETAG" != '"0"' ]] || { echo "FAIL ETag is a placeholder" >&2; exit 1; }
call GET "/customers/$CUSTOMER" "${A[@]}" -H "If-None-Match: $ETAG";        expect 304 "unchanged customer revalidated without a body"
call PUT "/customers/$CUSTOMER/name" "${A[@]}" "${JSON[@]}" -d '{"name":"No precondition"}'
expect 428 "rename without If-Match refused"; contains '"errorCode":"precondition.required"' "428 is a problem with its code"
call PUT "/customers/$CUSTOMER/name" "${A[@]}" "${JSON[@]}" -H "If-Match: $ETAG" -d '{"name":"Ada King"}'
expect 200 "rename with If-Match"
call PUT "/customers/$CUSTOMER/name" "${A[@]}" "${JSON[@]}" -H "If-Match: $ETAG" -d '{"name":"Lost update"}'
expect 412 "stale If-Match rejected"

call POST /invoices "${A[@]}" "${JSON[@]}" -d "{\"customerId\":\"$CUSTOMER\",\"currency\":\"EUR\",\"taxRate\":\"STD\",\"lines\":[{\"description\":\"Consulting\",\"quantity\":3,\"unitPrice\":100.50},{\"description\":\"Travel\",\"quantity\":1,\"unitPrice\":49.50}]}"
expect 201 "draft invoice"; INVOICE=$(field id)
call POST "/invoices/$INVOICE/issue" "${A[@]}";                             expect 204 "issue invoice (domain event)"
call GET "/customers/$CUSTOMER" "${A[@]}";                                  contains '"invoiceCount":1' "domain event handler updated the customer in the same save"
call POST "/invoices/$INVOICE/payments" "${A[@]}" "${JSON[@]}" -d '{"amount":1,"reference":"short"}'
expect 400 "wrong amount: Dapper insert rolled back with the failed command"
call POST "/invoices/$INVOICE/payments" "${A[@]}" "${JSON[@]}" -d '{"amount":421.20,"reference":"SEPA-1"}'
expect 204 "payment: Dapper + EF Core in one transaction"
call GET /reports/revenue "${A[@]}";                                        contains '"received":421.2' "revenue report (Dapper under RLS)"

call GET "/customers/$CUSTOMER" "${B[@]}";                                  expect 404 "other tenant: customer invisible"
call GET "/invoices/$INVOICE" "${B[@]}";                                    expect 404 "other tenant: invoice invisible"
call GET /reports/revenue "${B[@]}";                                        expect 200 "other tenant: revenue report"
[[ "$BODY" == "[]" ]] || { echo "FAIL other tenant sees revenue: $BODY" >&2; exit 1; }; echo "ok   other tenant: hand-written SQL sees no rows (RLS)"

call GET "/invoices?page=1&pageSize=10" "${A[@]}";                          contains '"totalCount":1' "offset paging"
call GET "/invoices/browse?limit=10" "${A[@]}";                             contains '"hasMore":false' "keyset paging"

call GET "/invoices?page=1" ;                                               expect 401 "anonymous rejected"
call POST /customers -H "X-Demo-User: eve" -H "X-Demo-Tenant: $TENANT_A" -H "X-Demo-Permissions: billing.read" "${JSON[@]}" -d '{"name":"x","email":"x@y.z"}'
expect 403 "missing permission rejected"
call GET /admin/reports/revenue-by-tenant "${ADMIN[@]}";                    contains "$TENANT_A" "back office sees every tenant (cross-tenant role)"

call GET "/audit/Invoice/$INVOICE" "${A[@]}";                               contains '"outcome":"Failed"' "audit trail records succeeded and failed commands"
sleep 3
call GET /audit/Invoice "${A[@]}";                                          contains '"status":"Intact"' "audit chain sealed and intact"

call DELETE "/customers/$CUSTOMER" "${A[@]}";                               expect 428 "delete without If-Match refused"
call GET "/customers/$CUSTOMER" "${A[@]}";                                  expect 200 "re-read the customer for its current ETag"
ETAG=$(grep -i '^etag:' <<<"$HEADERS" | cut -d' ' -f2 | tr -d '\r')
call DELETE "/customers/$CUSTOMER" "${A[@]}" -H "If-Match: $ETAG";          expect 204 "soft delete with If-Match"
call GET "/customers/$CUSTOMER" "${A[@]}";                                  expect 404 "soft-deleted customer hidden"
call POST "/admin/tenants/$TENANT_A/erase" "${A[@]}";                       expect 403 "erasure needs billing.admin (route policy)"
call POST "/admin/tenants/$TENANT_A/erase" "${ADMIN[@]}";                   contains '"isComplete":true' "tenant crypto-shredded"
call POST /customers "${A[@]}" "${JSON[@]}" -d '{"name":"Back","email":"back@example.com"}'
expect 404 "erased tenant cannot write encrypted data"

echo "All BillingApi smoke checks passed against $BASE"
