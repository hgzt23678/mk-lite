#!/usr/bin/env bash
set -euo pipefail

if [[ -z "${APPLICATION_PROBE:-}" || ! -x "$APPLICATION_PROBE" ]]; then
  echo "APPLICATION_PROBE must name an executable wrapper for 'dependency-probe' against the HA endpoint" >&2
  exit 2
fi
if [[ -z "${FAILOVER_HOOK:-}" || ! -x "$FAILOVER_HOOK" ]]; then
  echo "FAILOVER_HOOK must name an executable that promotes the configured standby through the platform control plane" >&2
  exit 2
fi

artifact_directory=${ARTIFACT_DIRECTORY:-"$PWD/artifacts/postgres-failover-$(date -u +%Y%m%dT%H%M%SZ)"}
maximum_recovery_seconds=${MAXIMUM_RECOVERY_SECONDS:-120}
mkdir -p "$artifact_directory"
protected_payload=$("$APPLICATION_PROBE" data-protection-protect | tail -1 | jq -er '.protectedPayload')
before=$("$APPLICATION_PROBE" postgres | tail -1)
started_epoch=$(date -u +%s)
hook_result=$("$FAILOVER_HOOK" | tail -1)

recovered=false
while (( $(date -u +%s) - started_epoch <= maximum_recovery_seconds )); do
  if after=$("$APPLICATION_PROBE" postgres 2>"$artifact_directory/last-probe-error.log" | tail -1) &&
     unprotected=$("$APPLICATION_PROBE" data-protection-unprotect "$protected_payload" 2>>"$artifact_directory/last-probe-error.log" | tail -1); then
    recovered=true
    break
  fi
  sleep 2
done
if [[ "$recovered" != true ]]; then
  echo "PostgreSQL HA endpoint did not recover within $maximum_recovery_seconds seconds" >&2
  exit 1
fi

recovery_seconds=$(( $(date -u +%s) - started_epoch ))
jq -n \
  --argjson before "$before" \
  --argjson after "$after" \
  --argjson dataProtection "$unprotected" \
  --argjson hook "$hook_result" \
  --argjson recoverySeconds "$recovery_seconds" \
  --argjson maximumRecoverySeconds "$maximum_recovery_seconds" \
  '{before:$before,after:$after,dataProtection:$dataProtection,hook:$hook,recoverySeconds:$recoverySeconds,maximumRecoverySeconds:$maximumRecoverySeconds,passed:($after.connected == true and $dataProtection.restored == true and $recoverySeconds <= $maximumRecoverySeconds)}' \
  | tee "$artifact_directory/result.json"
