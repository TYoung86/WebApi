﻿// Copyright (c) Microsoft Corporation.  All rights reserved.
// Licensed under the MIT License.  See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Linq;
using System.Reflection;
using Microsoft.AspNetCore.OData.Common;
using Microsoft.AspNetCore.OData.Extensions;
using Microsoft.AspNetCore.OData.Routing;
using Microsoft.OData.Edm;

namespace Microsoft.AspNetCore.OData {
	/// <summary>
	/// This defines some context information used to perform query composition. 
	/// </summary>
	public class ODataQueryContext {
		/// <summary>
		/// Constructs an instance of <see cref="ODataQueryContext"/> with <see cref="IEdmModel" />, element CLR type,
		/// and <see cref="ODataPath" />.
		/// </summary>
		/// <param name="model">The EdmModel that includes the <see cref="IEdmType"/> corresponding to
		/// the given <paramref name="elementClrType"/>.</param>
		/// <param name="elementClrType">The CLR type of the element of the collection being queried.</param>
		/// <param name="path">The parsed <see cref="ODataPath"/>.</param>
		public ODataQueryContext(IEdmModel model, Type elementClrType, ODataPath path) {
			if (model == null) {
				throw Error.ArgumentNull("model");
			}

			if (elementClrType == null) {
				throw Error.ArgumentNull("elementClrType");
			}

			ElementType = model.GetEdmType(elementClrType);

			if (ElementType == null) {
				var ti = elementClrType.GetTypeInfo();
				ElementType = model.GetEdmType(ti.BaseType);
				if (ElementType == null)
					foreach (var intf in ti.GetInterfaces()) {
						ElementType = model.GetEdmType(intf);
						if (ElementType != null) break;
					}
			}


			if (ElementType == null) {
				throw Error.Argument("elementClrType", SRResources.ClrTypeNotInModel, elementClrType.FullName);
			}

			ElementClrType = elementClrType;
			Model = model;
			Path = path;
			NavigationSource = GetNavigationSource(Model, ElementType, path);
		}

		/// <summary>
		/// Constructs an instance of <see cref="ODataQueryContext"/> with <see cref="IEdmModel" />, element EDM type,
		/// and <see cref="ODataPath" />.
		/// </summary>
		/// <param name="model">The EDM model the given EDM type belongs to.</param>
		/// <param name="elementType">The EDM type of the element of the collection being queried.</param>
		/// <param name="path">The parsed <see cref="ODataPath"/>.</param>
		public ODataQueryContext(IEdmModel model, IEdmType elementType, ODataPath path) {
			if (model == null) {
				throw Error.ArgumentNull("model");
			}
			if (elementType == null) {
				throw Error.ArgumentNull("elementType");
			}

			Model = model;
			ElementType = elementType;
			Path = path;
			NavigationSource = GetNavigationSource(Model, ElementType, path);
		}

		internal ODataQueryContext(IEdmModel model, Type elementClrType)
			: this(model, elementClrType, path: null) {
		}

		internal ODataQueryContext(IEdmModel model, IEdmType elementType)
			: this(model, elementType, path: null) {
		}

		/// <summary>
		/// Gets the given <see cref="IEdmModel"/> that contains the EntitySet.
		/// </summary>
		public IEdmModel Model { get; private set; }

		/// <summary>
		/// Gets the <see cref="IEdmType"/> of the element.
		/// </summary>
		public IEdmType ElementType { get; private set; }

		/// <summary>
		/// Gets the <see cref="IEdmNavigationSource"/> that contains the element.
		/// </summary>
		public IEdmNavigationSource NavigationSource { get; private set; }

		/// <summary>
		/// Gets the CLR type of the element.
		/// </summary>
		public Type ElementClrType { get; private set; }

		/// <summary>
		/// Gets the <see cref="ODataPath"/>.
		/// </summary>
		public ODataPath Path { get; private set; }

		private static IEdmNavigationSource GetNavigationSource(IEdmModel model, IEdmType elementType, ODataPath odataPath) {
			Contract.Assert(model != null);
			Contract.Assert(elementType != null);

			var navigationSource = (odataPath != null) ? odataPath.NavigationSource : null;
			if (navigationSource != null) {
				return navigationSource;
			}

			var entityContainer = model.EntityContainer;
			if (entityContainer == null) {
				return null;
			}

			var matchedNavigationSources =
				entityContainer.EntitySets().Where(e => e.EntityType() == elementType).ToList();

			return (matchedNavigationSources.Count != 1) ? null : matchedNavigationSources[0];
		}
	}
}
