// Copyright (c) Microsoft Corporation.  All rights reserved.
// Licensed under the MIT License.  See License.txt in the project root for license information.

using Microsoft.AspNetCore.OData.Builder;
using Microsoft.AspNetCore.OData.Common;
using Microsoft.AspNetCore.OData.Extensions;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing;
using Microsoft.Net.Http.Headers;
using Microsoft.OData.Core;
using Microsoft.OData.Core.UriParser.Semantic;
using Microsoft.OData.Edm;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Contracts;
using System.Linq;
using System.Runtime.Serialization;

namespace Microsoft.AspNetCore.OData.Formatter.Serialization
{
    /// <summary>
    /// ODataSerializer for serializing instances of <see cref="IEdmEntityType"/>
    /// </summary>
    public class ODataEntityTypeSerializer : ODataEdmTypeSerializer
    {
        private const string Entry = "entry";

        /// <inheritdoc />
        public ODataEntityTypeSerializer(ODataSerializerProvider serializerProvider)
            : base(ODataPayloadKind.Entry, serializerProvider)
        {
        }

        /// <inheritdoc />
        public override void WriteObject(object graph, Type type, ODataMessageWriter messageWriter,
            ODataSerializerContext writeContext)
        {
            if (messageWriter == null)
            {
                throw Error.ArgumentNull("messageWriter");
            }

            if (writeContext == null)
            {
                throw Error.ArgumentNull("writeContext");
            }

            var navigationSource = writeContext.NavigationSource;
            if (navigationSource == null)
            {
                throw new SerializationException(SRResources.NavigationSourceMissingDuringSerialization);
            }

            var path = writeContext.Path;
            if (path == null)
            {
                throw new SerializationException(SRResources.ODataPathMissing);
            }

            var writer = messageWriter.CreateODataEntryWriter(navigationSource, path.EdmType as IEdmEntityType);
            WriteObjectInline(graph, navigationSource.EntityType().ToEdmTypeReference(isNullable: false), writer, writeContext);
        }

        /// <inheritdoc />
        public override void WriteObjectInline(object graph, IEdmTypeReference expectedType, ODataWriter writer,
            ODataSerializerContext writeContext)
        {
            if (writer == null)
            {
                throw Error.ArgumentNull("writer");
            }

            if (writeContext == null)
            {
                throw Error.ArgumentNull("writeContext");
            }

            if (graph == null)
            {
                throw new SerializationException(Error.Format(SRResources.CannotSerializerNull, Entry));
            }
            else
            {
                WriteEntry(graph, writer, writeContext);
            }
        }

        /// <summary>
        /// Writes the given object specified by the parameter graph as a part of an existing OData message using the given
        /// deltaWriter and the writeContext.
        /// </summary>
        /// <param name="graph">The object to be written.</param>
        /// <param name="expectedType">The expected EDM type of the object represented by <paramref name="graph"/>.</param>
        /// <param name="writer">The <see cref="ODataDeltaWriter" /> to be used for writing.</param>
        /// <param name="writeContext">The <see cref="ODataSerializerContext"/>.</param>
        public virtual void WriteDeltaObjectInline(object graph, IEdmTypeReference expectedType, ODataDeltaWriter writer,
           ODataSerializerContext writeContext)
        {
            if (writer == null)
            {
                throw Error.ArgumentNull("writer");
            }

            if (writeContext == null)
            {
                throw Error.ArgumentNull("writeContext");
            }

            if (graph == null)
            {
                throw new SerializationException(Error.Format(SRResources.CannotSerializerNull, Entry));
            }
            else
            {
                WriteDeltaEntry(graph, writer, writeContext);
            }
        }

        private void WriteDeltaEntry(object graph, ODataDeltaWriter writer, ODataSerializerContext writeContext)
        {
            Contract.Assert(writeContext != null);

            var entityType = GetEntityType(graph, writeContext);
            var entityInstanceContext = new EntityInstanceContext(writeContext, entityType, graph);
            var selectExpandNode = CreateSelectExpandNode(entityInstanceContext);
            if (selectExpandNode != null)
            {
                var entry = CreateEntry(selectExpandNode, entityInstanceContext);
                if (entry != null)
                {
                    writer.WriteStart(entry);
                    //TODO: Need to add support to write Navigation Links using Delta Writer
                    //https://github.com/OData/odata.net/issues/155
                    writer.WriteEnd();
                }
            }
        }

        private void WriteEntry(object graph, ODataWriter writer, ODataSerializerContext writeContext)
        {
            Contract.Assert(writeContext != null);

            var entityType = GetEntityType(graph, writeContext);
            var entityInstanceContext = new EntityInstanceContext(writeContext, entityType, graph);
            var selectExpandNode = CreateSelectExpandNode(entityInstanceContext);
            if (selectExpandNode != null)
            {
                var entry = CreateEntry(selectExpandNode, entityInstanceContext);
                if (entry != null)
                {
                    writer.WriteStart(entry);
                    WriteNavigationLinks(selectExpandNode.SelectedNavigationProperties, entityInstanceContext, writer);
                    WriteExpandedNavigationProperties(selectExpandNode.ExpandedNavigationProperties, entityInstanceContext, writer);
                    writer.WriteEnd();
                }
            }
        }

        /// <summary>
        /// Creates the <see cref="SelectExpandNode"/> that describes the set of properties and actions to select and expand while writing this entity.
        /// </summary>
        /// <param name="entityInstanceContext">Contains the entity instance being written and the context.</param>
        /// <returns>
        /// The <see cref="SelectExpandNode"/> that describes the set of properties and actions to select and expand while writing this entity.
        /// </returns>
        public virtual SelectExpandNode CreateSelectExpandNode(EntityInstanceContext entityInstanceContext)
        {
            if (entityInstanceContext == null)
            {
                throw Error.ArgumentNull("entityInstanceContext");
            }

            var writeContext = entityInstanceContext.SerializerContext;
            var entityType = entityInstanceContext.EntityType;

            object selectExpandNode;
            var key = Tuple.Create(writeContext.SelectExpandClause, entityType);
            if (!writeContext.Items.TryGetValue(key, out selectExpandNode))
            {
                // cache the selectExpandNode so that if we are writing a feed we don't have to construct it again.
                selectExpandNode = new SelectExpandNode(writeContext.SelectExpandClause, entityType, writeContext.Model);
                writeContext.Items[key] = selectExpandNode;
            }
            return selectExpandNode as SelectExpandNode;
        }

        /// <summary>
        /// Creates the <see cref="ODataEntry"/> to be written while writing this entity.
        /// </summary>
        /// <param name="selectExpandNode">The <see cref="SelectExpandNode"/> describing the response graph.</param>
        /// <param name="entityInstanceContext">The context for the entity instance being written.</param>
        /// <returns>The created <see cref="ODataEntry"/>.</returns>
        public virtual ODataEntry CreateEntry(SelectExpandNode selectExpandNode, EntityInstanceContext entityInstanceContext)
        {
            if (selectExpandNode == null)
            {
                throw Error.ArgumentNull("selectExpandNode");
            }
            if (entityInstanceContext == null)
            {
                throw Error.ArgumentNull("entityInstanceContext");
            }

            var typeName = entityInstanceContext.EntityType.FullName();

            var entry = new ODataEntry
            {
                TypeName = typeName,
                Properties = CreateStructuralPropertyBag(selectExpandNode.SelectedStructuralProperties, entityInstanceContext),
            };

            // Try to add the dynamic properties if the entity type is open.
            if ((entityInstanceContext.EntityType.IsOpen && selectExpandNode.SelectAllDynamicProperties) ||
                (entityInstanceContext.EntityType.IsOpen && selectExpandNode.SelectedDynamicProperties.Any()))
            {
                var entityTypeReference =
                    entityInstanceContext.EntityType.ToEdmTypeReference(isNullable: false);
                var dynamicProperties = AppendDynamicProperties(entityInstanceContext.EdmObject,
                    (IEdmStructuredTypeReference)entityTypeReference,
                    entityInstanceContext.SerializerContext,
                    entry.Properties.ToList(),
                    selectExpandNode.SelectedDynamicProperties.ToArray());

                if (dynamicProperties != null)
                {
                    entry.Properties = entry.Properties.Concat(dynamicProperties);
                }
            }

            var actions = CreateODataActions(selectExpandNode.SelectedActions, entityInstanceContext);
            foreach (var action in actions)
            {
                entry.AddAction(action);
            }

            var pathType = GetODataPathType(entityInstanceContext.SerializerContext);
            AddTypeNameAnnotationAsNeeded(entry, pathType, entityInstanceContext.SerializerContext.MetadataLevel);

            if (entityInstanceContext.NavigationSource != null)
            {
                if (!(entityInstanceContext.NavigationSource is IEdmContainedEntitySet))
                {
                    var model = entityInstanceContext.SerializerContext.Model;
                    var linkBuilder = model.GetNavigationSourceLinkBuilder(entityInstanceContext.NavigationSource);
                    var selfLinks = linkBuilder.BuildEntitySelfLinks(entityInstanceContext, entityInstanceContext.SerializerContext.MetadataLevel);

                    if (selfLinks.IdLink != null)
                    {
                        entry.Id = selfLinks.IdLink;
                    }

                    if (selfLinks.ReadLink != null)
                    {
                        entry.ReadLink = selfLinks.ReadLink;
                    }

                    if (selfLinks.EditLink != null)
                    {
                        entry.EditLink = selfLinks.EditLink;
                    }
                }

                var etag = CreateETag(entityInstanceContext);
                if (etag != null)
                {
                    entry.ETag = etag;
                }
            }

            return entry;
        }

        /// <summary>
        /// Creates the ETag for the given entity.
        /// </summary>
        /// <param name="entityInstanceContext">The context for the entity instance being written.</param>
        /// <returns>The created ETag.</returns>
        public virtual string CreateETag(EntityInstanceContext entityInstanceContext)
        {
            if (entityInstanceContext.Request != null)
            {
                IEnumerable<IEdmStructuralProperty> concurrencyProperties =
                    entityInstanceContext.EntityType.GetConcurrencyProperties().OrderBy(c => c.Name);

                IDictionary<string, object> properties = new Dictionary<string, object>();
                foreach (var etagProperty in concurrencyProperties)
                {
                    properties.Add(etagProperty.Name, entityInstanceContext.GetPropertyValue(etagProperty.Name));
                }
                var etagHeaderValue = entityInstanceContext.Request.ETagHandler().CreateETag(properties);
                if (etagHeaderValue != null)
                {
                    return etagHeaderValue.ToString();
                }
            }

            return null;
        }

        private void WriteNavigationLinks(
            IEnumerable<IEdmNavigationProperty> navigationProperties, EntityInstanceContext entityInstanceContext, ODataWriter writer)
        {
            Contract.Assert(entityInstanceContext != null);

            var navigationLinks = CreateNavigationLinks(navigationProperties, entityInstanceContext);
            foreach (var navigationLink in navigationLinks)
            {
                writer.WriteStart(navigationLink);
                writer.WriteEnd();
            }
        }

        private void WriteExpandedNavigationProperties(
            IDictionary<IEdmNavigationProperty, SelectExpandClause> navigationPropertiesToExpand,
            EntityInstanceContext entityInstanceContext,
            ODataWriter writer)
        {
            Contract.Assert(navigationPropertiesToExpand != null);
            Contract.Assert(entityInstanceContext != null);
            Contract.Assert(writer != null);

            foreach (var navigationPropertyToExpand in navigationPropertiesToExpand)
            {
                var navigationProperty = navigationPropertyToExpand.Key;

                var navigationLink = CreateNavigationLink(navigationProperty, entityInstanceContext);
                if (navigationLink != null)
                {
                    writer.WriteStart(navigationLink);
                    WriteExpandedNavigationProperty(navigationPropertyToExpand, entityInstanceContext, writer);
                    writer.WriteEnd();
                }
            }
        }

        private void WriteExpandedNavigationProperty(
            KeyValuePair<IEdmNavigationProperty, SelectExpandClause> navigationPropertyToExpand,
            EntityInstanceContext entityInstanceContext,
            ODataWriter writer)
        {
            Contract.Assert(entityInstanceContext != null);
            Contract.Assert(writer != null);

            var navigationProperty = navigationPropertyToExpand.Key;
            var selectExpandClause = navigationPropertyToExpand.Value;

            var propertyValue = entityInstanceContext.GetPropertyValue(navigationProperty.Name);

            if (propertyValue == null)
            {
                if (navigationProperty.Type.IsCollection())
                {
                    // A navigation property whose Type attribute specifies a collection, the collection always exists,
                    // it may just be empty.
                    // If a collection of entities can be related, it is represented as a JSON array. An empty
                    // collection of entities (one that contains no entities) is represented as an empty JSON array.
                    writer.WriteStart(new ODataFeed());
                }
                else
                {
                    // If at most one entity can be related, the value is null if no entity is currently related.
                    writer.WriteStart(entry: null);
                }

                writer.WriteEnd();
            }
            else
            {
                // create the serializer context for the expanded item.
                var nestedWriteContext = new ODataSerializerContext(entityInstanceContext, selectExpandClause, navigationProperty);

                // write object.
                var serializer = SerializerProvider.GetEdmTypeSerializer(navigationProperty.Type);
                if (serializer == null)
                {
                    throw new SerializationException(
                        Error.Format(SRResources.TypeCannotBeSerialized, navigationProperty.Type.ToTraceString(), typeof(ODataOutputFormatter).Name));
                }

                serializer.WriteObjectInline(propertyValue, navigationProperty.Type, writer, nestedWriteContext);
            }
        }

        private IEnumerable<ODataNavigationLink> CreateNavigationLinks(
            IEnumerable<IEdmNavigationProperty> navigationProperties, EntityInstanceContext entityInstanceContext)
        {
            Contract.Assert(navigationProperties != null);
            Contract.Assert(entityInstanceContext != null);

            foreach (var navProperty in navigationProperties)
            {
                var navigationLink = CreateNavigationLink(navProperty, entityInstanceContext);
                if (navigationLink != null)
                {
                    yield return navigationLink;
                }
            }
        }

        /// <summary>
        /// Creates the <see cref="ODataNavigationLink"/> to be written while writing this entity.
        /// </summary>
        /// <param name="navigationProperty">The navigation property for which the navigation link is being created.</param>
        /// <param name="entityInstanceContext">The context for the entity instance being written.</param>
        /// <returns>The navigation link to be written.</returns>
        public virtual ODataNavigationLink CreateNavigationLink(IEdmNavigationProperty navigationProperty, EntityInstanceContext entityInstanceContext)
        {
            if (navigationProperty == null)
            {
                throw Error.ArgumentNull("navigationProperty");
            }
            if (entityInstanceContext == null)
            {
                throw Error.ArgumentNull("entityInstanceContext");
            }

            var writeContext = entityInstanceContext.SerializerContext;
            ODataNavigationLink navigationLink = null;

            if (writeContext.NavigationSource != null)
            {
                var propertyType = navigationProperty.Type;
                var model = writeContext.Model;
                var linkBuilder = model.GetNavigationSourceLinkBuilder(writeContext.NavigationSource);
                var navigationUrl = linkBuilder.BuildNavigationLink(entityInstanceContext, navigationProperty, writeContext.MetadataLevel);

                navigationLink = new ODataNavigationLink
                {
                    IsCollection = propertyType.IsCollection(),
                    Name = navigationProperty.Name,
                };

                if (navigationUrl != null)
                {
                    navigationLink.Url = navigationUrl;
                }
            }

            return navigationLink;
        }

        private IEnumerable<ODataProperty> CreateStructuralPropertyBag(
            IEnumerable<IEdmStructuralProperty> structuralProperties, EntityInstanceContext entityInstanceContext)
        {
            Contract.Assert(structuralProperties != null);
            Contract.Assert(entityInstanceContext != null);

            var properties = new List<ODataProperty>();
            foreach (var structuralProperty in structuralProperties)
            {
                var property = CreateStructuralProperty(structuralProperty, entityInstanceContext);
                if (property != null)
                {
                    properties.Add(property);
                }
            }

            return properties;
        }

        /// <summary>
        /// Creates the <see cref="ODataProperty"/> to be written for the given entity and the structural property.
        /// </summary>
        /// <param name="structuralProperty">The EDM structural property being written.</param>
        /// <param name="entityInstanceContext">The context for the entity instance being written.</param>
        /// <returns>The <see cref="ODataProperty"/> to write.</returns>
        public virtual ODataProperty CreateStructuralProperty(IEdmStructuralProperty structuralProperty, EntityInstanceContext entityInstanceContext)
        {
            if (structuralProperty == null)
            {
                throw Error.ArgumentNull("structuralProperty");
            }
            if (entityInstanceContext == null)
            {
                throw Error.ArgumentNull("entityInstanceContext");
            }

            var writeContext = entityInstanceContext.SerializerContext;

            var serializer = SerializerProvider.GetEdmTypeSerializer(structuralProperty.Type);
            if (serializer == null)
            {
                throw new SerializationException(
                    Error.Format(SRResources.TypeCannotBeSerialized, structuralProperty.Type.FullName(), typeof(ODataOutputFormatter).Name));
            }

            var propertyValue = entityInstanceContext.GetPropertyValue(structuralProperty.Name);

            var propertyType = structuralProperty.Type;
            if (propertyValue != null)
            {
                var actualType = writeContext.GetEdmType(propertyValue, propertyValue.GetType());
                if (propertyType != null && propertyType != actualType)
                {
                    propertyType = actualType;
                }
            }

            return serializer.CreateProperty(propertyValue, propertyType, structuralProperty.Name, writeContext);
        }

        private IEnumerable<ODataAction> CreateODataActions(
            IEnumerable<IEdmAction> actions, EntityInstanceContext entityInstanceContext)
        {
            Contract.Assert(actions != null);
            Contract.Assert(entityInstanceContext != null);

            foreach (var action in actions)
            {
                var oDataAction = CreateODataAction(action, entityInstanceContext);
                if (oDataAction != null)
                {
                    yield return oDataAction;
                }
            }
        }

        /// <summary>
        /// Creates an <see cref="ODataAction" /> to be written for the given action and the entity instance.
        /// </summary>
        /// <param name="action">The OData action.</param>
        /// <param name="entityInstanceContext">The context for the entity instance being written.</param>
        /// <returns>The created action or null if the action should not be written.</returns>
        [SuppressMessage("Microsoft.Usage", "CA2234: Pass System.Uri objects instead of strings", Justification = "This overload is equally good")]
        public virtual ODataAction CreateODataAction(IEdmAction action, EntityInstanceContext entityInstanceContext)
        {
            if (action == null)
            {
                throw Error.ArgumentNull("action");
            }

            if (entityInstanceContext == null)
            {
                throw Error.ArgumentNull("entityInstanceContext");
            }

            var metadataLevel = entityInstanceContext.SerializerContext.MetadataLevel;
            var model = entityInstanceContext.EdmModel;

            var builder = model.GetActionLinkBuilder(action);

            if (builder == null)
            {
                return null;
            }

            if (ShouldOmitAction(action, builder, metadataLevel))
            {
                return null;
            }

            var target = builder.BuildActionLink(entityInstanceContext);

            if (target == null)
            {
                return null;
            }

            var baseUri = new Uri(entityInstanceContext.Url.CreateODataLink(new MetadataPathSegment()));
            var metadata = new Uri(baseUri, "#" + CreateMetadataFragment(action));

            var odataAction = new ODataAction
            {
                Metadata = metadata,
            };

            var alwaysIncludeDetails = metadataLevel == ODataMetadataLevel.FullMetadata;

            // Always omit the title in minimal/no metadata modes.
            if (alwaysIncludeDetails)
            {
                EmitTitle(model, action, odataAction);
            }

            // Omit the target in minimal/no metadata modes unless it doesn't follow conventions.
            if (alwaysIncludeDetails || !builder.FollowsConventions)
            {
                odataAction.Target = target;
            }

            return odataAction;
        }

        internal static void EmitTitle(IEdmModel model, IEdmOperation operation, ODataOperation odataAction)
        {
            // The title should only be emitted in full metadata.
            var titleAnnotation = model.GetOperationTitleAnnotation(operation);
            if (titleAnnotation != null)
            {
                odataAction.Title = titleAnnotation.Title;
            }
            else
            {
                odataAction.Title = operation.Name;
            }
        }

        internal static string CreateMetadataFragment(IEdmAction action)
        {
            // There can only be one entity container in OData V4.
            var actionName = action.Name;
            var fragment = action.Namespace + "." + actionName;

            return fragment;
        }

        private static IEdmEntityType GetODataPathType(ODataSerializerContext serializerContext)
        {
            Contract.Assert(serializerContext != null);
            if (serializerContext.NavigationProperty != null)
            {
                // we are in an expanded navigation property. use the navigation source to figure out the 
                // type.
                return serializerContext.NavigationSource.EntityType();
            }
            else
            {
                // figure out the type from the path.
                var edmType = serializerContext.Path.EdmType;
                if (edmType.TypeKind == EdmTypeKind.Collection)
                {
                    edmType = (edmType as IEdmCollectionType).ElementType.Definition;
                }

                return edmType as IEdmEntityType;
            }
        }

        internal static void AddTypeNameAnnotationAsNeeded(ODataEntry entry, IEdmEntityType odataPathType,
            ODataMetadataLevel metadataLevel)
        {
            // ODataLib normally has the caller decide whether or not to serialize properties by leaving properties
            // null when values should not be serialized. The TypeName property is different and should always be
            // provided to ODataLib to enable model validation. A separate annotation is used to decide whether or not
            // to serialize the type name (a null value prevents serialization).

            // Note: In the current version of ODataLib the default behavior likely now matches the requirements for
            // minimal metadata mode. However, there have been behavior changes/bugs there in the past, so the safer
            // option is for this class to take control of type name serialization in minimal metadata mode.

            Contract.Assert(entry != null);

            string typeName = null; // Set null to force the type name not to serialize.

            // Provide the type name to serialize.
            if (!ShouldSuppressTypeNameSerialization(entry, odataPathType, metadataLevel))
            {
                typeName = entry.TypeName;
            }

            entry.SetAnnotation<SerializationTypeNameAnnotation>(new SerializationTypeNameAnnotation
            {
                TypeName = typeName
            });
        }

        internal static bool ShouldOmitAction(IEdmAction action, ActionLinkBuilder builder,
            ODataMetadataLevel metadataLevel)
        {
            Contract.Assert(builder != null);

            switch (metadataLevel)
            {
                case ODataMetadataLevel.MinimalMetadata:
                case ODataMetadataLevel.NoMetadata:
                    return action.IsBound && builder.FollowsConventions;

                case ODataMetadataLevel.FullMetadata:
                default: // All values already specified; just keeping the compiler happy.
                    return false;
            }
        }

        internal static bool ShouldSuppressTypeNameSerialization(ODataEntry entry, IEdmEntityType edmType,
            ODataMetadataLevel metadataLevel)
        {
            Contract.Assert(entry != null);

            switch (metadataLevel)
            {
                case ODataMetadataLevel.NoMetadata:
                    return true;
                case ODataMetadataLevel.FullMetadata:
                    return false;
                case ODataMetadataLevel.MinimalMetadata:
                default: // All values already specified; just keeping the compiler happy.
                    string pathTypeName = null;
                    if (edmType != null)
                    {
                        pathTypeName = edmType.FullName();
                    }
                    var entryTypeName = entry.TypeName;
                    return String.Equals(entryTypeName, pathTypeName, StringComparison.Ordinal);
            }
        }

        private IEdmEntityTypeReference GetEntityType(object graph, ODataSerializerContext writeContext)
        {
            Contract.Assert(graph != null);

            var edmType = writeContext.GetEdmType(graph, graph.GetType());
            Contract.Assert(edmType != null);

            if (!edmType.IsEntity())
            {
                throw new SerializationException(
                    Error.Format(SRResources.CannotWriteType, GetType().Name, edmType.FullName()));
            }

            return edmType.AsEntity();
        }
    }
}
