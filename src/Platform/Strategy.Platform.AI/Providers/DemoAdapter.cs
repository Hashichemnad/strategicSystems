using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;
using Strategy.Platform.AI.Domain;

namespace Strategy.Platform.AI.Providers;

/// <summary>
/// An offline provider for development and demonstrations, registered only when <c>AI:EnableDemoProvider=true</c> (never in production
/// configuration). Deterministic: answers the connection test, fills form fields from "Label: value" lines of the document, writes a short
/// brief, marks translations, answers questions with one search, and drafts tasks from list lines (Phase 6).
/// </summary>
internal sealed class DemoAdapter : IAiProviderAdapter
{
    public string Type => AiProviderTypes.Demo;

    public bool RequiresEndpoint => false;

    public bool RequiresApiKey => false;

    public IChatClient Create(ProviderConnection connection) => new DemoChatClient(connection.Model);

    public Task<IReadOnlyList<string>> ListModelsAsync(ProviderConnection connection, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<string>>(["demo-small", "demo-large"]);
}

internal sealed partial class DemoChatClient(string model) : IChatClient
{
    private static readonly string[] CapWhysAr = ["لأن الإجراء غير موثق.", "لأنه لم يُحدَّد مالك للعملية.", "لأن الأدوار والمسؤوليات غير معتمدة."];

    private static readonly string[] CapWhysEn =
        ["Because the procedure is not documented.", "Because no process owner was named.", "Because roles and responsibilities are not approved."];

    private static readonly string[] BenchGaps = ["Demo: our process is manual where theirs is automated."];

    private static readonly char[] KeywordSeparators = [' ', '?', '،', ','];

    private static readonly string[] RiskIssues = ["Demo: name a specific cause and the effect on objectives."];

    private static readonly string[] KpiSuggestions = ["Name the data source.", "Set a baseline and a target for each year of the strategy cycle."];

    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var list = messages.ToList();
        var system = list.FirstOrDefault(m => m.Role == ChatRole.System)?.Text ?? string.Empty;
        var user = string.Join('\n', list.Where(m => m.Role == ChatRole.User).Select(m => m.Text));
        string text;
        if (system.Contains("You fill in the form", StringComparison.Ordinal))
        {
            text = Extract(system, user);
        }
        else if (system.Contains("You help an approver", StringComparison.Ordinal))
        {
            var arabic = system.Contains("Write in Arabic", StringComparison.Ordinal);
            var title = TitleLine().Match(user) is { Success: true } m ? m.Groups[1].Value.Trim() : "—";
            var changed = user.Contains("No field changed.", StringComparison.Ordinal) || user.Contains("First submission.", StringComparison.Ordinal) ? 0 : 1;
            text = JsonSerializer.Serialize(new
            {
                summary = arabic
                    ? $"ملخص تجريبي: يطلب السجل «{title}» الاعتماد. راجع البيانات والمرفقات قبل اتخاذ القرار."
                    : $"Demo brief: the record “{title}” is waiting for approval. Check its values and attachments before you decide.",
                changes = changed == 0 ? Array.Empty<string>() : [arabic ? "تم تعديل بعض الحقول منذ التقديم السابق." : "Some fields changed since the previous submission."],
                attentionPoints = new[] { arabic ? "تحقق من المبلغ المطلوب." : "Check the requested amount." },
            });
        }
        else if (system.Contains("You turn meeting notes into structured minutes", StringComparison.Ordinal))
        {
            text = DraftMinutes(user);
        }
        else if (system.Contains("You help an organisation form a team", StringComparison.Ordinal))
        {
            var document = Document().Match(user) is { Success: true } d ? d.Groups[1].Value.Trim() : string.Empty;
            text = JsonSerializer.Serialize(new
            {
                objectiveAr = $"تحقيق الغرض التالي: {document}",
                objectiveEn = $"Deliver the following purpose: {document}",
                mandateAr = "دراسة الوضع الحالي\nاقتراح الحلول والتوصيات\nمتابعة التنفيذ ورفع التقارير",
                mandateEn = "Study the current situation\nPropose solutions and recommendations\nFollow up implementation and report",
                memoAr = $"بناءً على الصلاحيات المخولة، يُشكَّل الفريق لتحقيق: {document}. يباشر الفريق مهامه من تاريخ القرار.",
                memoEn = $"Under the delegated authority, the team is formed to deliver: {document}. The team starts its work on the date of this decision.",
            });
        }
        else if (system.Contains("You help the evaluator of a team", StringComparison.Ordinal))
        {
            var arabic = system.Contains("Write in Arabic", StringComparison.Ordinal);
            text = JsonSerializer.Serialize(new
            {
                summary = arabic
                    ? "ملخص تجريبي: عقد الفريق اجتماعاته وأغلق معظم توصياته؛ المستويات المقترحة مبنية على بيانات السجل والمحاضر."
                    : "Demo summary: the team held its meetings and closed most of its recommendations; the suggested levels come from the register and the minutes.",
                points = new[]
                {
                    arabic ? "قارن المستوى المختار بالاقتراح وبرّر أي اختلاف." : "Compare each chosen level with the suggestion and justify any difference.",
                    arabic ? "أرفق الأدلة إذا بلغت الدرجة الحد المطلوب." : "Attach evidence when the score reaches the threshold.",
                },
            });
        }
        else if (system.Contains("You assist the project management office. Task: ", StringComparison.Ordinal))
        {
            text = ProjectAssist(system, user);
        }
        else if (system.Contains("You assess a program proposal", StringComparison.Ordinal))
        {
            text = AssessProposal(system);
        }
        else if (system.Contains("You turn meeting minutes", StringComparison.Ordinal))
        {
            text = DraftTasks(user);
        }
        else if (system.Contains("You help people working on a task", StringComparison.Ordinal))
        {
            var arabic = system.Contains("Write in Arabic", StringComparison.Ordinal);
            text = JsonSerializer.Serialize(new
            {
                text = arabic
                    ? "ملخص تجريبي: تقدم العمل على المهمة وفق التعليقات. النقاط المفتوحة: مراجعة المرفقات قبل التسليم."
                    : "Demo summary: work on the task is progressing as the comments describe. Open point: check the attachments before submitting.",
            });
        }
        else if (system.Contains("You check the evidence of a KPI result", StringComparison.Ordinal))
        {
            var arabic = system.Contains("Write in Arabic", StringComparison.Ordinal);
            var value = KpiValue().Match(system) is { Success: true } v ? v.Groups[1].Value.Trim() : string.Empty;
            var found = value.Length > 0 && user.Contains(value, StringComparison.Ordinal);
            text = JsonSerializer.Serialize(new
            {
                valueFound = found ? (bool?)true : null,
                periodMatches = (bool?)null,
                missing = found ? Array.Empty<string>() : [arabic ? "مصدر يبين القيمة المدخلة صراحة" : "A source that states the reported value explicitly"],
                summary = arabic
                    ? (found ? $"فحص تجريبي: القيمة {value} واردة في الدليل. تحقق من الفترة." : "فحص تجريبي: لم تظهر القيمة المدخلة في الدليل.")
                    : (found ? $"Demo check: the value {value} appears in the evidence. Check the period." : "Demo check: the reported value does not appear in the evidence."),
            });
        }
        else if (system.Contains("You help the owner of a KPI that missed its target", StringComparison.Ordinal))
        {
            var arabic = system.Contains("Write in Arabic", StringComparison.Ordinal);
            var document = Document().Match(user) is { Success: true } d ? d.Groups[1].Value.Trim() : string.Empty;
            text = JsonSerializer.Serialize(new
            {
                causality = arabic ? $"(مسودة) الأسباب وفق ملاحظات المسؤول: {document}" : $"(Draft) Reasons from the owner's notes: {document}",
                correctiveActions = arabic
                    ? "مراجعة خطة العمل مع الإدارة خلال أسبوعين\nمتابعة شهرية للمؤشر حتى العودة إلى المستهدف"
                    : "Review the work plan with the department within two weeks\nMonthly follow-up of the KPI until it is back on target",
                weak = document.Length < 40,
                weakReasons = document.Length < 40 ? new[] { arabic ? "التبرير عام ولا يفسر الفجوة" : "The justification is generic and does not explain the gap" } : Array.Empty<string>(),
            });
        }
        else if (system.Contains("You draft a KPI card", StringComparison.Ordinal))
        {
            var document = Document().Match(user) is { Success: true } d ? d.Groups[1].Value.Trim() : string.Empty;
            var title = document.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? "KPI";
            if (title.Length > 120)
            {
                title = title[..120];
            }

            var percent = document.Contains('%', StringComparison.Ordinal) || document.Contains("percent", StringComparison.OrdinalIgnoreCase)
                || document.Contains("نسبة", StringComparison.Ordinal);
            text = JsonSerializer.Serialize(new
            {
                nameAr = $"مؤشر: {title}",
                nameEn = title,
                descriptionAr = $"يقيس هذا المؤشر: {title}",
                descriptionEn = $"This KPI measures: {title}",
                measuringEquation = percent ? "(achieved ÷ planned) × 100" : "count of completed items",
                unitCode = percent ? "percentage" : "number",
                measuringPeriod = "quarterly",
                direction = "increase",
                targetType = "single",
                target = (decimal?)null,
                suggestions = KpiSuggestions,
            });
        }
        else if (system.Contains("You write the quarterly performance report", StringComparison.Ordinal))
        {
            var score = ScoreLine().Match(user) is { Success: true } m ? m.Groups[1].Value.Trim() : "-";
            text = JsonSerializer.Serialize(new
            {
                reportAr = $"تقرير تجريبي: بلغ الأداء العام {score}. تُعرض أبرز الأهداف والمؤشرات المتأخرة مع أسبابها والإجراءات التصحيحية.",
                reportEn = $"Demo report: overall performance reached {score}. The highlights and the KPIs below target are listed with their reasons and corrective actions.",
            });
        }
        else if (system.Contains("You help identify risks", StringComparison.Ordinal))
        {
            // Phase 13 (risk-identify): one deterministic suggestion built from the source text.
            var document = Document().Match(user) is { Success: true } d ? d.Groups[1].Value.Trim() : string.Empty;
            var subject = document.Length > 80 ? document[..80] : document;
            text = JsonSerializer.Serialize(new
            {
                risks = new[]
                {
                    new
                    {
                        titleAr = $"تعثر التنفيذ: {subject}", titleEn = $"Delivery slips: {subject}", cause = $"Dependencies named in the source: {subject}",
                        @event = "Key milestones are delivered late", consequence = "Objectives and services are delayed and costs rise", categoryCode = "operational",
                        likelihood = 3, impact = 4, reasoning = "Demo: the source mentions dependencies that could delay delivery.",
                    },
                },
            });
        }
        else if (system.Contains("You check the quality of a risk statement", StringComparison.Ordinal))
        {
            text = JsonSerializer.Serialize(new
            {
                issues = RiskIssues,
                cause = "A specific, verifiable cause",
                @event = "An uncertain event that may happen",
                consequence = "The effect on the organisation's objectives",
            });
        }
        else if (system.Contains("You suggest existing controls and treatment actions", StringComparison.Ordinal))
        {
            text = JsonSerializer.Serialize(new
            {
                controls = new[] { new { nameAr = "اعتماد مزدوج", nameEn = "Dual approval", typeCode = "preventive" } },
                actions = new[] { new { titleAr = "تدريب الموظفين المعنيين", titleEn = "Train the staff concerned" } },
            });
        }
        else if (system.Contains("You help a risk owner prepare the periodic review", StringComparison.Ordinal))
        {
            var arabic = system.Contains("Write in Arabic", StringComparison.Ordinal);
            text = JsonSerializer.Serialize(new
            {
                changes = new[] { arabic ? "تقدم إجراءات المعالجة منذ المراجعة السابقة." : "Treatment actions progressed since the last review." },
                commentary = arabic ? "(مسودة) تحسنت الضوابط ويُقترح إبقاء التقييم مع المتابعة." : "(Draft) Controls improved; keep the rating under follow-up.",
            });
        }
        else if (system.Contains("You write the quarterly enterprise risk report", StringComparison.Ordinal))
        {
            text = JsonSerializer.Serialize(new
            {
                reportAr = "(مسودة) ملف المخاطر المؤسسي لهذا الربع وفق الأرقام المرفقة.",
                reportEn = "(Draft) The enterprise risk profile this quarter, from the figures given.",
                top10Ar = "(مسودة) أهم المخاطر المؤسسية كما في القائمة.",
                top10En = "(Draft) The Top-10 institutional risks as listed.",
            });
        }
        else if (system.Contains("You extract improvement remarks from a report", StringComparison.Ordinal))
        {
            // Phase 15 (cap-extract): one remark per list line of the report (at most 5), else one remark from the text.
            var document = Document().Match(user) is { Success: true } d ? d.Groups[1].Value.Trim() : string.Empty;
            var lines = document.Split('\n').Select(l => ListItem().Match(l.Trim())).Where(m => m.Success).Select(m => m.Groups[1].Value.Trim()).Take(5).ToList();
            if (lines.Count == 0)
            {
                lines.Add(document.Length > 160 ? document[..160] : document);
            }

            text = JsonSerializer.Serialize(new
            {
                remarks = lines.Select(l => new { textAr = $"ملاحظة: {l}", textEn = l, criterionCode = "processes", typeCode = "improvement", priority = "medium", departmentCode = "" }),
            });
        }
        else if (system.Contains("You guide a remark owner through a root cause analysis", StringComparison.Ordinal))
        {
            var arabic = system.Contains("Write in Arabic", StringComparison.Ordinal);
            text = JsonSerializer.Serialize(new
            {
                whys = arabic ? CapWhysAr : CapWhysEn,
                causes = new[]
                {
                    new { categoryCode = "process", text = arabic ? "غياب إجراء موثق" : "No documented procedure" },
                    new { categoryCode = "people", text = arabic ? "عدم وضوح المسؤوليات" : "Unclear responsibilities" },
                },
                analysis = arabic ? "(مسودة) السبب الجذري الأرجح هو غياب مالك معتمد للعملية." : "(Draft) The most likely root cause is the absence of an approved process owner.",
            });
        }
        else if (system.Contains("You propose corrective actions for a remark", StringComparison.Ordinal))
        {
            text = JsonSerializer.Serialize(new
            {
                actions = new[]
                {
                    new { detailsAr = "توثيق الإجراء واعتماده ونشره على الموظفين المعنيين", detailsEn = "Document, approve and publish the procedure to the staff concerned", typeCode = "corrective", durationDays = 60 },
                    new { detailsAr = "تعيين مالك للعملية ومتابعة مؤشر شهري", detailsEn = "Name a process owner and follow a monthly indicator", typeCode = "preventive", durationDays = 30 },
                },
            });
        }
        else if (system.Contains("You review the quality of a corrective action", StringComparison.Ordinal))
        {
            var arabic = system.Contains("Write in Arabic", StringComparison.Ordinal);
            text = JsonSerializer.Serialize(new
            {
                issues = new[] { arabic ? "حدد مخرجاً قابلاً للقياس وتاريخاً واقعياً." : "Name a measurable output and a realistic date." },
                rewriteAr = "(مسودة) نشر الإجراء المعتمد وتدريب 100% من موظفي القسم قبل التاريخ المحدد",
                rewriteEn = "(Draft) Publish the approved procedure and train 100% of the section staff by the due date",
            });
        }
        else if (system.Contains("You write the monthly corrective action plan summary", StringComparison.Ordinal))
        {
            text = JsonSerializer.Serialize(new
            {
                summaryAr = "(مسودة) ملخص حالة الملاحظات والإجراءات لهذا الشهر وفق الأرقام المرفقة.",
                summaryEn = "(Draft) The status of remarks and actions this month, from the figures given.",
                lateUnitsAr = "(مسودة) لا توجد وحدات متأخرة بشكل مزمن.",
                lateUnitsEn = "(Draft) No unit is chronically late.",
            });
        }
        else if (system.Contains("You suggest comparator entities for a benchmarking study", StringComparison.Ordinal))
        {
            text = JsonSerializer.Serialize(new
            {
                entities = new[]
                {
                    new { nameAr = "هيئة حكومية رائدة", nameEn = "Leading government authority", country = "UAE", scope = "local", reason = "Demo: recognised for excellence in the topics." },
                    new { nameAr = "جهة دولية نظيرة", nameEn = "International peer agency", country = "Singapore", scope = "international", reason = "Demo: a known benchmark in the field." },
                },
            });
        }
        else if (system.Contains("You draft the pre-visit questionnaire", StringComparison.Ordinal))
        {
            text = JsonSerializer.Serialize(new
            {
                questionsAr = "1. كيف تنظمون العملية من البداية إلى النهاية؟\n2. ما المؤشرات التي تقيسون بها الأداء؟\n3. ما الدروس المستفادة؟",
                questionsEn = "1. How do you organise the process end to end?\n2. Which indicators do you use to measure performance?\n3. What lessons did you learn?",
            });
        }
        else if (system.Contains("You turn benchmarking visit notes into structured practice comparisons", StringComparison.Ordinal))
        {
            var document = Document().Matches(user).Select(m => m.Groups[1].Value.Trim()).LastOrDefault() ?? string.Empty;
            var subject = document.Length > 80 ? document[..80] : document;
            text = JsonSerializer.Serialize(new
            {
                practices = new[] { new { topic = "", subject = $"Practice: {subject}", ourPractice = "Manual, case by case", theirPractice = subject, category = "compared" } },
                gaps = BenchGaps,
                actions = new[] { new { titleAr = "أتمتة العملية وفق الممارسة المقارنة", titleEn = "Automate the process following the compared practice", priority = "high" } },
            });
        }
        else if (system.Contains("You summarise a benchmarking study for leadership", StringComparison.Ordinal))
        {
            text = JsonSerializer.Serialize(new
            {
                summaryAr = "(مسودة) ملخص الدراسة: الغرض والجهة المقارن بها وأبرز الممارسات والفجوات وتقدم الإجراءات.",
                summaryEn = "(Draft) Study summary: purpose, comparator, key practices, gaps and action progress.",
            });
        }
        else if (system.Contains("You turn a question about best practices into search keywords", StringComparison.Ordinal))
        {
            var document = Document().Match(user) is { Success: true } d ? d.Groups[1].Value.Trim() : string.Empty;
            var words = document.Split(KeywordSeparators, StringSplitOptions.RemoveEmptyEntries).Where(w => w.Length > 3).Take(4).ToArray();
            text = JsonSerializer.Serialize(new { keywords = words.Length > 0 ? words : [document] });
        }
        else if (system.Contains("You translate", StringComparison.Ordinal))
        {
            var toArabic = system.Contains("to Arabic", StringComparison.Ordinal);
            var source = Document().Match(user) is { Success: true } d ? d.Groups[1].Value.Trim() : string.Empty;
            text = JsonSerializer.Serialize(new { translation = (toArabic ? "(ترجمة تجريبية) " : "(demo translation) ") + source });
        }
        else if (options?.Tools is { Count: > 0 } && !list.Any(m => m.Contents.OfType<FunctionResultContent>().Any()))
        {
            var question = list.Last(m => m.Role == ChatRole.User).Text;
            var call = new FunctionCallContent("demo-1", "search_records", new Dictionary<string, object?> { ["query"] = LastWord(question) });
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, [call])) { ModelId = model, Usage = Usage(question, string.Empty) });
        }
        else
        {
            var results = list.SelectMany(m => m.Contents.OfType<FunctionResultContent>()).Select(r => r.Result?.ToString() ?? string.Empty).LastOrDefault() ?? string.Empty;
            var count = Regex.Count(results, "\"link\"", RegexOptions.None, TimeSpan.FromSeconds(1));
            text = system.Contains("Answer in Arabic", StringComparison.Ordinal)
                ? $"إجابة تجريبية: وجدت {count} من السجلات المطابقة."
                : $"Demo answer: I found {count} matching record(s).";
        }

        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, text)) { ModelId = model, Usage = Usage(system + user, text) });
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var response = await GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);
        foreach (var update in response.ToChatResponseUpdates())
        {
            yield return update;
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => serviceType.IsInstanceOfType(this) ? this : null;

    public void Dispose()
    {
    }

    /// <summary>"Label: value" lines whose label is a field's English or Arabic label (or name) become proposals quoting that line.</summary>
    private static string Extract(string system, string user)
    {
        var document = Document().Match(user) is { Success: true } d ? d.Groups[1].Value : string.Empty;
        var lines = document.Split('\n').Select(l => l.Trim()).Where(l => l.Contains(':', StringComparison.Ordinal)).ToList();
        var fields = new JsonArray();
        foreach (Match field in FieldLine().Matches(system))
        {
            var (name, labels) = (field.Groups[1].Value, field.Groups[2].Value.Split(" / ").Select(l => l.Split(" — ")[0].Trim()).Append(field.Groups[1].Value));
            // The label may follow scanner noise on an OCR line, so it is searched anywhere in the line.
            var match = lines.Select(l => (Line: l, At: labels.Select(label => (label, i: l.IndexOf(label + ":", StringComparison.OrdinalIgnoreCase)))
                    .Where(x => x.i >= 0).Select(x => x.i + x.label.Length + 1).DefaultIfEmpty(-1).First()))
                .FirstOrDefault(x => x.At >= 0);
            if (match.Line is null)
            {
                continue;
            }

            var line = match.Line;
            var value = line[match.At..].Trim();
            fields.Add(new JsonObject { ["name"] = name, ["value"] = value, ["confidence"] = 0.9, ["source"] = line.Length > 300 ? line[..300] : line });
        }

        var arabic = document.Count(c => c is >= '؀' and <= 'ۿ');
        return new JsonObject { ["language"] = arabic > document.Length / 4 ? "ar" : "en", ["fields"] = fields }.ToJsonString();
    }

    /// <summary>Phase 8: a fixed Proposal Assessment naming the first configured objective and scoring every criterion 4.</summary>
    private static string AssessProposal(string system)
    {
        var arabic = system.Contains("in Arabic", StringComparison.Ordinal);
        static IEnumerable<string> Codes(string system, string marker)
        {
            var start = system.IndexOf(marker, StringComparison.Ordinal);
            if (start < 0)
            {
                return [];
            }

            var line = system[(start + marker.Length)..].Split('\n')[0];
            return line.Split(';').Select(part => part.Split(" = ")[0].Trim()).Where(code => code.Length > 0 && !code.StartsWith('(')).ToList();
        }

        var objective = Codes(system, "Use only these objective codes:").FirstOrDefault();
        var scores = new JsonObject();
        foreach (var code in Codes(system, "for each of these criteria codes, with reasoning:"))
        {
            scores[code] = 4;
        }

        return JsonSerializer.Serialize(new
        {
            summary = arabic
                ? "ملخص تجريبي: يقترح الطلب برنامجاً لتحسين الخدمة بنطاق واضح وميزانية موزعة على السنوات."
                : "Demo summary: the request proposes a program that improves the service, with a clear scope and a budget spread over the years.",
            gaps = arabic
                ? new List<string> { "لا توجد مؤشرات أداء قابلة للقياس للفوائد المتوقعة.", "لم تُحدد المخاطر الرئيسية وخطط معالجتها." }
                : new List<string> { "No measurable indicators for the expected benefits.", "The main risks and their treatment are not described." },
            strengths = arabic ? new List<string> { "نطاق العمل واضح ومحدد." } : new List<string> { "The scope of work is clear and bounded." },
            risks = arabic ? new List<string> { "الاعتماد على مورد واحد." } : new List<string> { "Dependence on a single vendor." },
            alignment = new
            {
                objectives = objective is null ? Array.Empty<object>() : [new { code = objective, strength = "strong", reason = arabic ? "يدعم الهدف مباشرة." : "Supports the objective directly." }],
                summary = arabic ? "توافق جيد مع الأهداف الاستراتيجية." : "Good alignment with the strategic objectives.",
            },
            suggestedClassification = new
            {
                value = system.Contains("Currently computed: project", StringComparison.Ordinal) ? "project" : "initiative",
                reasoning = arabic ? "وفق قاعدة التصنيف المعتمدة لدى الجهة." : "Following the organisation's classification rule.",
            },
            suggestedPriority = new { scores, reasoning = arabic ? "أثر متوقع جيد مع تعقيد متوسط." : "Good expected impact with moderate complexity." },
            recommendations = arabic
                ? new List<string> { "حدد مؤشرات أداء ومستهدفات لكل فائدة.", "أضف سجلاً أولياً للمخاطر." }
                : new List<string> { "Define KPIs and targets for each benefit.", "Add an initial risk register." },
        });
    }

    /// <summary>Each line starting with "-", "*", "•" or "1." becomes a task; "@Name" names the assignee and "by YYYY-MM-DD" the due date.</summary>
    private static string DraftTasks(string user)
    {
        var document = Document().Match(user) is { Success: true } d ? d.Groups[1].Value : string.Empty;
        var tasks = new JsonArray();
        foreach (var raw in document.Split('\n').Select(l => l.Trim()))
        {
            var item = ListItem().Match(raw);
            if (!item.Success || tasks.Count >= 20)
            {
                continue;
            }

            var line = item.Groups[1].Value.Trim();
            var assignee = Mention().Match(line) is { Success: true } m ? m.Groups[1].Value.Replace('_', ' ') : null;
            var due = DueDate().Match(line) is { Success: true } date ? date.Groups[1].Value : null;
            var title = DueDate().Replace(Mention().Replace(line, string.Empty), string.Empty).Trim(' ', '.', ',', '،', '-');
            if (title.Length == 0)
            {
                continue;
            }

            tasks.Add(new JsonObject
            {
                ["title"] = title.Length > 120 ? title[..120] : title,
                ["description"] = null,
                ["assigneeName"] = assignee,
                ["dueDate"] = due,
                ["priority"] = line.Contains("urgent", StringComparison.OrdinalIgnoreCase) || line.Contains("عاجل", StringComparison.Ordinal) ? "high" : "medium",
                ["source"] = raw.Length > 300 ? raw[..300] : raw,
            });
        }

        return new JsonObject { ["tasks"] = tasks }.ToJsonString();
    }

    /// <summary>Notes → minutes (non-list lines), decisions ("Decision:" / "قرار:" lines) and recommendations (list lines with @Owner and "by YYYY-MM-DD").</summary>
    private static string DraftMinutes(string user)
    {
        var document = Document().Match(user) is { Success: true } d ? d.Groups[1].Value : string.Empty;
        var minutes = new List<string>();
        var decisions = new JsonArray();
        var recommendations = new JsonArray();
        foreach (var raw in document.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0))
        {
            if (raw.StartsWith("Decision:", StringComparison.OrdinalIgnoreCase) || raw.StartsWith("قرار:", StringComparison.Ordinal))
            {
                decisions.Add(raw[(raw.IndexOf(':', StringComparison.Ordinal) + 1)..].Trim());
            }
            else if (ListItem().Match(raw) is { Success: true } item)
            {
                var line = item.Groups[1].Value.Trim();
                recommendations.Add(new JsonObject
                {
                    ["text"] = DueDate().Replace(Mention().Replace(line, string.Empty), string.Empty).Trim(' ', '.', ',', '،', '-'),
                    ["ownerName"] = Mention().Match(line) is { Success: true } m ? m.Groups[1].Value.Replace('_', ' ') : null,
                    ["dueDate"] = DueDate().Match(line) is { Success: true } date ? date.Groups[1].Value : null,
                });
            }
            else
            {
                minutes.Add(raw);
            }
        }

        return new JsonObject { ["minutes"] = string.Join("\n", minutes), ["decisions"] = decisions, ["recommendations"] = recommendations }.ToJsonString();
    }

    private static string LastWord(string text) =>
        text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(w => w.Trim('?', '؟', '.', '!')).LastOrDefault(w => w.Length > 2) ?? text;

    /// <summary>Portfolio Phase 10 assists (charter, plan, update, status report, change impact, insights): deterministic drafts for development and E2E.</summary>
    private static string ProjectAssist(string system, string user)
    {
        var arabic = system.Contains("Write free text in Arabic", StringComparison.Ordinal);
        var name = user.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.StartsWith(arabic ? "Name (Arabic):" : "Name (English):", StringComparison.Ordinal))
            ?.Split(':', 2)[1].Trim() ?? "—";
        if (system.Contains("Task: charter-assistant", StringComparison.Ordinal))
        {
            return JsonSerializer.Serialize(new
            {
                kpis = new[] { new { nameAr = "نسبة إنجاز مخرجات المشروع في موعدها", nameEn = "Deliverables completed on time", equation = "on-time deliverables ÷ planned deliverables × 100", target = (decimal?)90 } },
                risks = new[]
                {
                    new { title = arabic ? "تأخر توريد المتطلبات من المورّد" : "Supplier delivers late", probability = 3, impact = 4, mitigation = arabic ? "متابعة أسبوعية مع المورّد" : "Weekly supplier follow-up" },
                    new { title = arabic ? "نقص الموارد المتخصصة" : "Shortage of specialised staff", probability = 2, impact = 3, mitigation = arabic ? "خطة بديلة للموارد" : "Backup resourcing plan" },
                },
                stakeholders = new[] { new { name = arabic ? "الإدارة المستفيدة" : "Beneficiary department", classification = "internal", interest = "high", impact = "high" } },
                planningItems = new[]
                {
                    new { category = "in-scope", description = arabic ? $"تنفيذ {name}" : $"Deliver {name}" },
                    new { category = "assumption", description = arabic ? "توفر الميزانية المعتمدة في موعدها" : "The approved budget is available on time" },
                },
            });
        }

        if (system.Contains("Task: plan-generator", StringComparison.Ordinal))
        {
            var agile = system.Contains("Methodology: agile", StringComparison.Ordinal);
            object[] milestones = agile ? [] :
            [
                new { phase = arabic ? "التحليل" : "Analysis", titleAr = "تحليل المتطلبات", titleEn = "Requirements analysis", weight = 15, durationDays = 30 },
                new { phase = arabic ? "التحليل" : "Analysis", titleAr = "التصميم", titleEn = "Design", weight = 15, durationDays = 30 },
                new { phase = arabic ? "التنفيذ" : "Delivery", titleAr = "البناء والتطوير", titleEn = "Build", weight = 40, durationDays = 75 },
                new { phase = arabic ? "التنفيذ" : "Delivery", titleAr = "الاختبار والقبول", titleEn = "Testing and acceptance", weight = 20, durationDays = 30 },
                new { phase = arabic ? "الإطلاق" : "Launch", titleAr = "الإطلاق والتسليم", titleEn = "Launch and handover", weight = 10, durationDays = 15 },
            ];
            object[] epics = !agile ? [] :
            [
                new
                {
                    titleAr = "بوابة الخدمات", titleEn = "Service portal", weight = 50,
                    stories = new[]
                    {
                        new { title = arabic ? "تقديم طلب إلكتروني" : "Submit a request online", asA = "customer", iWant = "to submit a request online", soThat = "I do not visit the office", points = 5 },
                        new { title = arabic ? "متابعة حالة الطلب" : "Track the request status", asA = "customer", iWant = "to see my request status", soThat = "I know what happens next", points = 3 },
                    },
                },
                new
                {
                    titleAr = "لوحة الموظف", titleEn = "Staff workspace", weight = 30,
                    stories = new[] { new { title = arabic ? "معالجة الطلبات" : "Process requests", asA = "employee", iWant = "a queue of requests", soThat = "I process them in order", points = 8 } },
                },
                new
                {
                    titleAr = "التقارير", titleEn = "Reports", weight = 20,
                    stories = new[] { new { title = arabic ? "تقرير شهري" : "Monthly report", asA = "manager", iWant = "a monthly report", soThat = "I follow performance", points = 3 } },
                },
            ];
            return JsonSerializer.Serialize(new { milestones, epics });
        }

        if (system.Contains("Task: update-assistant", StringComparison.Ordinal))
        {
            return JsonSerializer.Serialize(new
            {
                narrative = arabic
                    ? $"ملخص تجريبي: تقدّم العمل في «{name}» خلال الفترة وفق الخطة في معظم المراحل، مع متابعة المراحل المعرضة للتأخر."
                    : $"Demo narrative: work on “{name}” progressed mostly as planned this period; the milestones at risk are being followed up.",
                correctiveActions = new[] { arabic ? "إعادة جدولة الموارد للمراحل المتأخرة" : "Re-assign resources to the late milestones" },
            });
        }

        if (system.Contains("Task: status-report", StringComparison.Ordinal))
        {
            return JsonSerializer.Serialize(new
            {
                narrativeAr = $"تقرير تجريبي: يسير مشروع «{name}» وفق الخطة المعتمدة، مع متابعة المخاطر الرئيسية والإجراءات التصحيحية للمراحل المتأخرة.",
                narrativeEn = $"Demo report: “{name}” is progressing against the approved plan; the main risks and the corrective actions for late milestones are being followed up.",
            });
        }

        if (system.Contains("Task: vendor-review", StringComparison.Ordinal))
        {
            return JsonSerializer.Serialize(new
            {
                summary = arabic
                    ? "ملخص تجريبي: قدّم المورد تحديث المراحل المسندة إليه؛ راجع المؤشرات المعلّمة قبل القبول."
                    : "Demo summary: the vendor submitted the update of its assigned milestones; check the flagged items before accepting.",
                returnCommentAr = "يرجى استكمال الدليل للمراحل المكتملة وتوضيح أسباب التأخير والإجراءات التصحيحية ثم إعادة التقديم.",
                returnCommentEn = "Please add the evidence for the completed milestones, explain the causes of any delay with the corrective actions, and resubmit.",
            });
        }

        if (system.Contains("Task: change-impact", StringComparison.Ordinal))
        {
            return JsonSerializer.Serialize(new
            {
                summary = arabic ? "تحليل تجريبي: يؤثر التغيير على الجدول الزمني ويستلزم إعادة اعتماد خط الأساس." : "Demo analysis: the change affects the schedule and needs a re-baseline.",
                dates = arabic ? "يتأخر تاريخ الانتهاء وفق المقترح." : "The end date moves as proposed.",
                cost = arabic ? "لا تغيير على التكلفة ما لم تُعدَّل الميزانية." : "No cost effect unless the budget changes.",
                weights = arabic ? "تبقى الأوزان كما هي." : "Weights stay the same.",
                dependents = arabic ? "راجع المشاريع المعتمدة على هذا المشروع." : "Check the projects that depend on this one.",
                recommendation = new[] { arabic ? "تحقق من أثر التغيير على المشاريع المعتمدة." : "Check the effect on the dependent projects." },
            });
        }

        return JsonSerializer.Serialize(new
        {
            narrative = arabic
                ? "رؤى تجريبية: معظم المشاريع في المسار الصحيح، وتحتاج المشاريع المتأخرة إلى متابعة أسبوعية."
                : "Demo insights: most projects are on track; the delayed projects need weekly follow-up.",
            recommendations = new[] { arabic ? "مراجعة المشاريع الحمراء في اجتماع المحفظة القادم." : "Review the red projects at the next portfolio meeting." },
        });
    }

    private static UsageDetails Usage(string input, string output) =>
        new() { InputTokenCount = Math.Max(1, Encoding.UTF8.GetByteCount(input) / 4), OutputTokenCount = Math.Max(1, Encoding.UTF8.GetByteCount(output) / 4) };

    [GeneratedRegex(@"<untrusted_document[^>]*>\n?(.*?)\n?</untrusted_document", RegexOptions.Singleline)]
    private static partial Regex Document();

    [GeneratedRegex(@"^- (\w+) \([^)]*\): (.+)$", RegexOptions.Multiline)]
    private static partial Regex FieldLine();

    [GeneratedRegex(@"Record [^:]*: (.+)")]
    private static partial Regex TitleLine();

    [GeneratedRegex(@"^(?:[-*•]|\d+[.)])\s+(.+)$")]
    private static partial Regex ListItem();

    [GeneratedRegex(@"@([\p{L}_]+)")]
    private static partial Regex Mention();

    [GeneratedRegex(@"(?:by|قبل)\s+(\d{4}-\d{2}-\d{2})")]
    private static partial Regex DueDate();

    [GeneratedRegex(@"Reported value: ([^ \n]+)", RegexOptions.None, 1000)]
    private static partial Regex KpiValue();

    [GeneratedRegex(@"organisation: [^|]*\| score ([^|]+)\|", RegexOptions.None, 1000)]
    private static partial Regex ScoreLine();
}
