using AsmResolver.DotNet;

namespace CompatUnbreaker.Utilities.AsmResolver;

internal readonly partial struct MemberClonerLite
{
    public PropertyDefinition CloneProperty(PropertyDefinition property, Dictionary<IMemberDescriptor, MethodDefinition>? clonedMethods)
    {
        var clonedProperty = new PropertyDefinition(
            property.Name,
            property.Attributes,
            property.Signature?.ImportWith(_importer)
        );

        if (clonedMethods != null) CloneSemantics(property, clonedProperty, clonedMethods);
        CloneCustomAttributes(property, clonedProperty);
        property.Constant = CloneConstant(property.Constant);

        return clonedProperty;
    }

    public EventDefinition CloneEvent(EventDefinition @event, Dictionary<IMemberDescriptor, MethodDefinition>? clonedMethods)
    {
        var clonedEvent = new EventDefinition(
            @event.Name,
            @event.Attributes,
            @event.EventType?.ImportWith(_importer)
        );

        if (clonedMethods != null) CloneSemantics(@event, clonedEvent, clonedMethods);
        CloneCustomAttributes(@event, clonedEvent);

        return clonedEvent;
    }

    private static void CloneSemantics(IHasSemantics semanticsProvider, IHasSemantics clonedProvider, Dictionary<IMemberDescriptor, MethodDefinition> clonedMethods)
    {
        foreach (var semantics in semanticsProvider.Semantics)
        {
            if (clonedMethods.TryGetValue(semantics.Method!, out var semanticMethod))
            {
                clonedProvider.Semantics.Add(new MethodSemantics(semanticMethod, semantics.Attributes));
            }
        }
    }
}
