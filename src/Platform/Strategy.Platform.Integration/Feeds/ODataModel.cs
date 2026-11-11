using Microsoft.OData.Edm;
using Strategy.Platform.Reporting;

namespace Strategy.Platform.Integration.Feeds;

/// <summary>
/// The EDM model of one system for one caller (ADR 0192): one entity type and set per readable record type, key <c>id</c>, every readable
/// field, a <c>…Label</c> string for each lookup / unit / employee field, navigation properties for the readable relationships.
/// </summary>
public sealed class ODataModel
{
    private readonly Dictionary<string, (VisibleRecordType Type, EdmEntityType Entity, EdmEntitySet Set)> _bySet = new(StringComparer.Ordinal);

    public ODataModel(string systemCode, IReadOnlyList<VisibleRecordType> types)
    {
        ArgumentNullException.ThrowIfNull(types);
        SystemCode = systemCode;
        var ns = "Strategy." + VisibleRecordType.TypeNameOf(systemCode);
        var model = new EdmModel();
        var container = new EdmEntityContainer(ns, "Container");
        model.AddElement(container);
        var entities = new Dictionary<string, (VisibleRecordType, EdmEntityType)>(StringComparer.Ordinal);
        foreach (var type in types)
        {
            var entity = new EdmEntityType(ns, type.TypeName);
            foreach (var field in type.AllFields)
            {
                var property = entity.AddStructuralProperty(field.Code, KindOf(field.Type), field.Code != RecordFields.Id);
                if (field.Code == RecordFields.Id)
                {
                    entity.AddKeys(property);
                }

                if (FeedLabels.HasLabel(field))
                {
                    entity.AddStructuralProperty(FeedLabels.LabelProperty(field.Code), EdmPrimitiveTypeKind.String, true);
                }
            }

            model.AddElement(entity);
            entities[type.Code] = (type, entity);
        }

        foreach (var (type, entity) in entities.Values)
        {
            _bySet[type.EntitySet] = (type, entity, container.AddEntitySet(type.EntitySet, entity));
        }

        foreach (var (type, entity) in entities.Values)
        {
            foreach (var relation in type.Relationships)
            {
                var target = entities[relation.TargetType];
                var navigation = entity.AddUnidirectionalNavigation(new EdmNavigationPropertyInfo
                {
                    Name = relation.Name,
                    Target = target.Item2,
                    TargetMultiplicity = relation.Many ? EdmMultiplicity.Many : EdmMultiplicity.ZeroOrOne,
                });
                _bySet[type.EntitySet].Set.AddNavigationTarget(navigation, _bySet[target.Item1.EntitySet].Set);
            }
        }

        Model = model;
    }

    public string SystemCode { get; }

    public IEdmModel Model { get; }

    public IEnumerable<VisibleRecordType> Types => _bySet.Values.Select(v => v.Type);

    public VisibleRecordType? FindSet(string entitySet) => _bySet.TryGetValue(entitySet, out var v) ? v.Type : null;

    public VisibleRecordType? FindTypeByName(string typeName) => _bySet.Values.FirstOrDefault(v => v.Entity.Name == typeName).Type;

    public static EdmPrimitiveTypeKind KindOf(RecordFieldType type) => type switch
    {
        RecordFieldType.Id or RecordFieldType.Unit or RecordFieldType.Employee => EdmPrimitiveTypeKind.Guid,
        RecordFieldType.Text or RecordFieldType.Lookup => EdmPrimitiveTypeKind.String,
        RecordFieldType.Number or RecordFieldType.Money or RecordFieldType.Ratio => EdmPrimitiveTypeKind.Decimal,
        RecordFieldType.Date => EdmPrimitiveTypeKind.Date,
        RecordFieldType.DateTime => EdmPrimitiveTypeKind.DateTimeOffset,
        RecordFieldType.Bool => EdmPrimitiveTypeKind.Boolean,
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };
}
