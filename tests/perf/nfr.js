// Load test of the non-functional requirements (docs/performance.md, ADR 0158). Run through tests/perf/run.sh.
//
//   BASE_URL   application (default http://localhost:5197)
//   SEED_FILE  JSON written by the PerformanceVolumeSeed runner: personal access tokens and record ids
//   LOAD_VUS   virtual users of the concurrency phase (default 100)
//
// Phase "baseline" (0–2 min): each NFR measured with a few concurrent users.
// Phase "load" (2:30–8:30): LOAD_VUS users browse records, Gantt charts, lists, My Work and dashboards across the active projects,
// with 1–3 s think time (a "concurrent active user" model), while the NFR timings keep being recorded.
import http from 'k6/http';
import { check, sleep } from 'k6';
import { Trend, Rate } from 'k6/metrics';

const seed = JSON.parse(open(__ENV.SEED_FILE || '/scripts/seed.json'));
const base = __ENV.BASE_URL || 'http://localhost:5197';
const loadVus = Number(__ENV.LOAD_VUS || 100);

const recordPage = new Trend('record_page_ms', true);
const gantt = new Trend('gantt_ms', true);
const executive = new Trend('executive_ms', true);
const myWork = new Trend('my_work_ms', true);
const listPage = new Trend('list_page_ms', true);
const failures = new Rate('failed_requests');

export const options = {
  discardResponseBodies: true,
  scenarios: {
    record_baseline: { executor: 'constant-vus', vus: 5, duration: '2m', exec: 'recordFlow', tags: { phase: 'baseline' } },
    gantt_baseline: { executor: 'constant-vus', vus: 2, duration: '2m', exec: 'ganttFlow', tags: { phase: 'baseline' } },
    executive_baseline: { executor: 'constant-vus', vus: 2, duration: '2m', exec: 'executiveFlow', tags: { phase: 'baseline' } },
    mywork_baseline: { executor: 'constant-vus', vus: 2, duration: '2m', exec: 'myWorkFlow', tags: { phase: 'baseline' } },
    lists_baseline: { executor: 'constant-vus', vus: 2, duration: '2m', exec: 'listFlow', tags: { phase: 'baseline' } },
    browse_load: {
      executor: 'ramping-vus',
      startTime: '2m30s',
      stages: [
        { duration: '1m', target: loadVus },
        { duration: '4m', target: loadVus },
        { duration: '1m', target: 0 },
      ],
      exec: 'browse',
      tags: { phase: 'load' },
    },
  },
  thresholds: {
    'record_page_ms{phase:baseline}': ['p(50)<3000', 'p(95)<3000', 'p(99)<60000', 'max<60000'],
    'gantt_ms{phase:baseline}': ['p(50)<5000', 'p(95)<5000', 'p(99)<60000', 'max<60000'],
    'executive_ms{phase:baseline}': ['p(50)<3000', 'p(95)<3000', 'p(99)<60000', 'max<60000'],
    'my_work_ms{phase:baseline}': ['p(50)<3000', 'p(95)<3000', 'p(99)<60000', 'max<60000'],
    'list_page_ms{phase:baseline}': ['p(50)<3000', 'p(95)<3000', 'p(99)<60000', 'max<60000'],
    'record_page_ms{phase:load}': ['p(50)<3000', 'p(95)<3000', 'p(99)<60000', 'max<60000'],
    'gantt_ms{phase:load}': ['p(50)<5000', 'p(95)<5000', 'p(99)<60000', 'max<60000'],
    'executive_ms{phase:load}': ['p(50)<3000', 'p(95)<3000', 'p(99)<60000', 'max<60000'],
    'list_page_ms{phase:load}': ['p(50)<3000', 'p(95)<3000', 'p(99)<60000', 'max<60000'],
    failed_requests: ['rate<0.01'],
  },
  summaryTrendStats: ['avg', 'min', 'med', 'p(90)', 'p(95)', 'p(99)', 'max'],
};

const auth = (token) => ({ headers: { Authorization: `Bearer ${token}`, Accept: 'application/json' } });

function pick(list) {
  return list[Math.floor(Math.random() * list.length)];
}

/** Fires the requests a page makes in parallel (as the SPA does) and records the time until the last one answered. */
function page(trend, token, paths, tags) {
  const params = auth(token);
  const started = Date.now();
  const responses = http.batch(paths.map((p) => ['GET', `${base}${p}`, null, params]));
  const elapsed = Date.now() - started;
  trend.add(elapsed, tags);
  for (const r of responses) {
    const ok = check(r, { 'status 200': (x) => x.status === 200 });
    failures.add(!ok);
  }
}

const recordPaths = (id) => [
  `/api/v1/portfolio/projects/${id}`,
  `/api/v1/portfolio/projects/${id}/milestones`,
  `/api/v1/portfolio/projects/${id}/health`,
  `/api/v1/portfolio/projects/${id}/change-requests`,
  `/api/v1/workflow/status/portfolio.project/${id}`,
];

const ganttPaths = (id) => [`/api/v1/portfolio/projects/${id}`, `/api/v1/portfolio/projects/${id}/gantt`];

const lists = [
  '/api/v1/portfolio/projects?page=1&pageSize=25',
  '/api/v1/portfolio/requests?page=1&pageSize=25',
  '/api/v1/kpi/cards?page=1&pageSize=25',
  '/api/v1/risk/risks?page=1&pageSize=25',
  '/api/v1/tasks?page=1&pageSize=25',
  '/api/v1/cap/remarks?page=1&pageSize=25',
  '/api/v1/auditmgmt/findings?page=1&pageSize=25',
];

export function recordFlow() {
  page(recordPage, seed.directorToken, recordPaths(pick(seed.activeProjects)));
}

export function ganttFlow() {
  page(gantt, seed.directorToken, ganttPaths(seed.ganttProject));
}

export function executiveFlow() {
  // The Director General (scope All, cached facts) and a unit-scoped executive viewer.
  page(executive, Math.random() < 0.5 ? seed.directorToken : seed.unitToken, ['/api/v1/reporting/dashboards/executive/data?kind=year']);
}

export function myWorkFlow() {
  page(myWork, seed.directorToken, ['/api/v1/reporting/my-work/cards', '/api/v1/workflow/my-work']);
}

export function listFlow() {
  page(listPage, seed.directorToken, [pick(lists)]);
}

/** One concurrent user: opens a list, a project record, sometimes its Gantt chart, My Work or the executive dashboard. */
export function browse() {
  const tags = { phase: 'load' };
  page(listPage, seed.directorToken, [pick(lists)], tags);
  sleep(1 + Math.random() * 2);
  const id = pick(seed.activeProjects);
  page(recordPage, seed.directorToken, recordPaths(id), tags);
  sleep(1 + Math.random() * 2);
  const roll = Math.random();
  if (roll < 0.3) {
    page(gantt, seed.directorToken, ganttPaths(roll < 0.1 ? seed.ganttProject : id), tags);
  } else if (roll < 0.5) {
    page(executive, seed.directorToken, ['/api/v1/reporting/dashboards/executive/data?kind=year'], tags);
  } else if (roll < 0.7) {
    page(myWork, seed.directorToken, ['/api/v1/reporting/my-work/cards', '/api/v1/workflow/my-work'], tags);
  }

  sleep(1 + Math.random() * 2);
}
