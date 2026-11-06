// Notification latency (SPEC §0.2: e-mails delivered within 5 minutes): assigns a task to a person and measures the time until the
// "New task" e-mail reaches Mailpit. Run through tests/perf/run.sh (MAILPIT_URL = Mailpit's HTTP API).
import http from 'k6/http';
import { check, sleep } from 'k6';
import { Trend, Rate } from 'k6/metrics';

const seed = JSON.parse(open(__ENV.SEED_FILE || '/scripts/seed.json'));
const base = __ENV.BASE_URL || 'http://localhost:5197';
const mailpit = __ENV.MAILPIT_URL || 'http://localhost:8325';

const latency = new Trend('notification_latency_ms', true);
const lost = new Rate('notification_lost');

export const options = {
  scenarios: { notify: { executor: 'shared-iterations', vus: 2, iterations: Number(__ENV.NOTIFY_ITERATIONS || 10), maxDuration: '30m' } },
  thresholds: { notification_latency_ms: ['max<300000'], notification_lost: ['rate==0'] },
  summaryTrendStats: ['avg', 'min', 'med', 'p(95)', 'max'],
};

export default function () {
  const title = `perf-notify-${__VU}-${__ITER}-${Date.now()}`;
  const started = Date.now();
  const created = http.post(
    `${base}/api/v1/tasks`,
    JSON.stringify({
      titleEn: title,
      typeCode: __ENV.TASK_TYPE || 'manager-assignment',
      sourceCode: __ENV.TASK_SOURCE || 'committee-decision',
      assigneeEmployeeIds: [seed.assigneeEmployeeId],
    }),
    { headers: { Authorization: `Bearer ${seed.directorToken}`, 'Content-Type': 'application/json' } },
  );
  check(created, { 'task created': (r) => r.status === 201 });

  while (Date.now() - started < 300000) {
    const found = http.get(`${mailpit}/api/v1/search?query=${encodeURIComponent(title)}`);
    if (found.status === 200 && found.json('messages_count') > 0) {
      latency.add(Date.now() - started);
      lost.add(false);
      return;
    }

    sleep(1);
  }

  lost.add(true);
}
