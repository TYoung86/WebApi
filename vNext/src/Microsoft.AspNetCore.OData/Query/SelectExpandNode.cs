// Copyright (c) Microsoft Corporation.  All rights reserved.
// Licensed under the MIT License.  See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Linq;
using Microsoft.OData.Core;
using Microsoft.OData.Core.UriParser.Semantic;
using Microsoft.OData.Edm;
using Microsoft.AspNetCore.OData.Common;
using Microsoft.AspNetCore.OData.Builder;

namespace Microsoft.AspNetCore.OData.Query
{
    /// <summary>
    /// Describes the set of structural properties and navigation properties and actions to select and navigation properties to expand while 
    /// writing an <see cref="ODataEntry"/> in the response.
    /// </summary>
    public class SelectExpandNode
    {
        /// <summary>
        /// Creates a new instance of the <see cref="SelectExpandNode"/> class.
        /// </summary>
        /// <remarks>The default constructor is for unit testing only.</remarks>
        public SelectExpandNode()
        {
            SelectedStructuralProperties = new HashSet<IEdmStructuralProperty>();
            SelectedNavigationProperties = new HashSet<IEdmNavigationProperty>();
            ExpandedNavigationProperties = new Dictionary<IEdmNavigationProperty, SelectExpandClause>();
            SelectedActions = new HashSet<IEdmAction>();
            SelectedFunctions = new HashSet<IEdmFunction>();
            SelectedDynamicProperties = new HashSet<string>();
        }

        /// <summary>
        /// Creates a new instance of the <see cref="SelectExpandNode"/> class describing the set of structural properties,
        /// navigation properties, and actions to select and expand for the given <paramref name="selectExpandClause"/>.
        /// </summary>
        /// <param name="selectExpandClause">The parsed $select and $expand query options.</param>
        /// <param name="entityType">The entity type of the entry that would be written.</param>
        /// <param name="model">The <see cref="IEdmModel"/> that contains the given entity type.</param>
        public SelectExpandNode(SelectExpandClause selectExpandClause, IEdmEntityType entityType, IEdmModel model)
            : this()
        {
            if (entityType == null)
            {
                throw Error.ArgumentNull("entityType");
            }
            if (model == null)
            {
                throw Error.ArgumentNull("model");
            }

            var allStructuralProperties = new HashSet<IEdmStructuralProperty>(entityType.StructuralProperties());
            var allNavigationProperties = new HashSet<IEdmNavigationProperty>(entityType.NavigationProperties());
            var allActions = new HashSet<IEdmAction>(model.GetAvailableActions(entityType));
            var allFunctions = new HashSet<IEdmFunction>(model.GetAvailableFunctions(entityType));

            if (selectExpandClause == null)
            {
                SelectedStructuralProperties = allStructuralProperties;
                SelectedNavigationProperties = allNavigationProperties;
                SelectedActions = allActions;
                SelectedFunctions = allFunctions;
                SelectAllDynamicProperties = true;
            }
            else
            {
                if (selectExpandClause.AllSelected)
                {
                    SelectedStructuralProperties = allStructuralProperties;
                    SelectedNavigationProperties = allNavigationProperties;
                    SelectedActions = allActions;
                    SelectedFunctions = allFunctions;
                    SelectAllDynamicProperties = true;
                }
                else
                {
                    BuildSelections(selectExpandClause, allStructuralProperties, allNavigationProperties, allActions, allFunctions);
                    SelectAllDynamicProperties = false;
                }

                BuildExpansions(selectExpandClause, allNavigationProperties);

                // remove expanded navigation properties from the selected navigation properties.
                SelectedNavigationProperties.ExceptWith(ExpandedNavigationProperties.Keys);
            }
        }

        /// <summary>
        /// Gets the list of EDM structural properties to be included in the response.
        /// </summary>
        public ISet<IEdmStructuralProperty> SelectedStructuralProperties { get; private set; }

        /// <summary>
        /// Gets the list of EDM navigation properties to be included as links in the response.
        /// </summary>
        public ISet<IEdmNavigationProperty> SelectedNavigationProperties { get; private set; }

        /// <summary>
        /// Gets the list of EDM navigation properties to be expanded in the response.
        /// </summary>
        public IDictionary<IEdmNavigationProperty, SelectExpandClause> ExpandedNavigationProperties { get; private set; }

        /// <summary>
        /// Gets the list of dynamic properties to select.
        /// </summary>
        public ISet<string> SelectedDynamicProperties { get; private set; }

        /// <summary>
        /// Gets the flag to indicate the dynamic property to be included in the response or not.
        /// </summary>
        public bool SelectAllDynamicProperties { get; private set; }

        /// <summary>
        /// Gets the list of OData actions to be included in the response.
        /// </summary>
        public ISet<IEdmAction> SelectedActions { get; private set; }

        /// <summary>
        /// Gets the list of OData functions to be included in the response.
        /// </summary>
        public ISet<IEdmFunction> SelectedFunctions { get; private set; }

        private void BuildExpansions(SelectExpandClause selectExpandClause, HashSet<IEdmNavigationProperty> allNavigationProperties)
        {
            foreach (var selectItem in selectExpandClause.SelectedItems)
            {
                var expandItem = selectItem as ExpandedNavigationSelectItem;
                if (expandItem != null)
                {
                    ValidatePathIsSupported(expandItem.PathToNavigationProperty);
                    var navigationSegment = (NavigationPropertySegment)expandItem.PathToNavigationProperty.LastSegment;
                    var navigationProperty = navigationSegment.NavigationProperty;
                    if (allNavigationProperties.Contains(navigationProperty))
                    {
                        ExpandedNavigationProperties.Add(navigationProperty, expandItem.SelectAndExpand);
                    }
                }
            }
        }

        private void BuildSelections(
            SelectExpandClause selectExpandClause,
            HashSet<IEdmStructuralProperty> allStructuralProperties,
            HashSet<IEdmNavigationProperty> allNavigationProperties,
            HashSet<IEdmAction> allActions,
            HashSet<IEdmFunction> allFunctions)
        {
            foreach (var selectItem in selectExpandClause.SelectedItems)
            {
                if (selectItem is ExpandedNavigationSelectItem)
                {
                    continue;
                }

                var pathSelectItem = selectItem as PathSelectItem;

                if (pathSelectItem != null)
                {
                    ValidatePathIsSupported(pathSelectItem.SelectedPath);
                    var segment = pathSelectItem.SelectedPath.LastSegment;

                    var navigationPropertySegment = segment as NavigationPropertySegment;
                    if (navigationPropertySegment != null)
                    {
                        var navigationProperty = navigationPropertySegment.NavigationProperty;
                        if (allNavigationProperties.Contains(navigationProperty))
                        {
                            SelectedNavigationProperties.Add(navigationProperty);
                        }
                        continue;
                    }

                    var structuralPropertySegment = segment as PropertySegment;
                    if (structuralPropertySegment != null)
                    {
                        var structuralProperty = structuralPropertySegment.Property;
                        if (allStructuralProperties.Contains(structuralProperty))
                        {
                            SelectedStructuralProperties.Add(structuralProperty);
                        }
                        continue;
                    }

                    var operationSegment = segment as OperationSegment;
                    if (operationSegment != null)
                    {
                        AddOperations(allActions, allFunctions, operationSegment);
                        continue;
                    }

                    var openPropertySegment = segment as OpenPropertySegment;
                    if (openPropertySegment != null)
                    {
                        SelectedDynamicProperties.Add(openPropertySegment.PropertyName);
                        continue;
                    }
                    throw new ODataException(Error.Format(SRResources.SelectionTypeNotSupported, segment.GetType().Name));
                }

                var wildCardSelectItem = selectItem as WildcardSelectItem;
                if (wildCardSelectItem != null)
                {
                    SelectedStructuralProperties = allStructuralProperties;
                    SelectedNavigationProperties = allNavigationProperties;
                    continue;
                }

                var wildCardActionSelection = selectItem as NamespaceQualifiedWildcardSelectItem;
                if (wildCardActionSelection != null)
                {
                    SelectedActions = allActions;
                    SelectedFunctions = allFunctions;
                    continue;
                }

                throw new ODataException(Error.Format(SRResources.SelectionTypeNotSupported, selectItem.GetType().Name));
            }
        }

        private void AddOperations(HashSet<IEdmAction> allActions, HashSet<IEdmFunction> allFunctions, OperationSegment operationSegment)
        {
            foreach (var operation in operationSegment.Operations)
            {
                var action = operation as IEdmAction;
                if (action != null && allActions.Contains(action))
                {
                    SelectedActions.Add(action);
                }

                var function = operation as IEdmFunction;
                if (function != null && allFunctions.Contains(function))
                {
                    SelectedFunctions.Add(function);
                }
            }
        }

        // we only support paths of type 'cast/structuralOrNavPropertyOrAction' and 'structuralOrNavPropertyOrAction'.
        internal static void ValidatePathIsSupported(ODataPath path)
        {
            var segmentCount = path.Count();

            if (segmentCount > 2)
            {
                throw new ODataException(SRResources.UnsupportedSelectExpandPath);
            }

            if (segmentCount == 2)
            {
                if (!(path.FirstSegment is TypeSegment))
                {
                    throw new ODataException(SRResources.UnsupportedSelectExpandPath);
                }
            }

            var lastSegment = path.LastSegment;
            if (!(lastSegment is NavigationPropertySegment
                || lastSegment is PropertySegment
                || lastSegment is OperationSegment
                || lastSegment is OpenPropertySegment))
            {
                throw new ODataException(SRResources.UnsupportedSelectExpandPath);
            }
        }
    }
}
