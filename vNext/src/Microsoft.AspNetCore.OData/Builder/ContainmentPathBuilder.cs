﻿// Copyright (c) Microsoft Corporation.  All rights reserved.
// Licensed under the MIT License.  See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Linq;
using Microsoft.OData.Core.UriParser.Semantic;
using Microsoft.OData.Edm;

namespace Microsoft.AspNetCore.OData.Builder
{
    internal class ContainmentPathBuilder
    {
        private List<ODataPathSegment> _segments;

        public ODataPath TryComputeCanonicalContainingPath(ODataPath path)
        {
            Contract.Assert(path != null);
            Contract.Assert(path.Count >= 2);

            _segments = path.ToList();

            RemoveAllTypeCasts();

            // New ODataPath will be extended later to include any final required key or cast.
            RemovePathSegmentsAfterTheLastNavigationProperty();

            RemoveRedundantContainingPathSegments();

            AddTypeCastsIfNecessary();

            // Also remove the last navigation property segment, since it is not part of the containing path segments.
            if (_segments.Count > 0)
            {
                _segments.RemoveAt(_segments.Count - 1);
            }

            return new ODataPath(_segments);
        }

        private void RemovePathSegmentsAfterTheLastNavigationProperty()
        {
            // Find the last navigation property segment.
            ODataPathSegment lastNavigationProperty = _segments.OfType<NavigationPropertySegment>().LastOrDefault();
            var newSegments = new List<ODataPathSegment>();
            foreach (var segment in _segments)
            {
                newSegments.Add(segment);
                if (segment == lastNavigationProperty)
                {
                    break;
                }
            }

            _segments = newSegments;
        }

        private void RemoveRedundantContainingPathSegments()
        {
            // Find the last non-contained navigation property segment:
            //   Collection valued: entity set
            //   -or-
            //   Single valued: singleton
            // Copy over other path segments such as: not a navigation path segment, contained navigation property,
            // single valued navigation property with navigation source targetting an entity set (we won't have key
            // information for that navigation property.)
            _segments.Reverse();
            NavigationPropertySegment navigationPropertySegment = null;
            var newSegments = new List<ODataPathSegment>();
            foreach (var segment in _segments)
            {
                navigationPropertySegment = segment as NavigationPropertySegment;
                if (navigationPropertySegment != null)
                {
                    var navigationSourceKind =
                        navigationPropertySegment.NavigationSource.NavigationSourceKind();
                    if ((navigationPropertySegment.NavigationProperty.TargetMultiplicity() == EdmMultiplicity.Many &&
                         navigationSourceKind == EdmNavigationSourceKind.EntitySet) ||
                        (navigationSourceKind == EdmNavigationSourceKind.Singleton))
                    {
                        break;
                    }
                }

                newSegments.Insert(0, segment);
            }

            // Start the path with the navigation source of the navigation property found above.
            if (navigationPropertySegment != null)
            {
                var navigationSource = navigationPropertySegment.NavigationSource;
                Contract.Assert(navigationSource != null);
                if (navigationSource.NavigationSourceKind() == EdmNavigationSourceKind.Singleton)
                {
                    var singletonSegment = new SingletonSegment((IEdmSingleton)navigationSource);
                    newSegments.Insert(0, singletonSegment);
                }
                else
                {
                    Contract.Assert(navigationSource.NavigationSourceKind() == EdmNavigationSourceKind.EntitySet);
                    var entitySetSegment = new EntitySetSegment((IEdmEntitySet)navigationSource);
                    newSegments.Insert(0, entitySetSegment);
                }
            }

            _segments = newSegments;
        }

        private void RemoveAllTypeCasts()
        {
            var newSegments = new List<ODataPathSegment>();
            foreach (var segment in _segments)
            {
                if (!(segment is TypeSegment))
                {
                    newSegments.Add(segment);
                }
            }

            _segments = newSegments;
        }

        private void AddTypeCastsIfNecessary()
        {
            IEdmEntityType owningType = null;
            var newSegments = new List<ODataPathSegment>();
            foreach (var segment in _segments)
            {
                var navProp = segment as NavigationPropertySegment;
                if (navProp != null && owningType != null &&
                    owningType.FindProperty(navProp.NavigationProperty.Name) == null)
                {
                    // need a type cast
                    var typeCast = new TypeSegment(
                        navProp.NavigationProperty.DeclaringType,
                        navigationSource: null);
                    newSegments.Add(typeCast);
                }

                newSegments.Add(segment);
                var targetEntityType = GetTargetEntityType(segment);
                if (targetEntityType != null)
                {
                    owningType = targetEntityType;
                }
            }

            _segments = newSegments;
        }

        private static IEdmEntityType GetTargetEntityType(ODataPathSegment segment)
        {
            Contract.Assert(segment != null);

            var entitySetSegment = segment as EntitySetSegment;
            if (entitySetSegment != null)
            {
                return entitySetSegment.EntitySet.EntityType();
            }

            var singletonSegment = segment as SingletonSegment;
            if (singletonSegment != null)
            {
                return singletonSegment.Singleton.EntityType();
            }

            var navigationPropertySegment = segment as NavigationPropertySegment;
            if (navigationPropertySegment != null)
            {
                return navigationPropertySegment.NavigationSource.EntityType();
            }

            return null;
        }
    }
}
