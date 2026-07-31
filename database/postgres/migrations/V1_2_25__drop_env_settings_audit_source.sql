-- 移除未使用的环境设置表（1.2.23）；审计 source 默认值改为 *_desktop

drop table if exists app_environment_settings;

alter table drug_key_fix_audit
  alter column source set default 'drug_index_desktop';

update drug_key_fix_audit
set source = 'drug_index_desktop'
where source = 'drug_index_ui';

alter table inventory_reassign_audit
  alter column source set default 'inventory_desktop';

update inventory_reassign_audit
set source = 'inventory_desktop'
where source = 'inventory_ui';
