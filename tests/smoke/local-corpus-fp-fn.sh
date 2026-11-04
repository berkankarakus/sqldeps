#!/usr/bin/env bash
set -euo pipefail

corpus_db="${SQLDEPS_CORPUS_DB:-${HOME}/.local/share/sqldeps/local-corpus.sqlite}"

if [[ ! -f "$corpus_db" ]]; then
    printf 'Local corpus index not found: %s\n' "$corpus_db" >&2
    exit 1
fi

printf '%s\n' 'local-corpus FP/FN report'
printf 'database: %s\n' "$corpus_db"
printf '%s\n' '--- inventory ---'
sqlite3 -header -column "$corpus_db" <<'SQL'
SELECT
    (SELECT COUNT(*) FROM files) AS files,
    (SELECT COUNT(*) FROM parse_errors) AS parse_errors,
    (SELECT COUNT(*) FROM parse_warnings) AS parse_warnings,
    (SELECT COUNT(*) FROM lint_findings) AS lint_findings;
SQL

printf '%s\n' '--- findings by rule ---'
sqlite3 -header -column "$corpus_db" <<'SQL'
SELECT rule, severity, COUNT(*) AS findings, COUNT(DISTINCT source_file) AS files
FROM lint_findings
GROUP BY rule, severity
ORDER BY findings DESC, rule;
SQL

printf '%s\n' '--- review buckets ---'
sqlite3 -header -column "$corpus_db" <<'SQL'
SELECT 'remote/correlated alias binding' AS bucket,
       COUNT(*) AS findings,
       CASE WHEN COUNT(*) = 0 THEN 'fixed; keep regression fixture'
            ELSE 'review remaining alias scope' END AS decision
FROM lint_findings
WHERE rule = 'unbound-identifier';

SELECT 'conservative CTE/derived DML warnings' AS bucket,
       COUNT(*) AS findings,
       'keep warning; updateability depends on query shape' AS decision
FROM lint_findings
WHERE rule IN ('dml-cte-target', 'dml-derived-target');

SELECT 'dynamic SQL concatenation' AS bucket,
       COUNT(*) AS findings,
       'keep warning; do not inspect runtime-expanded SQL' AS decision
FROM lint_findings
WHERE rule = 'dynamic-sql-concatenation';

SELECT 'parser input boundaries' AS bucket,
       (SELECT COUNT(*) FROM parse_errors) + (SELECT COUNT(*) FROM parse_warnings) AS findings,
       'keep parse diagnostics; corpus files stay outside Git' AS decision;
SQL
