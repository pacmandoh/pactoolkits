-- 收敛注入任务状态：彻底移除 PARTIAL，并删除旧的队列直领 claim 入口。
-- 1) 历史 PARTIAL 一律归并为 FAILED
-- 2) 任务状态约束不再接受 PARTIAL
-- 3) 删除旧的 msfx_claim_inject_tasks(text, integer)

update msfx_inject_task
set
  status = 'FAILED',
  err_msg = case
              when coalesce(err_msg, '') = '' then 'legacy PARTIAL normalized to FAILED'
              else err_msg
            end,
  finished_at = coalesce(finished_at, now())
where status = 'PARTIAL';

alter table if exists msfx_inject_task
  drop constraint if exists msfx_inject_task_status_check;

alter table if exists msfx_inject_task
  add constraint msfx_inject_task_status_check
  check (status in ('NEW', 'RUNNING', 'SUCCESS', 'FAILED', 'CANCELLED'));

drop function if exists msfx_claim_inject_tasks(text, integer);
