-- drug_index：摆放位置（pos）
alter table drug_index
  add column if not exists pos text;
