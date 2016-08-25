// Copyright (c) Microsoft Corporation.  All rights reserved.
// Licensed under the MIT License.  See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Contracts;
using System.Linq;
using System.Reflection;
using Microsoft.OData.Edm;
using Microsoft.OData.Edm.Expressions;
using Microsoft.OData.Edm.Library;
using Microsoft.OData.Edm.Library.Expressions;
using Microsoft.OData.Edm.Validation;
using Microsoft.AspNetCore.OData.Common;
using Microsoft.AspNetCore.OData.Extensions;

namespace Microsoft.AspNetCore.OData.Builder
{
    internal static class EdmModelHelperMethods
    {
        public static IEdmModel BuildEdmModel(ODataModelBuilder builder)
        {
            if (builder == null)
            {
                throw Error.ArgumentNull("builder");
            }

            var model = new EdmModel();
            var container = new EdmEntityContainer(builder.Namespace, builder.ContainerName);

            // add types and sets, building an index on the way.
            Dictionary<Type, IEdmType> edmTypeMap = model.AddTypes(builder.StructuralTypes, builder.EnumTypes);

            // Add EntitySets and build the mapping between the EdmEntitySet and the NavigationSourceConfiguration
            NavigationSourceAndAnnotations[] entitySets = container.AddEntitySetAndAnnotations(builder, edmTypeMap);

            // Add Singletons and build the mapping between the EdmSingleton and the NavigationSourceConfiguration
            NavigationSourceAndAnnotations[] singletons = container.AddSingletonAndAnnotations(builder, edmTypeMap);

            // Merge EntitySets and Singletons together
            var navigationSources = entitySets.Concat(singletons);

            // Build the navigation source map
            IDictionary<string, EdmNavigationSource> navigationSourceMap = model.GetNavigationSourceMap(builder, edmTypeMap, navigationSources);

            // add procedures
            model.AddProcedures(builder.Procedures, container, edmTypeMap, navigationSourceMap);

            // finish up
            model.AddElement(container);

            // build the map from IEdmEntityType to IEdmFunctionImport
            model.SetAnnotationValue<BindableProcedureFinder>(model, new BindableProcedureFinder(model));

            return model;
        }

        private static void AddTypes(this EdmModel model, Dictionary<Type, IEdmType> types)
        {
            Contract.Assert(model != null);
            Contract.Assert(types != null);

            foreach (var type in types.Values)
            {
                model.AddType(type);
            }
        }

        private static NavigationSourceAndAnnotations[] AddEntitySetAndAnnotations(this EdmEntityContainer container,
            ODataModelBuilder builder, Dictionary<Type, IEdmType> edmTypeMap)
        {
            var configurations = builder.EntitySets;

            // build the entitysets
            var entitySets = AddEntitySets(configurations, container, edmTypeMap);

            // return the annotation array
            return entitySets.Select(e => new NavigationSourceAndAnnotations()
            {
                NavigationSource = e.Item1,
                Configuration = e.Item2,
                LinkBuilder = new NavigationSourceLinkBuilderAnnotation(e.Item2),
                Url = new NavigationSourceUrlAnnotation { Url = e.Item2.GetUrl() }
            }).ToArray();
        }

        private static NavigationSourceAndAnnotations[] AddSingletonAndAnnotations(this EdmEntityContainer container,
            ODataModelBuilder builder, Dictionary<Type, IEdmType> edmTypeMap)
        {
            var configurations = builder.Singletons;

            // build the singletons
            var singletons = AddSingletons(configurations, container, edmTypeMap);

            // return the annotation array
            return singletons.Select(e => new NavigationSourceAndAnnotations()
            {
                NavigationSource = e.Item1,
                Configuration = e.Item2,
                LinkBuilder = new NavigationSourceLinkBuilderAnnotation(e.Item2),
                Url = new NavigationSourceUrlAnnotation { Url = e.Item2.GetUrl() }
            }).ToArray();
        }

        private static IDictionary<string, EdmNavigationSource> GetNavigationSourceMap(this EdmModel model, ODataModelBuilder builder,
            Dictionary<Type, IEdmType> edmTypeMap, IEnumerable<NavigationSourceAndAnnotations> navigationSourceAndAnnotations)
        {
            // index the navigation source by name
            var edmNavigationSourceMap = navigationSourceAndAnnotations.ToDictionary(e => e.NavigationSource.Name, e => e.NavigationSource);

            // apply the annotations
            foreach (var navigationSourceAndAnnotation in navigationSourceAndAnnotations)
            {
                var navigationSource = navigationSourceAndAnnotation.NavigationSource;
                model.SetAnnotationValue<NavigationSourceUrlAnnotation>(navigationSource, navigationSourceAndAnnotation.Url);
                model.SetNavigationSourceLinkBuilder(navigationSource, navigationSourceAndAnnotation.LinkBuilder);

                AddNavigationBindings(navigationSourceAndAnnotation.Configuration, navigationSource, navigationSourceAndAnnotation.LinkBuilder,
                    builder, edmTypeMap, edmNavigationSourceMap);
            }

            return edmNavigationSourceMap;
        }

        private static void AddNavigationBindings(NavigationSourceConfiguration configuration,
            EdmNavigationSource navigationSource,
            NavigationSourceLinkBuilderAnnotation linkBuilder,
            ODataModelBuilder builder,
            Dictionary<Type, IEdmType> edmTypeMap,
            Dictionary<string, EdmNavigationSource> edmNavigationSourceMap)
        {
            foreach (EntityTypeConfiguration entityType in builder.ThisAndBaseAndDerivedTypes(configuration.EntityType))
            {
                foreach (var navigationProperty in entityType.NavigationProperties)
                {
                    var binding = configuration.FindBinding(navigationProperty);
                    var isContained = navigationProperty.ContainsTarget;
                    if (binding != null || isContained)
                    {
                        var edmEntityType = edmTypeMap[entityType.ClrType] as EdmEntityType;
                        var edmNavigationProperty = edmEntityType.NavigationProperties()
                            .Single(np => np.Name == navigationProperty.Name);

                        if (!isContained)
                        {
                            navigationSource.AddNavigationTarget(
                                edmNavigationProperty,
                                edmNavigationSourceMap[binding.TargetNavigationSource.Name]);
                        }

                        var linkBuilderFunc = configuration.GetNavigationPropertyLink(navigationProperty);
                        if (linkBuilderFunc != null)
                        {
                            linkBuilder.AddNavigationPropertyLinkBuilder(edmNavigationProperty, linkBuilderFunc);
                        }
                    }
                }
            }
        }

        private static void AddProcedureParameters(EdmOperation operation, ProcedureConfiguration procedure, Dictionary<Type, IEdmType> edmTypeMap)
        {
            foreach (var parameter in procedure.Parameters)
            {
                var isParameterOptional = parameter.OptionalParameter;
                var parameterTypeReference = GetEdmTypeReference(edmTypeMap, parameter.TypeConfiguration, nullable: isParameterOptional);
                IEdmOperationParameter operationParameter = new EdmOperationParameter(operation, parameter.Name, parameterTypeReference);
                operation.AddParameter(operationParameter);
            }
        }

        private static void AddProcedureLinkBuilder(IEdmModel model, IEdmOperation operation, ProcedureConfiguration procedure)
        {
            if (procedure.BindingParameter.TypeConfiguration.Kind == EdmTypeKind.Entity)
            {
                var actionConfiguration = procedure as ActionConfiguration;
                var action = operation as IEdmAction;
                var functionConfiguration = procedure as FunctionConfiguration;
                var function = operation as IEdmFunction;
                if (actionConfiguration != null && actionConfiguration.GetActionLink() != null && action != null)
                {
                    model.SetActionLinkBuilder(
                        action,
                        new ActionLinkBuilder(actionConfiguration.GetActionLink(), actionConfiguration.FollowsConventions));
                }
                else if (functionConfiguration != null && functionConfiguration.GetFunctionLink() != null && function != null)
                {
                    model.SetFunctionLinkBuilder(
                        function,
                        new FunctionLinkBuilder(functionConfiguration.GetFunctionLink(), functionConfiguration.FollowsConventions));
                }
            }
        }

        private static void ValidateProcedureEntitySetPath(IEdmModel model, IEdmOperationImport operationImport, ProcedureConfiguration procedure)
        {
            IEdmOperationParameter procedureParameter;
            IEnumerable<IEdmNavigationProperty> navPath;
            IEnumerable<EdmError> edmErrors;
            if (procedure.EntitySetPath != null && !operationImport.TryGetRelativeEntitySetPath(model, out procedureParameter, out navPath, out edmErrors))
            {
                throw Error.InvalidOperation(SRResources.ProcedureHasInvalidEntitySetPath, String.Join("/", procedure.EntitySetPath), procedure.FullyQualifiedName);
            }
        }

        [SuppressMessage("Microsoft.Maintainability", "CA1506:AvoidExcessiveClassCoupling", 
            Justification = "The majority of types referenced by this method are EdmLib types this method needs to know about to operate correctly")]
        private static void AddProcedures(this EdmModel model, IEnumerable<ProcedureConfiguration> configurations, EdmEntityContainer container,
            Dictionary<Type, IEdmType> edmTypeMap, IDictionary<string, EdmNavigationSource> edmNavigationSourceMap)
        {
            Contract.Assert(model != null, "Model can't be null");

            ValidateActionOverload(configurations.OfType<ActionConfiguration>());

            foreach (var procedure in configurations)
            {
                var returnReference = GetEdmTypeReference(edmTypeMap,
                    procedure.ReturnType,
                    procedure.ReturnType != null && procedure.OptionalReturn);
                var expression = GetEdmEntitySetExpression(edmNavigationSourceMap, procedure);
                IEdmPathExpression pathExpression = procedure.EntitySetPath != null
                    ? new EdmPathExpression(procedure.EntitySetPath)
                    : null;

                EdmOperationImport operationImport;

                switch (procedure.Kind)
                {
                    case ProcedureKind.Action:
                        operationImport = CreateActionImport(procedure, container, returnReference, expression, pathExpression);
                        break;
                    case ProcedureKind.Function:
                        operationImport = CreateFunctionImport((FunctionConfiguration)procedure, container, returnReference, expression, pathExpression);
                        break;
                    case ProcedureKind.ServiceOperation:
                        Contract.Assert(false, "ServiceOperations are not supported.");
                        goto default;
                    default:
                        Contract.Assert(false, "Unsupported ProcedureKind");
                        return;
                }

                var operation = (EdmOperation)operationImport.Operation;
                if (procedure.IsBindable && procedure.Title != null & procedure.Title != procedure.Name)
                {
                    model.SetOperationTitleAnnotation(operation, new OperationTitleAnnotation(procedure.Title));
                }

                if (procedure.IsBindable &&
                    procedure.NavigationSource != null &&
                    edmNavigationSourceMap.ContainsKey(procedure.NavigationSource.Name))
                {
                    model.SetAnnotationValue(operation, new ReturnedEntitySetAnnotation(procedure.NavigationSource.Name));
                }

                AddProcedureParameters(operation, procedure, edmTypeMap);

                if (procedure.IsBindable)
                {
                    AddProcedureLinkBuilder(model, operation, procedure);
                    ValidateProcedureEntitySetPath(model, operationImport, procedure);
                }
                else
                {
                    container.AddElement(operationImport);
                }

                model.AddElement(operation);
            }
        }

        private static EdmOperationImport CreateActionImport(
            ProcedureConfiguration procedure,
            EdmEntityContainer container,
            IEdmTypeReference returnReference,
            IEdmExpression expression,
            IEdmPathExpression pathExpression)
        {
            var operation = new EdmAction(
                container.Namespace,
                procedure.Name,
                returnReference,
                procedure.IsBindable,
                pathExpression);
            return new EdmActionImport(container, procedure.Name, operation, expression);
        }

        private static EdmOperationImport CreateFunctionImport(
            FunctionConfiguration function,
            EdmEntityContainer container,
            IEdmTypeReference returnReference,
            IEdmExpression expression,
            IEdmPathExpression pathExpression)
        {
            var operation = new EdmFunction(
                    container.Namespace,
                    function.Name,
                    returnReference,
                    function.IsBindable,
                    pathExpression,
                    function.IsComposable);
            return new EdmFunctionImport(container, function.Name, operation, expression, includeInServiceDocument: function.IncludeInServiceDocument);
        }

        // 11.5.4.2  Action Overload Resolution
        // The same action name may be used multiple times within a schema provided there is at most one unbound overload,
        // and each bound overload specifies a different binding parameter type. If the action is bound and the binding 
        // parameter type is part of an inheritance hierarchy, the action overload is selected based on the type of the
        // URL segment preceding the action name. A type-cast segment can be used to select an action defined on a
        // particular type in the hierarchy.
        private static void ValidateActionOverload(IEnumerable<ActionConfiguration> configurations)
        {
            // 1. validate at most one unbound overload
            var unboundActions = configurations.Where(a => !a.IsBindable).ToArray();
            if (unboundActions.Length > 0)
            {
                var unboundActionNames = new HashSet<string>();
                foreach (var action in unboundActions)
                {
                    if (!unboundActionNames.Contains(action.Name))
                    {
                        unboundActionNames.Add(action.Name);
                    }
                    else
                    {
                        throw Error.InvalidOperation(SRResources.MoreThanOneUnboundActionFound, action.Name);
                    }
                }
            }

            // 2. validate each bound overload action specifies a diffrent binding parameter type
            var boundActions = configurations.Where(a => a.IsBindable).ToArray();
            if (boundActions.Length > 0)
            {
                var actionNamesToBindingTypes = new Dictionary<string, IList<IEdmTypeConfiguration>>();
                foreach (var action in boundActions)
                {
                    var newBindingType = action.BindingParameter.TypeConfiguration;
                    if (actionNamesToBindingTypes.ContainsKey(action.Name))
                    {
                        var bindingTypes = actionNamesToBindingTypes[action.Name];
                        foreach (var type in bindingTypes)
                        {
                            if (type == newBindingType)
                            {
                                throw Error.InvalidOperation(SRResources.MoreThanOneOverloadActionBoundToSameTypeFound,
                                    action.Name, type.FullName);
                            }
                        }

                        bindingTypes.Add(newBindingType);
                    }
                    else
                    {
                        IList<IEdmTypeConfiguration> bindingTypes = new List<IEdmTypeConfiguration>();
                        bindingTypes.Add(newBindingType);
                        actionNamesToBindingTypes.Add(action.Name, bindingTypes);
                    }
                }
            }
        }

        private static Dictionary<Type, IEdmType> AddTypes(this EdmModel model, IEnumerable<StructuralTypeConfiguration> types,
            IEnumerable<EnumTypeConfiguration> enumTypes)
        {
            var configTypes = types.Concat<IEdmTypeConfiguration>(enumTypes);

            // build types
            var edmTypeMap = EdmTypeBuilder.GetTypesAndProperties(configTypes);
            var edmTypes = edmTypeMap.EdmTypes;

            // Add an annotate types
            model.AddTypes(edmTypes);
            model.AddClrTypeAnnotations(edmTypes);

            // add annotation for properties
            var edmProperties = edmTypeMap.EdmProperties;
            model.AddClrPropertyInfoAnnotations(edmProperties);
            model.AddPropertyRestrictionsAnnotations(edmTypeMap.EdmPropertiesRestrictions);

            // add dynamic dictionary property annotation for open types
            model.AddDynamicPropertyDictionaryAnnotations(edmTypeMap.OpenTypes);

            return edmTypes;
        }

        private static void AddType(this EdmModel model, IEdmType type)
        {
            if (type.TypeKind == EdmTypeKind.Complex)
            {
                model.AddElement(type as IEdmComplexType);
            }
            else if (type.TypeKind == EdmTypeKind.Entity)
            {
                model.AddElement(type as IEdmEntityType);
            }
            else if (type.TypeKind == EdmTypeKind.Enum)
            {
                model.AddElement(type as IEdmEnumType);
            }
            else
            {
                Contract.Assert(false, "Only ComplexTypes, EntityTypes and EnumTypes are supported.");
            }
        }

        private static EdmEntitySet AddEntitySet(this EdmEntityContainer container, EntitySetConfiguration entitySet, IDictionary<Type, IEdmType> edmTypeMap)
        {
            return container.AddEntitySet(entitySet.Name, (IEdmEntityType)edmTypeMap[entitySet.EntityType.ClrType]);
        }

        private static IEnumerable<Tuple<EdmEntitySet, EntitySetConfiguration>> AddEntitySets(IEnumerable<EntitySetConfiguration> entitySets, EdmEntityContainer container, Dictionary<Type, IEdmType> edmTypeMap)
        {
            return entitySets.Select(es => Tuple.Create(container.AddEntitySet(es, edmTypeMap), es));
        }

        private static EdmSingleton AddSingleton(this EdmEntityContainer container, SingletonConfiguration singletonType, IDictionary<Type, IEdmType> edmTypeMap)
        {
            return container.AddSingleton(singletonType.Name, (IEdmEntityType)edmTypeMap[singletonType.EntityType.ClrType]);
        }

        private static IEnumerable<Tuple<EdmSingleton, SingletonConfiguration>> AddSingletons(IEnumerable<SingletonConfiguration> singletons, EdmEntityContainer container, Dictionary<Type, IEdmType> edmTypeMap)
        {
            return singletons.Select(sg => Tuple.Create(container.AddSingleton(sg, edmTypeMap), sg));
        }

        private static void AddClrTypeAnnotations(this EdmModel model, Dictionary<Type, IEdmType> edmTypes)
        {
            foreach (var map in edmTypes)
            {
                // pre-populate the model with clr-type annotations so that we dont have to scan 
                // all loaded assemblies to find the clr type for an edm type that we build.
                var edmType = map.Value;
                var clrType = map.Key;
                model.SetAnnotationValue<ClrTypeAnnotation>(edmType, new ClrTypeAnnotation(clrType));
            }
        }

        private static void AddClrPropertyInfoAnnotations(this EdmModel model, Dictionary<PropertyInfo, IEdmProperty> edmProperties)
        {
            foreach (var edmPropertyMap in edmProperties)
            {
                var edmProperty = edmPropertyMap.Value;
                var clrProperty = edmPropertyMap.Key;
                if (edmProperty.Name != clrProperty.Name)
                {
                    model.SetAnnotationValue(edmProperty, new ClrPropertyInfoAnnotation(clrProperty));
                }
            }
        }

        private static void AddDynamicPropertyDictionaryAnnotations(this EdmModel model,
            Dictionary<IEdmStructuredType, PropertyInfo> openTypes)
        {
            foreach (var openType in openTypes)
            {
                var edmStructuredType = openType.Key;
                var propertyInfo = openType.Value;
                model.SetAnnotationValue(edmStructuredType, new DynamicPropertyDictionaryAnnotation(propertyInfo));
            }
        }

        private static void AddPropertyRestrictionsAnnotations(this EdmModel model, Dictionary<IEdmProperty, QueryableRestrictions> edmPropertiesRestrictions)
        {
            foreach (var edmPropertyRestriction in edmPropertiesRestrictions)
            {
                var edmProperty = edmPropertyRestriction.Key;
                var restrictions = edmPropertyRestriction.Value;
                model.SetAnnotationValue(edmProperty, new QueryableRestrictionsAnnotation(restrictions));
            }
        }

        private static IEdmExpression GetEdmEntitySetExpression(IDictionary<string, EdmNavigationSource> navigationSources, ProcedureConfiguration procedure)
        {
            if (procedure.NavigationSource != null)
            {
                EdmNavigationSource navigationSource;
                if (navigationSources.TryGetValue(procedure.NavigationSource.Name, out navigationSource))
                {
                    var entitySet = navigationSource as EdmEntitySet;
                    if (entitySet != null)
                    {
                        return new EdmEntitySetReferenceExpression(entitySet);
                    }
                }
                else
                {
                    throw Error.InvalidOperation(SRResources.EntitySetNotFoundForName, procedure.NavigationSource.Name);
                }
            }
            else if (procedure.EntitySetPath != null)
            {
                return new EdmPathExpression(procedure.EntitySetPath);
            }

            return null;
        }

        private static IEdmTypeReference GetEdmTypeReference(Dictionary<Type, IEdmType> availableTypes, IEdmTypeConfiguration configuration, bool nullable)
        {
            Contract.Assert(availableTypes != null);

            if (configuration == null)
            {
                return null;
            }

            var kind = configuration.Kind;
            if (kind == EdmTypeKind.Collection)
            {
                var collectionType = (CollectionTypeConfiguration)configuration;
                var edmCollectionType =
                    new EdmCollectionType(GetEdmTypeReference(availableTypes, collectionType.ElementType, nullable));
                return new EdmCollectionTypeReference(edmCollectionType);
            }
            else
            {
                var configurationClrType = TypeHelper.GetUnderlyingTypeOrSelf(configuration.ClrType);

                if (!configurationClrType.GetTypeInfo().IsEnum)
                {
                    configurationClrType = configuration.ClrType;
                }

                IEdmType type;

                if (availableTypes.TryGetValue(configurationClrType, out type))
                {
                    if (kind == EdmTypeKind.Complex)
                    {
                        return new EdmComplexTypeReference((IEdmComplexType)type, nullable);
                    }
                    else if (kind == EdmTypeKind.Entity)
                    {
                        return new EdmEntityTypeReference((IEdmEntityType)type, nullable);
                    }
                    else if (kind == EdmTypeKind.Enum)
                    {
                        return new EdmEnumTypeReference((IEdmEnumType)type, nullable);
                    }
                    else
                    {
                        throw Error.InvalidOperation(SRResources.UnsupportedEdmTypeKind, kind.ToString());
                    }
                }
                else if (configuration.Kind == EdmTypeKind.Primitive)
                {
                    var primitiveTypeConfiguration = configuration as PrimitiveTypeConfiguration;
                    return new EdmPrimitiveTypeReference(primitiveTypeConfiguration.EdmPrimitiveType, nullable);
                }
                else
                {
                    throw Error.InvalidOperation(SRResources.NoMatchingIEdmTypeFound, configuration.FullName);
                }
            }
        }

        internal static string GetNavigationSourceUrl(this IEdmModel model, IEdmNavigationSource navigationSource)
        {
            if (model == null)
            {
                throw Error.ArgumentNull("model");
            }

            if (navigationSource == null)
            {
                throw Error.ArgumentNull("navigationSource");
            }

            var annotation = model.GetAnnotationValue<NavigationSourceUrlAnnotation>(navigationSource);
            if (annotation == null)
            {
                return navigationSource.Name;
            }
            else
            {
                return annotation.Url;
            }
        }

        internal static IEnumerable<IEdmAction> GetAvailableActions(this IEdmModel model, IEdmEntityType entityType)
        {
            return model.GetAvailableProcedures(entityType).OfType<IEdmAction>();
        }

        internal static IEnumerable<IEdmFunction> GetAvailableFunctions(this IEdmModel model, IEdmEntityType entityType)
        {
            return model.GetAvailableProcedures(entityType).OfType<IEdmFunction>();
        }

        internal static IEnumerable<IEdmOperation> GetAvailableProcedures(this IEdmModel model, IEdmEntityType entityType)
        {
            if (model == null)
            {
                throw Error.ArgumentNull("model");
            }

            if (entityType == null)
            {
                throw Error.ArgumentNull("entityType");
            }

            var annotation = model.GetAnnotationValue<BindableProcedureFinder>(model);
            if (annotation == null)
            {
                annotation = new BindableProcedureFinder(model);
                model.SetAnnotationValue(model, annotation);
            }

            return annotation.FindProcedures(entityType);
        }
    }
}
