#!/usr/bin/env bash
# This file is the pull request title rule.
# Call this file from the pull request workflow. Do not copy the rule.
set -eu -o pipefail
export LC_ALL=C

usage() {
  printf '%s\n' \
    'Usage: eng/check-pr-title.sh --examples' \
    '       TITLE=<pull request title> eng/check-pr-title.sh --check'
}

display_title() {
  local title=$1
  if [[ -z $title ]]; then
    printf '<empty>'
    return
  fi
  title=${title//$'\n'/\\n}
  title=${title//$'\r'/\\r}
  printf '%s' "$title"
}

title_matches() {
  local title=$1
  local type='build|chore|ci|docs|feat|fix|perf|refactor|revert|style|test'
  local scope='[a-z0-9]+(-[a-z0-9]+)*'
  local pattern="^(${type})(\\(${scope}\\))?(!)?: [^[:space:]](.*[^[:space:]])?$"

  if [[ $title == *$'\n'* || $title == *$'\r'* ]]; then
    return 1
  fi
  [[ $title =~ $pattern ]]
}

example_failures=0

record_example() {
  local expected=$1
  local reason=$2
  local title=$3
  local actual=reject

  if title_matches "$title"; then
    actual=accept
  fi
  printf '%s\t%s\t%s\n' "$actual" "$reason" "$(display_title "$title")"
  if [[ $actual != "$expected" ]]; then
    example_failures=$((example_failures + 1))
    printf 'Example mismatch. Expected %s for %s.\n' "$expected" "$reason" >&2
  fi
}

examples() {
  example_failures=0

  record_example accept 'documented example' 'chore(build): pin the .NET SDK'
  record_example accept 'type without scope' 'feat: add document identity'
  record_example accept 'scope' 'fix(parser): handle empty input'
  record_example accept 'breaking change indicator' 'feat!: change document identity'
  record_example accept 'breaking change indicator with scope' 'fix(core)!: reject unknown encoding'
  record_example accept 'hyphenated scope' 'refactor(editor-app): move startup'
  record_example accept 'digit in scope' 'build(net10): pin the SDK'
  record_example accept 'one visible character' 'feat: x'
  record_example accept 'type ci' 'ci: run the title check'
  record_example accept 'type docs' 'docs: record the title rule'
  record_example accept 'type perf' 'perf(core): reduce allocations'
  record_example accept 'type revert' 'revert: undo the document change'
  record_example accept 'type style' 'style: apply the format'
  record_example accept 'type test' 'test(desktop): verify native startup'

  record_example reject 'invalid type' 'feature: add document identity'
  record_example reject 'invalid type case' 'Feat: add document identity'
  record_example reject 'empty description' 'feat:'
  record_example reject 'whitespace description' 'feat: '
  record_example reject 'empty description with breaking change indicator' 'feat!:'
  record_example reject 'breaking change indicator in the wrong position' 'feat!(core): change document identity'
  record_example reject 'missing space after colon' 'feat:add document identity'
  record_example reject 'empty scope' 'feat(): add document identity'
  record_example reject 'invalid scope case' 'feat(Core): add document identity'
  record_example reject 'space in scope' 'feat(my scope): add document identity'
  record_example reject 'underscore in scope' 'feat(my_scope): add document identity'
  record_example reject 'dot in scope' 'feat(editor.core): add document identity'
  record_example reject 'missing type' 'add document identity'
  record_example reject 'empty title' ''
  record_example reject 'leading space' ' feat: add document identity'
  record_example reject 'trailing space' 'feat: add document identity '
  record_example reject 'extra space after colon' 'feat:  add document identity'
  record_example reject 'repeated breaking change indicator' 'feat!!: change document identity'
  record_example reject 'second line' $'feat: add document identity\nmore'

  if (( example_failures > 0 )); then
    printf 'Title examples failed.\n' >&2
    return 1
  fi
  printf 'Title examples passed.\n'
}

reject_title() {
  printf '%s\n' \
    'The pull request title does not match the Conventional Commit rule.' \
    'Use this form: type, optional (scope), optional !, colon, one space, description.' \
    'The type is one of: build, chore, ci, docs, feat, fix, perf, refactor, revert, style, test.' \
    'A scope uses lowercase letters, digits, and hyphens.' \
    'Put ! immediately before the colon.' \
    'Write a description with visible text.' \
    'Do not add leading or trailing whitespace.' \
    'The title must be one line.' \
    "Received title: $(display_title "$TITLE")" >&2
}

check_title() {
  examples
  if [[ ${TITLE+set} != set ]]; then
    printf 'TITLE is not set.\n' >&2
    usage >&2
    return 2
  fi
  if title_matches "$TITLE"; then
    printf 'The pull request title matches the Conventional Commit rule.\n'
    printf 'Received title: %s\n' "$(display_title "$TITLE")"
    return 0
  fi
  reject_title
  return 1
}

if [[ $# -ne 1 ]]; then
  usage >&2
  exit 2
fi

case $1 in
  --examples)
    examples
    ;;
  --check)
    check_title
    ;;
  *)
    printf 'The argument is not supported: %s\n' "$1" >&2
    usage >&2
    exit 2
    ;;
esac
