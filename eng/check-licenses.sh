#!/usr/bin/env bash
set -euo pipefail

repository_root=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
package_cache=${NUGET_PACKAGES:-${HOME}/.nuget/packages}
package_list=$(mktemp)
trap 'rm -f "$package_list"' EXIT

find "$repository_root" -name packages.lock.json -not -path '*/obj/*' -print0 \
  | xargs -0 jq -r '(.dependencies[] | to_entries[]) | select(.value.resolved?) | [.key, .value.resolved] | @tsv' \
  | sort -u > "$package_list"

failure=0
printf 'PACKAGE\tVERSION\tLICENSE\n'
while IFS=$'\t' read -r package version; do
  package_lower=$(printf '%s' "$package" | tr '[:upper:]' '[:lower:]')
  nuspec=$(find "$package_cache/$package_lower/$version" -maxdepth 1 -name '*.nuspec' -print -quit 2>/dev/null || true)
  license=$(sed -n 's/.*<license type="expression">\([^<]*\)<\/license>.*/\1/p' "$nuspec" | head -1)
  if [[ -z "$license" ]]; then
    license=$(sed -n 's/.*<licenseUrl>\([^<]*\)<\/licenseUrl>.*/URL:\1/p' "$nuspec" | head -1)
  fi

  printf '%s\t%s\t%s\n' "$package" "$version" "${license:-UNKNOWN}"
  case "$license" in
    MIT|Apache-2.0|PostgreSQL|BSD-2-Clause|BSD-3-Clause|"URL:https://raw.githubusercontent.com/xunit/xunit/master/license.txt") ;;
    *)
      printf 'Unapproved or unknown license: %s %s (%s)\n' "$package" "$version" "${license:-UNKNOWN}" >&2
      failure=1
      ;;
  esac
done < "$package_list"

exit "$failure"
