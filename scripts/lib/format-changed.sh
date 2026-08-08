# FORMAT_CHANGED=1 时各 formatter 只处理 git 变更文件（agent/本地 commit gate）
# CI 不设该变量，仍全量检查

format_changed_enabled() {
  [[ "${FORMAT_CHANGED:-}" == "1" ]]
}

# 打印相对 ROOT_DIR、且磁盘上存在的变更/未跟踪路径（pathspec 可选）
format_collect_changed_files() {
  {
    git -C "$ROOT_DIR" diff --name-only --diff-filter=ACMR HEAD -- "$@"
    git -C "$ROOT_DIR" ls-files --others --exclude-standard -- "$@"
  } | sort -u | while IFS= read -r file; do
    [[ -n "$file" && -f "$ROOT_DIR/$file" ]] || continue
    printf '%s\n' "$file"
  done
}
