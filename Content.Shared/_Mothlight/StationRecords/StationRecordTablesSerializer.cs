using System.Globalization;
using Robust.Shared.Reflection;
using Robust.Shared.Serialization;
using Robust.Shared.Serialization.Manager;
using Robust.Shared.Serialization.Markdown;
using Robust.Shared.Serialization.Markdown.Mapping;
using Robust.Shared.Serialization.Markdown.Validation;
using Robust.Shared.Serialization.TypeSerializers.Interfaces;

namespace Content.Shared._Mothlight.StationRecords;

/// <summary>
/// Serializes a station record set's tables, which are keyed by the record's <see cref="Type"/>. Types can't be written
/// as YAML keys, so each table is written under its type's full name instead, which made saving any station with
/// records in it fail.
/// </summary>
public sealed class StationRecordTablesSerializer :
    ITypeSerializer<Dictionary<Type, Dictionary<uint, object>>, MappingDataNode>,
    ITypeCopyCreator<Dictionary<Type, Dictionary<uint, object>>>
{
    public ValidationNode Validate(ISerializationManager serializationManager,
        MappingDataNode node,
        IDependencyCollection dependencies,
        ISerializationContext? context = null)
    {
        var reflection = dependencies.Resolve<IReflectionManager>();
        foreach (var (typeName, _) in node)
        {
            if (reflection.GetType(typeName) == null)
                return new ErrorNode(node, $"Unknown station record type {typeName}");
        }

        return new ValidatedValueNode(node);
    }

    public Dictionary<Type, Dictionary<uint, object>> Read(ISerializationManager serializationManager,
        MappingDataNode node,
        IDependencyCollection dependencies,
        SerializationHookContext hookCtx,
        ISerializationContext? context = null,
        ISerializationManager.InstantiationDelegate<Dictionary<Type, Dictionary<uint, object>>>? instanceProvider = null)
    {
        var reflection = dependencies.Resolve<IReflectionManager>();
        var tables = instanceProvider?.Invoke() ?? new Dictionary<Type, Dictionary<uint, object>>();

        foreach (var (typeName, tableNode) in node)
        {
            // A record type that's since been removed, its records go with it.
            if (reflection.GetType(typeName) is not { } type || tableNode is not MappingDataNode table)
                continue;

            var records = new Dictionary<uint, object>();
            foreach (var (id, recordNode) in table)
            {
                if (serializationManager.Read(type, recordNode, context) is { } record)
                    records[uint.Parse(id, CultureInfo.InvariantCulture)] = record;
            }

            tables[type] = records;
        }

        return tables;
    }

    public DataNode Write(ISerializationManager serializationManager,
        Dictionary<Type, Dictionary<uint, object>> value,
        IDependencyCollection dependencies,
        bool alwaysWrite = false,
        ISerializationContext? context = null)
    {
        var node = new MappingDataNode();
        foreach (var (type, records) in value)
        {
            var table = new MappingDataNode();
            foreach (var (id, record) in records)
            {
                table.Add(id.ToString(CultureInfo.InvariantCulture),
                    serializationManager.WriteValue(type, record, alwaysWrite, context));
            }

            node.Add(type.FullName!, table);
        }

        return node;
    }

    public Dictionary<Type, Dictionary<uint, object>> CreateCopy(ISerializationManager serializationManager,
        Dictionary<Type, Dictionary<uint, object>> source,
        IDependencyCollection dependencies,
        SerializationHookContext hookCtx,
        ISerializationContext? context = null)
    {
        var copy = new Dictionary<Type, Dictionary<uint, object>>(source.Count);
        foreach (var (type, records) in source)
        {
            copy[type] = new Dictionary<uint, object>(records);
        }

        return copy;
    }
}
