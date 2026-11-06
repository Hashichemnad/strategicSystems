<div dir="rtl">

# ربط Claude وعملاء MCP الآخرين

للمستخدمين ومسؤولي النظام. النسخة الإنجليزية: [`../en/mcp.md`](../en/mcp.md). يحل هذا الدليل محل [`docs/mcp-guide.md`](../../mcp-guide.md)
ويوسّعه (ويبقى ذلك الملف للمرجعية).

يتضمن نظام الاستراتيجية المتكامل **خادم بروتوكول سياق النموذج (MCP)**. يمكن لعميل MCP — مثل Claude Desktop أو claude.ai أو Claude Code أو أي
مساعد يدعم MCP — الاتصال بالنظام في جهتك واستخدام أدواته: موافقاتك المعلقة وإشعاراتك ومهامك، وحالة سجل ما، والبحث في السجلات التي يحق لك
رؤيتها، وملخصات الأنظمة، و(فقط عندما يسمح مسؤول النظام) المسودات.

**يعمل المساعد دائماً بصفة الشخص الذي ربطه.** فهو يرى ما تراه أنت في النظام بالضبط — ولا شيء أكثر — ويُسجَّل كل استدعاء في سجل التدقيق
(القناة `mcp`) مع اسمك والعميل والأداة. ولا يمكن لحسابات الموردين (واصل) استخدام MCP.

## 1. قبل البدء (مسؤول النظام)

1. **قدّم النظام عبر HTTPS** على عنوانه العام (مثل `https://strategy.example.gov.ae`). يتطلب تسجيل دخول عملاء MCP عبر OAuth بروتوكول HTTPS.
   وخلف الوكيل العكسي (Reverse Proxy) اجعل النظام يرى العنوان الأصلي (الترويسات المُمرّرة — دليل التثبيت / `deploy/README.md`).
2. **الإعدادات ← خادم MCP**: فعّل **تشغيل خادم MCP**؛ ودوّن **عنوان الخادم** (`https://<host>/mcp`)؛ وتحت **تسجيل دخول عملاء MCP ‏(OAuth 2.1)**
   أبقِ **السماح للعملاء بتسجيل أنفسهم** مفعّلاً وراجع **مضيفو إعادة التوجيه المسموح بهم** (افتراضياً: `localhost` و`127.0.0.1` لـ Claude Code
   والعملاء المحليين، و`claude.ai` و`claude.com` لـ claude.ai وClaude Desktop)؛ واختر مدد صلاحية الرموز (رمز الوصول 60 دقيقة، والبقاء متصلاً 30
   يوماً) و**استدعاءات الأدوات لكل مستخدم في الدقيقة**.
3. راجع جدول **الأدوات**. أدوات القراءة مفعّلة؛ و**أدوات الكتابة موقوفة** حتى تفعّلها؛ و**أدوات الاعتماد** (`approval_decide` و`approve_task`) لا
   تُفعّل افتراضياً أبداً — فعّلها فقط إذا كانت سياستك تسمح للأشخاص بتسجيل قراراتهم بأنفسهم من عميل ذكاء اصطناعي.
4. اختيارياً: **الإعدادات ← الذكاء الاصطناعي ← مشاركة البيانات والحدود** — الأنظمة والحقول التي لا تُرسل أبداً إلى الذكاء الاصطناعي؛ وتنطبق هذه
   الاستثناءات أيضاً على أداة `get_record`.

![إعدادات MCP](../../screenshots/phase-5/ar-mcp-settings.png)

## 2. OAuth أم رمز وصول شخصي؟

| | OAuth 2.1 (موصى به) | رمز وصول شخصي |
|---|---|---|
| الطريقة | يفتح العميل صفحة الدخول إلى النظام (مع التحقق بخطوتين) ويطلب منك **السماح** له | **ملفي الشخصي ← رموز الوصول ← رمز جديد** بصلاحية **مساعدات الذكاء الاصطناعي (MCP)**؛ ثم الصقه في العميل كـ `Authorization: Bearer sst_…` |
| المدة | رمز الوصول 60 دقيقة، ويُجدَّد تلقائياً حتى 30 يوماً | الأيام التي تختارها (90 افتراضياً، و365 كحد أقصى) |
| الإبطال | مسؤول النظام: الإعدادات ← خادم MCP ← العملاء المتصلون ← **قطع الاتصال** | أنت: **إبطال** من ملفك الشخصي |
| أين يعمل | كل عميل يدعم اكتشاف OAuth في MCP | العملاء الذين يسمحون بترويسة مخصصة (مفيد حين لا يتوفر OAuth) |

رموز OAuth الصادرة لعملاء MCP لا تعمل إلا على `/mcp`، ولا تعمل أبداً على واجهة REST البرمجية. ويعمل الرمز ذو صلاحية MCP على `/mcp`.

![الموافقة](../../screenshots/phase-5/ar-mcp-consent.png)

## 3. Claude Code

يعمل Claude Code على جهازك، لذا يعمل أيضاً مع الأنظمة المتاحة داخل شبكة جهتك فقط.

```bash
claude mcp add --transport http strategy https://strategy.example.gov.ae/mcp
```

شغّل `/mcp` داخل Claude Code، واختر **strategy** ← **Authenticate**. يفتح المتصفح صفحة الدخول إلى النظام، ثم يسألك هل تسمح لـ Claude Code باستخدام
حسابك؛ اختر **السماح** وعُد إلى Claude Code.

باستخدام رمز وصول شخصي بدلاً من المتصفح:

```bash
claude mcp add --transport http strategy https://strategy.example.gov.ae/mcp --header "Authorization: Bearer sst_xxxxxxxx_xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx"
```

حافظ على سرية الرمز وأبطله عندما لا تعود بحاجة إليه.

## 4. Claude Desktop وclaude.ai

**Settings ← Connectors ← Add custom connector** (في خطط Team وEnterprise قد يلزم أن يسمح مالك الحساب بالموصلات المخصصة أولاً). أدخل اسماً (مثل
*Strategy*) وعنوان الخادم `https://strategy.example.gov.ae/mcp`، ثم **Connect**: فتسجّل الدخول إلى النظام وتسمح بالاتصال.

> **ملاحظة عن الشبكة.** الموصلات المضافة في claude.ai أو Claude Desktop يُتصل بها **من سحابة Anthropic**، لذا يجب أن تكون العناوين `/mcp` و`/connect/*`
> و`/.well-known/*` في نظامك متاحة من الإنترنت عبر HTTPS. وإذا كان النظام داخلياً فقط فاستخدم Claude Code أو عميلاً آخر يعمل على جهاز المستخدم.

## 5. عملاء MCP الآخرون

| الإعداد | القيمة |
|---|---|
| عنوان الخادم | `https://<host>/mcp` ‏(Streamable HTTP) |
| المصادقة | OAuth 2.1 يُكتشف من الخادم، أو `Authorization: Bearer sst_…` (بصلاحية MCP) |
| البيانات الوصفية للمورد المحمي | `https://<host>/.well-known/oauth-protected-resource/mcp` |
| البيانات الوصفية لخادم التفويض | `https://<host>/.well-known/oauth-authorization-server` (وأيضاً `/.well-known/openid-configuration`) |
| تسجيل العميل | ديناميكي (RFC 7591) على `https://<host>/connect/register`؛ عملاء عامون مع PKCE ‏(S256)؛ وعنوان إعادة التوجيه `https`، أو `http` على `localhost` / `127.0.0.1`، على مضيف مسموح |
| النطاق | `mcp` (ويُصدر معه رمز تحديث) |

## 6. الأدوات (الإصدار 1.0)

تعرض الإعدادات ← خادم MCP القائمة المعتمدة؛ والنظام الموقوف يخفي أدواته.

| النظام | أدوات القراءة (مفعّلة افتراضياً) | أدوات الكتابة (موقوفة افتراضياً) |
|---|---|---|
| المنصة | `whoami`، `my_pending_approvals`، `my_notifications`، `record_status`، `search_records`، `get_record` | — (الاعتماد: `approval_decide`، لا يُفعّل افتراضياً أبداً) |
| المحافظ والمشاريع | `portfolio_summary`، `portfolio_my_projects`، `portfolio_get_project`، `portfolio_project_milestones`، `portfolio_my_requests`، `portfolio_search_requests`، `portfolio_get_request`، `portfolio_project_backlog`، `portfolio_sprint_summary` | `portfolio_create_draft_request`، `portfolio_update_milestone_draft`، `portfolio_create_story_draft` |
| إدارة الأداء | `my_pending_kpi_results`، `kpi_card_lookup`، `kpi_approval_queue_summary`، `department_scorecard` | `submit_kpi_result_draft` |
| إدارة المخاطر | `risk_register_search`، `risk_details`، `my_risk_actions`، `risk_heatmap_summary`، `risk_top10`، `risks_outside_appetite` | `update_risk_action_progress_draft` |
| الفرق | `my_teams`، `team_details`، `my_pending_evaluations`، `evaluation_status` | `add_meeting_minutes` |
| المهام | `my_tasks` | `create_task`، `update_task_status`، `add_task_comment` (الاعتماد: `approve_task`، لا يُفعّل افتراضياً أبداً) |
| الطلبات / الوثائق / الملكية الفكرية | `my_requests`، `request_status`، `search_documents`، `get_document`، `document_expiry_list`، `search_ip_records`، `get_ip_record` | `submit_request_draft` |
| المقارنات المرجعية | `bench_studies`، `bench_study_details`، `bench_best_practices` | — |
| خطة العمل التصحيحية | `cap_my_remarks`، `cap_my_actions`، `cap_overdue_actions`، `cap_remarks_by_source` | `cap_update_achievement_draft` |
| إدارة التدقيق | `audit_my_finding_status`، `audit_findings_by_department`، `audit_finding_details`، `audit_overdue_findings`، `audit_followups_due` | `audit_record_follow_up_draft` |

تُنشئ أدوات الكتابة **مسودات** يؤكدها الشخص داخل النظام. أمثلة على ما يمكنك طلبه بعد الاتصال: *«ما الموافقات التي تنتظرني؟»*، *«أي نتائج مؤشراتي
تستحق هذا الشهر؟»*، *«لخّص أعلى عشرة مخاطر»*، *«أي مشاريعي متأخرة؟»*.

## 7. الأمان والتحكم

- **الصلاحيات نفسها كما في النظام** — تمر كل أداة بفحوص الصلاحيات نفسها التي تمر بها الشاشات؛ ويُجاب عن السجل خارج نطاقك بـ*غير موجود*. وتنطبق هنا
  أيضاً قواعد المهام السرية ودرجات سرية الوثائق وإخفاء أرقام الهوية الإماراتية وحقول التكاليف المخفية.
- **الموافقة** عند أول اتصال لكل عميل؛ ويمكن لمسؤول النظام **قطع الاتصال** بعميل (فتتوقف رموزه للجميع).
- **الرموز** قصيرة الأجل؛ ويفقد الحساب المعطل أو المقفل الوصول فوراً.
- **الحدود** لكل مستخدم في الدقيقة (لكل الأدوات، ولكل أداة إن ضُبط ذلك) ← `rate_limited`.
- **التدقيق**: كل استدعاء، بما فيه المرفوض، مع الشخص والعميل (معرّف عميل OAuth أو بادئة الرمز) والأداة والمعاملات مختصرة والنتيجة؛ وكذلك تسجيلات
  العملاء والموافقات وقطع الاتصال.
- **الذكاء الاصطناعي لا يقرر** — لا يعتمد الخادم شيئاً من تلقاء نفسه؛ وأداة الاعتماد لا تسجل إلا قراراً طلبه الشخص، بحسابه هو، وفقط عندما يفعّلها مسؤول
  النظام.

## 8. حل المشكلات

| العَرَض | السبب والحل |
|---|---|
| `404` على `/mcp` | خادم MCP موقوف: الإعدادات ← خادم MCP ← **تشغيل خادم MCP** |
| `401` ولا يبدأ العميل تسجيل الدخول | العميل لا يدعم اكتشاف OAuth: استخدم رمز وصول شخصياً بصلاحية MCP |
| `403 auth.tokenScope` | الرمز يفتقر إلى صلاحية **مساعدات الذكاء الاصطناعي (MCP)**: أنشئ رمزاً جديداً |
| `403` لحساب مورد | لا يمكن لحسابات الموردين (واصل) استخدام MCP |
| صفحة الدخول تقول إن الطلب غير صالح | مضيف إعادة التوجيه غير مسموح (مضيفو إعادة التوجيه المسموح بهم) أو حُذف تسجيل العميل: أعد الاتصال |
| خطأ OAuth يتعلق بـ HTTPS، أو عناوين `http://` في البيانات الوصفية | النظام لا يرى عنوان HTTPS الأصلي: فعّل الترويسات المُمرّرة خلف الوكيل |
| أداة تجيب `disabled` | أوقفها مسؤول النظام، أو أن نظامها موقوف |
| أداة تجيب `not_found` | السجل غير موجود أو خارج نطاق صلاحياتك |

</div>
