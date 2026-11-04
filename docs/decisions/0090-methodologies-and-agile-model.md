# 0090 — Delivery methodologies and the agile data model

- **Status:** Proposed (Phase 10)
- **Context:** SPEC §1.13: methodology chosen at charter (Take Ownership), suggested by the request type, changed only through an approved
  change request; agile objects (releases, epics with weights, user stories, sprints, review, retrospective, impediments), configurable
  boards. The SPEC does not say what "request type" means for the suggestion, nor how stories move.

## Decision
- `project.methodology` (`waterfall` / `agile` / `hybrid`). The suggestion comes from Settings → Portfolio `methodologySuggestions`
  (program type code → methodology; default empty, i.e. waterfall). The PM chooses while drafting the charter (or, for templates without a
  charter, before the first planning submission). Afterwards only a change request of type *details* with the area **methodology** changes it.
- Agile tables `release`, `epic` (weight ratio; = 100 % at the planning gate; hybrid: linked to a milestone and = 100 % of it),
  `user_story`, `sprint`, `story_event` (append-only moves for metrics). Story state **is** its board column; columns are configured per
  project (code, labels, category to-do / doing / done, WIP limit) with swimlanes none / epic / assignee.
- WIP limits are **enforced** on the server (a move into a full column is refused); a story whose completion was approved is locked in done.
- Sprint lifecycle planned → active → closed → in-approval → approved (returned → closed). Closing requires the review (demo outcome);
  retrospective items become lessons learned; unfinished stories return to the backlog or the next planned sprint.
- Impediments become RAID issues (or challenges when RAID is off). Story tasks are Task Management tasks (`portfolio.story` task-able).
- Team members create stories only when `teamMembersCreateStories` is on (default off); they move stories assigned to them.

## Consequences
Agile and waterfall share governance, access and the common progress; only planning and execution differ. Scrum and Kanban are the same
model (a Kanban team simply runs long sprints or ignores sprint dates).
