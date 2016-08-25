// Copyright (c) Microsoft Corporation.  All rights reserved.
// Licensed under the MIT License.  See License.txt in the project root for license information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Reflection;
using System.Runtime.Serialization;
using Microsoft.AspNetCore.OData.Common;
using Microsoft.AspNetCore.OData.Extensions;
using Microsoft.OData.Core;
using Microsoft.OData.Edm;
using Microsoft.OData.Edm.Library;

namespace Microsoft.AspNetCore.OData.Formatter.Deserialization
{
    internal static class DeserializationHelpers
    {
        internal static void ApplyProperty(ODataProperty property, IEdmStructuredTypeReference resourceType, object resource,
            ODataDeserializerProvider deserializerProvider, ODataDeserializerContext readContext)
        {
            var edmProperty = resourceType.FindProperty(property.Name);

            var isDynamicProperty = false;
            var propertyName = property.Name;
            if (edmProperty != null)
            {
                propertyName = EdmLibHelpers.GetClrPropertyName(edmProperty, readContext.Model);
            }
            else
            {
                var structuredType = resourceType.StructuredDefinition();
                isDynamicProperty = structuredType != null && structuredType.IsOpen;
            }

            // dynamic properties have null values
            var propertyType = edmProperty != null ? edmProperty.Type : null;

            EdmTypeKind propertyKind;
            var value = ConvertValue(property.Value, ref propertyType, deserializerProvider, readContext,
                out propertyKind);

            if (isDynamicProperty)
            {
                SetDynamicProperty(resource, resourceType, propertyKind, propertyName, value, propertyType,
                    readContext);
            }
            else
            {
                SetDeclaredProperty(resource, propertyKind, propertyName, value, edmProperty, readContext);
            }
        }

        internal static void SetDynamicProperty(object resource, IEdmStructuredTypeReference resourceType,
            EdmTypeKind propertyKind, string propertyName, object propertyValue, IEdmTypeReference propertyType,
            ODataDeserializerContext readContext)
        {  
            if (propertyKind == EdmTypeKind.Collection && propertyValue.GetType() != typeof(EdmComplexObjectCollection)
                && propertyValue.GetType() != typeof(EdmEnumObjectCollection))
            {
                SetDynamicCollectionProperty(resource, propertyName, propertyValue, propertyType.AsCollection(),
                    resourceType.StructuredDefinition(), readContext);
            }
            else
            {
                SetDynamicProperty(resource, propertyName, propertyValue, resourceType.StructuredDefinition(),
                    readContext);
            }
        }

        internal static void SetDeclaredProperty(object resource, EdmTypeKind propertyKind, string propertyName,
            object propertyValue, IEdmProperty edmProperty, ODataDeserializerContext readContext)
        {
            if (propertyKind == EdmTypeKind.Collection)
            {
                SetCollectionProperty(resource, edmProperty, propertyValue, propertyName);
            }
            else
            {
                if (!readContext.IsUntyped)
                {
                    if (propertyKind == EdmTypeKind.Primitive)
                    {
                        propertyValue = EdmPrimitiveHelpers.ConvertPrimitiveValue(propertyValue,
                            GetPropertyType(resource, propertyName));
                    }
                }

                SetProperty(resource, propertyName, propertyValue);
            }
        }

        internal static void SetCollectionProperty(object resource, IEdmProperty edmProperty, object value, string propertyName)
        {
            Contract.Assert(edmProperty != null);

            SetCollectionProperty(resource, propertyName, edmProperty.Type.AsCollection(), value, clearCollection: false);
        }

        internal static void SetCollectionProperty(object resource, string propertyName,
            IEdmCollectionTypeReference edmPropertyType, object value, bool clearCollection)
        {
            if (value != null)
            {
                var collection = value as IEnumerable;
                Contract.Assert(collection != null,
                    "SetCollectionProperty is always passed the result of ODataFeedDeserializer or ODataCollectionDeserializer");

                var resourceType = resource.GetType();
                var propertyType = GetPropertyType(resource, propertyName);

                Type elementType;
                if (!propertyType.IsCollection(out elementType))
                {
                    var message = Error.Format(SRResources.PropertyIsNotCollection, propertyType.FullName, propertyName, resourceType.FullName);
                    throw new SerializationException(message);
                }

                IEnumerable newCollection;
                if (CanSetProperty(resource, propertyName) &&
                    CollectionDeserializationHelpers.TryCreateInstance(propertyType, edmPropertyType, elementType, out newCollection))
                {
                    // settable collections
                    collection.AddToCollection(newCollection, elementType, resourceType, propertyName, propertyType);
                    if (propertyType.IsArray)
                    {
                        newCollection = CollectionDeserializationHelpers.ToArray(newCollection, elementType);
                    }

                    SetProperty(resource, propertyName, newCollection);
                }
                else
                {
                    // get-only collections.
                    newCollection = GetProperty(resource, propertyName) as IEnumerable;
                    if (newCollection == null)
                    {
                        var message = Error.Format(SRResources.CannotAddToNullCollection, propertyName, resourceType.FullName);
                        throw new SerializationException(message);
                    }

                    if (clearCollection)
                    {
                        newCollection.Clear(propertyName, resourceType);
                    }

                    collection.AddToCollection(newCollection, elementType, resourceType, propertyName, propertyType);
                }
            }
        }

        internal static void SetDynamicCollectionProperty(object resource, string propertyName, object value,
            IEdmCollectionTypeReference edmPropertyType, IEdmStructuredType structuredType,
            ODataDeserializerContext readContext)
        {
            Contract.Assert(value != null);
            Contract.Assert(readContext != null);
            Contract.Assert(readContext.Model != null);

            var collection = value as IEnumerable;
            Contract.Assert(collection != null);

            var resourceType = resource.GetType();
            var elementType = EdmLibHelpers.GetClrType(edmPropertyType.ElementType(), readContext.Model);
            var propertyType = typeof(ICollection<>).MakeGenericType(elementType);
            IEnumerable newCollection;
            if (CollectionDeserializationHelpers.TryCreateInstance(propertyType, edmPropertyType, elementType,
                out newCollection))
            {
                collection.AddToCollection(newCollection, elementType, resourceType, propertyName, propertyType);
                SetDynamicProperty(resource, propertyName, newCollection, structuredType, readContext);
            }
        }

        internal static void SetProperty(object resource, string propertyName, object value)
        {
            var delta = resource as IDelta;
            if (delta == null)
            {
                resource.GetType().GetProperty(propertyName).SetValue(resource, value, index: null);
            }
            else
            {
                delta.TrySetPropertyValue(propertyName, value);
            }
        }

        internal static void SetDynamicProperty(object resource, string propertyName, object value,
            IEdmStructuredType structuredType, ODataDeserializerContext readContext)
        {
            var delta = resource as IDelta;
            if (delta != null)
            {
                delta.TrySetPropertyValue(propertyName, value);
            }
            else
            {
                var propertyInfo = EdmLibHelpers.GetDynamicPropertyDictionary(structuredType,
                    readContext.Model);
                if (propertyInfo == null)
                {
                    return;
                }

                IDictionary<string, object> dynamicPropertyDictionary;
                var dynamicDictionaryObject = propertyInfo.GetValue(resource);
                if (dynamicDictionaryObject == null)
                {
                    if (!propertyInfo.CanWrite)
                    {
                        throw Error.InvalidOperation(SRResources.CannotSetDynamicPropertyDictionary, propertyName,
                            resource.GetType().FullName);
                    }

                    dynamicPropertyDictionary = new Dictionary<string, object>();
                    propertyInfo.SetValue(resource, dynamicPropertyDictionary);
                }
                else
                {
                    dynamicPropertyDictionary = (IDictionary<string, object>)dynamicDictionaryObject;
                }

                if (dynamicPropertyDictionary.ContainsKey(propertyName))
                {
                    throw Error.InvalidOperation(SRResources.DuplicateDynamicPropertyNameFound,
                        propertyName, structuredType.FullTypeName());
                }

                dynamicPropertyDictionary.Add(propertyName, value);
            }
        }

        internal static object ConvertValue(object oDataValue, ref IEdmTypeReference propertyType, ODataDeserializerProvider deserializerProvider,
            ODataDeserializerContext readContext, out EdmTypeKind typeKind)
        {
            if (oDataValue == null)
            {
                typeKind = EdmTypeKind.None;
                return null;
            }

            var complexValue = oDataValue as ODataComplexValue;
            if (complexValue != null)
            {
                typeKind = EdmTypeKind.Complex;
                return ConvertComplexValue(complexValue, ref propertyType, deserializerProvider, readContext);
            }

            var enumValue = oDataValue as ODataEnumValue;
            if (enumValue != null)
            {
                typeKind = EdmTypeKind.Enum;
                return ConvertEnumValue(enumValue, ref propertyType, deserializerProvider, readContext);
            }

            var collection = oDataValue as ODataCollectionValue;
            if (collection != null)
            {
                typeKind = EdmTypeKind.Collection;
                return ConvertCollectionValue(collection, ref propertyType, deserializerProvider, readContext);
            }
            
            typeKind = EdmTypeKind.Primitive;
            return oDataValue;
        }

        internal static Type GetPropertyType(object resource, string propertyName)
        {
            Contract.Assert(resource != null);
            Contract.Assert(propertyName != null);

            var delta = resource as IDelta;
            if (delta != null)
            {
                Type type;
                delta.TryGetPropertyType(propertyName, out type);
                return type;
            }
            else
            {
                var property = resource.GetType().GetProperty(propertyName);
                return property == null ? null : property.PropertyType;
            }
        }

        private static object ConvertComplexValue(ODataComplexValue complexValue, ref IEdmTypeReference propertyType,
            ODataDeserializerProvider deserializerProvider, ODataDeserializerContext readContext)
        {
            IEdmComplexTypeReference edmComplexType;
            if (propertyType == null)
            {
                // open complex property
                Contract.Assert(!String.IsNullOrEmpty(complexValue.TypeName),
                    "ODataLib should have verified that open complex value has a type name since we provided metadata.");
                var model = readContext.Model;
                IEdmType edmType = model.FindType(complexValue.TypeName);
                Contract.Assert(edmType.TypeKind == EdmTypeKind.Complex, "ODataLib should have verified that complex value has a complex resource type.");
                edmComplexType = new EdmComplexTypeReference(edmType as IEdmComplexType, isNullable: true);
                propertyType = edmComplexType;
            }
            else
            {
                edmComplexType = propertyType.AsComplex();
            }

            var deserializer = deserializerProvider.GetEdmTypeDeserializer(edmComplexType);
            return deserializer.ReadInline(complexValue, propertyType, readContext);
        }

        private static bool CanSetProperty(object resource, string propertyName)
        {
            var delta = resource as IDelta;
            if (delta != null)
            {
                return true;
            }
            else
            {
                var property = resource.GetType().GetProperty(propertyName);
                return property != null && property.GetSetMethod() != null;
            }
        }

        private static object GetProperty(object resource, string propertyName)
        {
            var delta = resource as IDelta;
            if (delta != null)
            {
                object value;
                delta.TryGetPropertyValue(propertyName, out value);
                return value;
            }
            else
            {
                var property = resource.GetType().GetProperty(propertyName);
                Contract.Assert(property != null, "ODataLib should have already verified that the property exists on the type.");
                return property.GetValue(resource, index: null);
            }
        }

        private static object ConvertCollectionValue(ODataCollectionValue collection,
            ref IEdmTypeReference propertyType, ODataDeserializerProvider deserializerProvider,
            ODataDeserializerContext readContext)
        {
            IEdmCollectionTypeReference collectionType;
            if (propertyType == null)
            {
                // dynamic collection property
                Contract.Assert(!String.IsNullOrEmpty(collection.TypeName),
                    "ODataLib should have verified that dynamic collection value has a type name " +
                    "since we provided metadata.");

                var elementTypeName = GetCollectionElementTypeName(collection.TypeName, isNested: false);
                var model = readContext.Model;
                var elementType = model.FindType(elementTypeName);
                Contract.Assert(elementType != null);
                collectionType =
                    new EdmCollectionTypeReference(
                        new EdmCollectionType(elementType.ToEdmTypeReference(isNullable: false)));
                propertyType = collectionType;
            }
            else
            {
                collectionType = propertyType as IEdmCollectionTypeReference;
                Contract.Assert(collectionType != null, "The type for collection must be a IEdmCollectionType.");
            }

            var deserializer = deserializerProvider.GetEdmTypeDeserializer(collectionType);
            return deserializer.ReadInline(collection, collectionType, readContext);
        }

        private static object ConvertEnumValue(ODataEnumValue enumValue, ref IEdmTypeReference propertyType,
            ODataDeserializerProvider deserializerProvider, ODataDeserializerContext readContext)
        {
            IEdmEnumTypeReference edmEnumType;
            if (propertyType == null)
            {
                // dynamic enum property
                Contract.Assert(!String.IsNullOrEmpty(enumValue.TypeName),
                    "ODataLib should have verified that dynamic enum value has a type name since we provided metadata.");
                var model = readContext.Model;
                IEdmType edmType = model.FindType(enumValue.TypeName);
                Contract.Assert(edmType.TypeKind == EdmTypeKind.Enum, "ODataLib should have verified that enum value has a enum resource type.");
                edmEnumType = new EdmEnumTypeReference(edmType as IEdmEnumType, isNullable: true);
                propertyType = edmEnumType;
            }
            else
            {
                edmEnumType = propertyType.AsEnum();
            }

            var deserializer = deserializerProvider.GetEdmTypeDeserializer(edmEnumType);
            return deserializer.ReadInline(enumValue, propertyType, readContext);
        }

        // The same logic from ODL to get the element type name in a collection.
        private static string GetCollectionElementTypeName(string typeName, bool isNested)
        {
            const string CollectionTypeQualifier = "Collection";
            var collectionTypeQualifierLength = CollectionTypeQualifier.Length;

            // A collection type name must not be null, it has to start with "Collection(" and end with ")"
            // and must not be "Collection()"
            if (typeName != null &&
                typeName.StartsWith(CollectionTypeQualifier + "(", StringComparison.Ordinal) &&
                typeName[typeName.Length - 1] == ')' &&
                typeName.Length != collectionTypeQualifierLength + 2)
            {
                if (isNested)
                {
                    throw new ODataException(Error.Format(SRResources.NestedCollectionsNotSupported, typeName));
                }

                var innerTypeName = typeName.Substring(collectionTypeQualifierLength + 1,
                    typeName.Length - (collectionTypeQualifierLength + 2));

                // Check if it is not a nested collection and throw if it is
                GetCollectionElementTypeName(innerTypeName, true);

                return innerTypeName;
            }

            return null;
        }
    }
}
