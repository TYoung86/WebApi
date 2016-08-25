// Copyright (c) Microsoft Corporation.  All rights reserved.
// Licensed under the MIT License.  See License.txt in the project root for license information.

using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.OData.Core;
using Microsoft.OData.Core.UriParser.Semantic;
using Microsoft.OData.Edm;
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.OData.Common;

namespace Microsoft.AspNetCore.OData.Query.Validators
{


    /// <summary>
    /// Represents a validator used to validate a <see cref="SelectExpandQueryOption" /> based on the <see cref="ODataValidationSettings"/>.
    /// </summary>
    public class SelectExpandQueryValidator
    {
        /// <summary>
        /// Validates a <see cref="TopQueryOption" />.
        /// </summary>
        /// <param name="selectExpandQueryOption">The $select and $expand query.</param>
        /// <param name="validationSettings">The validation settings.</param>
        public virtual void Validate(SelectExpandQueryOption selectExpandQueryOption, ODataValidationSettings validationSettings)
        {
            if (selectExpandQueryOption == null)
            {
                throw Error.ArgumentNull("selectExpandQueryOption");
            }

            if (validationSettings == null)
            {
                throw Error.ArgumentNull("validationSettings");
            }

            var model = selectExpandQueryOption.Context.Model;
            ValidateRestrictions(selectExpandQueryOption.SelectExpandClause, model);

            if (validationSettings.MaxExpansionDepth > 0)
            {
                if (selectExpandQueryOption.LevelsMaxLiteralExpansionDepth > validationSettings.MaxExpansionDepth)
                {
                    throw new ODataException(Error.Format(
                        SRResources.InvalidExpansionDepthValue,
                        "LevelsMaxLiteralExpansionDepth",
                        "MaxExpansionDepth"));
                }

                ValidateDepth(selectExpandQueryOption.SelectExpandClause, validationSettings.MaxExpansionDepth);
            }
        }

        private static void ValidateDepth(SelectExpandClause selectExpand, int maxDepth)
        {
            // do a DFS to see if there is any node that is too deep.
            var nodesToVisit = new Stack<Tuple<int, SelectExpandClause>>();
            nodesToVisit.Push(Tuple.Create(0, selectExpand));
            while (nodesToVisit.Count > 0)
            {
                var tuple = nodesToVisit.Pop();
                var currentDepth = tuple.Item1;
                var currentNode = tuple.Item2;

                var expandItems = currentNode.SelectedItems.OfType<ExpandedNavigationSelectItem>().ToArray();

                if (expandItems.Length > 0 &&
                    ((currentDepth == maxDepth &&
                    expandItems.Any(expandItem =>
                        expandItem.LevelsOption == null ||
                        expandItem.LevelsOption.IsMaxLevel ||
                        expandItem.LevelsOption.Level != 0)) ||
                    expandItems.Any(expandItem =>
                        expandItem.LevelsOption != null &&
                        !expandItem.LevelsOption.IsMaxLevel &&
                        (expandItem.LevelsOption.Level > Int32.MaxValue ||
                        expandItem.LevelsOption.Level + currentDepth > maxDepth))))
                {
                    throw new ODataException(
                        Error.Format(SRResources.MaxExpandDepthExceeded, maxDepth, "MaxExpansionDepth"));
                }

                foreach (var expandItem in expandItems)
                {
                    var depth = currentDepth + 1;

                    if (expandItem.LevelsOption != null && !expandItem.LevelsOption.IsMaxLevel)
                    {
                        // Add the value of $levels for next depth.
                        depth = depth + (int)expandItem.LevelsOption.Level - 1;
                    }

                    nodesToVisit.Push(Tuple.Create(depth, expandItem.SelectAndExpand));
                }
            }
        }

        private static void ValidateRestrictions(SelectExpandClause selectExpandClause, IEdmModel edmModel)
        {
            foreach (var selectItem in selectExpandClause.SelectedItems)
            {
                var expandItem = selectItem as ExpandedNavigationSelectItem;
                if (expandItem != null)
                {
                    var navigationSegment = (NavigationPropertySegment)expandItem.PathToNavigationProperty.LastSegment;
                    var navigationProperty = navigationSegment.NavigationProperty;
                    if (EdmLibHelpers.IsNotExpandable(navigationProperty, edmModel))
                    {
                        throw new ODataException(Error.Format(SRResources.NotExpandablePropertyUsedInExpand, navigationProperty.Name));
                    }
                    ValidateRestrictions(expandItem.SelectAndExpand, edmModel);
                }

                var pathSelectItem = selectItem as PathSelectItem;
                if (pathSelectItem != null)
                {
                    var segment = pathSelectItem.SelectedPath.LastSegment;
                    var navigationPropertySegment = segment as NavigationPropertySegment;
                    if (navigationPropertySegment != null)
                    {
                        var navigationProperty = navigationPropertySegment.NavigationProperty;
                        if (EdmLibHelpers.IsNotNavigable(navigationProperty, edmModel))
                        {
                            throw new ODataException(Error.Format(SRResources.NotNavigablePropertyUsedInNavigation, navigationProperty.Name));
                        }
                    }
                }
            }
        }
    }
}
