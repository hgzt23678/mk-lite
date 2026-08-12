#!/usr/bin/env bash
set -euo pipefail

required=(SOURCE_PROBE RESTORED_PROBE POSTGRES_RESTORE_HOOK S3_RESTORE_HOOK VAULT_RESTORE_HOOK DATA_PROTECTION_RESTORE_HOOK EGRESS_ASSERT_HOOK)
for name in "${required[@]}"; do
  value=${!name:-}
  if [[ -z "$value" || ! -x "$value" ]]; then
    echo "$name must name an executable, environment-specific hook" >&2
    exit 2
  fi
done

artifact_directory=${ARTIFACT_DIRECTORY:-"$PWD/artifacts/recovery-$(date -u +%Y%m%dT%H%M%SZ)"}
mkdir -p "$artifact_directory"
started_epoch=$(date -u +%s)

source_postgres=$("$SOURCE_PROBE" postgres | tail -1)
source_vault=$("$SOURCE_PROBE" vault | tail -1)
source_media=$("$SOURCE_PROBE" media-create | tail -1)
source_data_protection=$("$SOURCE_PROBE" data-protection-protect | tail -1)
media_id=$(jq -er '.id' <<<"$source_media")
protected_payload=$(jq -er '.protectedPayload' <<<"$source_data_protection")
protected_payload_hash=$(printf '%s' "$protected_payload" | sha256sum | cut -d' ' -f1)

postgres_restore=$("$POSTGRES_RESTORE_HOOK" | tail -1)
s3_restore=$("$S3_RESTORE_HOOK" | tail -1)
vault_restore=$("$VAULT_RESTORE_HOOK" | tail -1)
data_protection_restore=$("$DATA_PROTECTION_RESTORE_HOOK" | tail -1)
egress_assertion=$("$EGRESS_ASSERT_HOOK" | tail -1)

jq -e '.component == "postgres" and .mode == "PITR" and .restored == true and (.targetTime | type == "string")' <<<"$postgres_restore" >/dev/null
jq -e '.component == "s3" and .versionRestore == true and .restored == true' <<<"$s3_restore" >/dev/null
jq -e '.component == "vault" and .snapshotRestored == true and .restored == true' <<<"$vault_restore" >/dev/null
jq -e '.component == "data-protection" and .certificateRestored == true and .restored == true' <<<"$data_protection_restore" >/dev/null
jq -e '.egressBlocked == true' <<<"$egress_assertion" >/dev/null

restored_postgres=$("$RESTORED_PROBE" postgres | tail -1)
restored_vault=$("$RESTORED_PROBE" vault | tail -1)
restored_media=$("$RESTORED_PROBE" media-open "$media_id" | tail -1)
restored_data_protection=$("$RESTORED_PROBE" data-protection-unprotect "$protected_payload" | tail -1)

jq -e '.connected == true' <<<"$restored_postgres" >/dev/null
jq -e '.signatureVerified == true' <<<"$restored_vault" >/dev/null
jq -e '.restored == true' <<<"$restored_media" >/dev/null
jq -e '.restored == true' <<<"$restored_data_protection" >/dev/null

completed_epoch=$(date -u +%s)
jq -n \
  --arg completedAt "$(date -u +%Y-%m-%dT%H:%M:%SZ)" \
  --arg mediaId "$media_id" \
  --arg protectedPayloadHash "$protected_payload_hash" \
  --argjson durationSeconds "$(( completed_epoch - started_epoch ))" \
  --argjson postgresRestore "$postgres_restore" \
  --argjson s3Restore "$s3_restore" \
  --argjson vaultRestore "$vault_restore" \
  --argjson dataProtectionRestore "$data_protection_restore" \
  --argjson egress "$egress_assertion" \
  --argjson restoredPostgres "$restored_postgres" \
  --argjson restoredVault "$restored_vault" \
  --argjson restoredMedia "$restored_media" \
  --argjson restoredDataProtection "$restored_data_protection" \
  '{completedAt:$completedAt,durationSeconds:$durationSeconds,mediaId:$mediaId,protectedPayloadHash:$protectedPayloadHash,restoreEvidence:{postgres:$postgresRestore,s3:$s3Restore,vault:$vaultRestore,dataProtection:$dataProtectionRestore,egress:$egress},verification:{postgres:$restoredPostgres,vault:$restoredVault,media:$restoredMedia,dataProtection:$restoredDataProtection},passed:true}' \
  | tee "$artifact_directory/result.json"
