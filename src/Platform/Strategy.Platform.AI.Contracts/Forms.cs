namespace Strategy.Platform.AI;

/// <summary>Field types the extraction can fill. Values are coerced to the type server-side; the model is never trusted.</summary>
public static class AiFieldTypes
{
    public const string Text = "text";
    public const string LongText = "longText";
    public const string Number = "number";
    public const string Date = "date";
    public const string Choice = "choice";

    public static readonly IReadOnlyList<string> All = [Text, LongText, Number, Date, Choice];
}

/// <summary>A form field the AI may propose a value for.</summary>
/// <remarks><c>Name</c>: Field name the client form uses (e.g. <c>nameEn</c>).</remarks>
/// <remarks><c>Hint</c>: What belongs in the field, in English, for the prompt.</remarks>
/// <remarks><c>Choices</c>: Allowed codes for <see cref="AiFieldTypes.Choice"/>.</remarks>
public sealed record AiFormField(
    string Name, string Type, string LabelAr, string LabelEn, string? Hint = null, int? MaxLength = null, IReadOnlyList<string>? Choices = null);

/// <summary>
/// A form that can be filled from an uploaded document. <c>Permission</c> is the permission the user needs for the form itself
/// (e.g. <c>sample.item.create</c>): extraction never gives more than the form would.
/// </summary>
public sealed record AiFormDefinition(string Key, string SystemCode, string NameAr, string NameEn, string Permission, IReadOnlyList<AiFormField> Fields);
