# Salesforce standby sync: setup, monitoring, failover and return to standby

For administrators (Phase 23, SPEC-ADDENDUM §G, ADRs 0200–0208). Screens: **Settings → Integrations → Salesforce sync**.

While Salesforce is the **primary** system, this system receives a regular **one-way** copy of its data (departments, users matched to
employees, teams, projects and all other systems, files and approval history) so it can take over if Salesforce becomes unavailable.
**This system never writes to Salesforce**: the connector sends only read requests (and Bulk API 2.0 *query* jobs). Synced records are
**read-only** here while Salesforce is primary. After an outage, changes made here are re-entered in Salesforce by people, with the
**outage changes report**.

## 1. In Salesforce: certificate, Connected App and integration user

1. **Create a key pair and certificate** (on an administrator's computer, not on the server):
   ```bash
   openssl req -x509 -newkey rsa:2048 -sha256 -days 730 -nodes -keyout strategy-sync.key -out strategy-sync.crt -subj "/CN=Strategy standby sync"
   ```
   `strategy-sync.crt` goes to Salesforce; `strategy-sync.key` (the private key, PEM) goes to this system in step 2.4 and is then deleted
   from the computer. Keys smaller than 2048 bits are refused.
2. **Integration user**: Setup → Users → New user, licence *Salesforce Integration* (or a standard licence if your org does not have it),
   profile *Minimum Access – API Only Integrations*. Do not use a person's account.
3. **Read-only permission set** ("Strategy standby sync — read only") assigned to the integration user:
   - *API Enabled*, *View Setup and Configuration* is **not** needed.
   - Object permission **Read** (and *View All* so sharing does not hide records) on every mapped object: `Department__c`, `User`, `Contact`,
     `Project_Team__c`, `Project_Team_Association__c`, `PMS_Project_Request__c`, `PMS_Project__c`, `PMS_Work_Plan__c`, `PMS_Project_Kpi__c`,
     `PMS_Project_Risk__c`, `PMS_Project_Stakeholder__c`, `PMS_Project_Planning_Item__c`, `PMS_Challenges_Constraints__c`, `PMS_Change_Request__c`,
     `PMS_Contract__c`, `PMS_Lesson_Learned__c`, `PMS_Team_Meeting__c`, the KPI objects (`Org_Kpi_Goal__c`, `Org_Kpi_Measure__c`, `Org_Kpi_Process__c`),
     `Risk_Kpi_Relation__c` and the Risk, Audit, CAP, Benchmarking, Document Registry, IP and Tasks objects of your org.
   - Field-level security **Read** on all fields of those objects (otherwise the mapping editor reports them as unknown).
   - Files: *View All Data* is not required; give the user access to the libraries / records whose files must be copied.
   - Never grant *Create*, *Edit*, *Delete* or *Modify All*: the sync does not need them.
4. **Connected App**: Setup → App Manager → New Connected App: *Enable OAuth Settings*, callback URL `https://localhost/unused` (not used),
   **Use digital signatures** → upload `strategy-sync.crt`, OAuth scopes **Manage user data via APIs (api)** and **Perform requests at any time
   (refresh_token, offline_access)**. Save, then *Manage* → *Edit Policies*: **Permitted users: Admin approved users are pre-authorized**,
   and add the permission set (or the integration user's profile). Copy the **Consumer Key**.
5. Field history: if you want field history in the "Source history" panel, keep *Track Field History* on for the objects; give the integration
   user read access to the `…__History` objects.

## 2. In this system: connection

1. Settings → Integrations → **Salesforce sync** → **Connection**.
2. **Login URL**: `https://login.salesforce.com` (production), `https://test.salesforce.com` (sandbox) or your My Domain URL. HTTPS only.
3. **Consumer key** (from the Connected App), **Username** of the integration user, **API version** (default `v62.0`; keep it at a version your
   org supports).
4. **Private key**: paste `strategy-sync.key` (PEM). It is encrypted at rest and never shown again — only its **thumbprint**, which you can
   compare with the certificate. Replace it before the certificate expires (step 1.1 again, upload the new certificate to the Connected App first).
5. **Bulk threshold** (default 2 000): deltas bigger than this use Bulk API 2.0; smaller deltas use REST. Initial loads always use Bulk API 2.0.
6. **Mapping pack**: *ADoF Salesforce* (shipped).
7. **Test connection**: signs in with the JWT bearer flow, reads the API limits and the object list. Fix the message shown before you continue
   (e.g. *user hasn't approved this consumer* → step 1.4 policies).

## 3. Mappings (mapping editor)

The ADoF pack maps each Salesforce object to a record type here. Objects that the integration user could not see when the pack was written are
marked **provisional** and disabled.

1. **Mappings** tab → open an object. The left side shows the **live Salesforce field list** (types and picklist values); the right side the
   field mappings: source field → target field, kind (direct, picklist → code, reference → synced record, person → employee, record type,
   constant), value maps, transforms (trim, upper / lower case, percent → ratio, date only, HTML → text, truncate, Yes/No), required.
2. Also: defaults for empty fields, ignore list (fields deliberately not copied), filter (SOQL condition), external ID template (e.g.
   `{Id}-{year:End_Date__c}` for "one evaluation per year"), load order (parents before children).
3. **Validate**: checks every field and picklist value against the live describe. An object can be enabled only after it validates; any edit
   clears the confirmation. Warnings list new picklist values that have no code yet and custom fields that are neither mapped nor ignored.
4. **Export / Import** the pack (JSON) to keep a copy or move it between installations. Secrets and watermarks are never exported.
5. **People**: Salesforce users and contacts are **matched** to existing employees by e-mail, employee number or Federation ID (in that order).
   Load employees first (Organisation import). Unmatched people go to the exceptions queue.

## 4. First full load

1. **Activate** on the Status tab (needs a successful connection test). The mode becomes **Standby**.
2. **Full load**: reads every enabled object with Bulk API 2.0 in load order. Large orgs take a while; the Status tab shows progress per object.
3. Open **Exceptions** and fix what came up:
   - *Unmapped value* → **Map value** (adds the value → code to the field's value map) — the record is re-processed at once.
   - *Unknown user* → **Map person** (choose the employee) — then the records that referred to that person are re-processed.
   - *Missing parent* → usually fixes itself when the parent arrives; otherwise check the parent's mapping.
   - *Invalid value* / *Required missing* → correct the mapping (transform, default) and **Re-process**.
4. Re-running is safe: records are matched by their Salesforce Id; nothing is created twice.

## 5. Schedule

**Schedule** tab: every N hours (anchored at a time of day), daily at a time, or weekly on chosen days at a time — in the installation's time
zone; the next five runs are shown. *Late after* (minutes) decides when the sync counts as late. **Sync now** runs an incremental sync at once.
Incremental runs read only records changed since the last run (`SystemModstamp`, with a 5-minute overlap); records deleted in Salesforce are
marked **"Deleted in Salesforce"** here and never removed.

## 6. Monitoring

- **Status** tab: mode, last run (status, duration), next run, API usage against the daily limit, per-object counts (created, updated,
  unchanged, deleted, failed, conflicts), open exceptions.
- **Runs** tab: history and the log of each run.
- **E-mail alerts** (bilingual) to the holders of *Manage the sync*: when a run fails and when the sync is late (once per late period).
- **Health page** (Settings → Health → Integrations): *Salesforce sync* ok / late / failed / exceptions / this system primary.
- A run stops when less than 10 % of the daily API requests remain (setting).

## 7. While Salesforce is primary (standby)

- Synced records show **"Synced from Salesforce — read-only"**. Every change to them is refused on every channel (screens, REST API, MCP,
  imports, jobs) with a clear message; approvals cannot be submitted or decided for them.
- Jobs and notifications of the business systems are **suspended** (no duplicate reminders or e-mails); dashboards and reports keep working.
- The record page shows the Salesforce **approval history** and **field history** under *External references → Source history* (read-only).
- Records created here that do not come from Salesforce stay editable.

## 8. Failover runbook — "Make this system primary"

Use it only when Salesforce is unavailable for long enough that work must continue here.

1. Confirm the outage with the Salesforce administrator and agree the time of the switch. Tell users.
2. Settings → Integrations → Salesforce sync → **Failover** → **Make this system primary**: enter the **reason** and your **verification code**
   (authenticator app, or *send e-mail code*). You need the *Failover* permission (Integrations administrator, MFA required).
3. What happens: the sync stops; synced records unlock; workflows, approvals, jobs and notifications run normally; an **outage period** starts;
   records whose Salesforce approval was pending get **"Resubmit required"** — the owner submits them again here and the approvers decide here
   (the Salesforce approval history stays visible).
4. Work continues here. Every change is recorded in the audit trail and appears in the **outage changes report** (Failover tab), grouped by
   system and record, with old → new values and the Salesforce ID.

## 9. Return-to-standby runbook

When Salesforce is available again:

1. **Freeze** new work here for the re-entry (agree a time with the users).
2. Open the **outage changes report**, **export** it (Excel or PDF; Arabic or English) and re-enter the changes in Salesforce. Tick
   **Re-entered** per record as you go. Records created here have no Salesforce ID: create them in Salesforce.
3. **Return to standby** (the report must have been exported or acknowledged). The sync resumes and synced records become read-only again.
   Records changed here during the outage are protected: when Salesforce sends a newer version of such a record, it waits as a **conflict**.
4. **Conflicts**: for each record choose **Keep local** (the Salesforce version is ignored until the outage closes — use it when the change was
   not yet re-entered in Salesforce) or **Take Salesforce** (applied at once — use it when Salesforce now holds the correct data).
5. **Close outage** when no conflict is pending. From then on Salesforce wins again for every record.
6. If Salesforce fails again before you close the outage, **Make this system primary** again: the same outage continues.

## 10. Good to know

- The sync never writes to Salesforce; the integration user needs read rights only.
- The private key, the access tokens and the Salesforce data never appear in logs; every administrator action is in the audit trail
  (`integration.sync.*`), and every synced change is audited with the channel **sync**.
- Moving to another Salesforce org: export the pack, change the connection, validate every object, then run a full load.
