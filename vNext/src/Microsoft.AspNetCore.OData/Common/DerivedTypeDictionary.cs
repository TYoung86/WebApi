using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace Microsoft.AspNetCore.OData.Common {
	public class DerivedTypeDictionary : IReadOnlyDictionary<Type, IEnumerable<Type>> {
		private readonly IAssemblyProvider _assemblyProvider;

		private readonly ISet<Type> _allTypes;

		private HashSet<Type> GetLoadedTypes() {
			return new HashSet<Type>(OData.TypeHelper.GetLoadedTypes(_assemblyProvider)
				.Where(t => t.GetTypeInfo().IsVisible && t.GetTypeInfo().IsClass && t != typeof(object)));
		}

		private KeyValuePair<Type, IEnumerable<Type>> GetDerivedTypesKvp(Type baseType) {
			return new KeyValuePair<Type, IEnumerable<Type>>(baseType,
				GetDerivedTypes(baseType));
		}

		public IEnumerable<Type> GetDerivedTypes(Type baseType) {
			return _allTypes
				.Where(type => type.GetTypeInfo().BaseType == baseType)
				.ToArray();
		}

		public bool Add(Type baseType) {
			return _allTypes.Add(baseType);
		}

		public IEnumerable<Type> GetOrAdd(Type baseType) {
			IEnumerable<Type> results;
			if (TryGetValue(baseType, out results))
				return results;
			Add(baseType);
			return GetDerivedTypes(baseType);
		}

		public DerivedTypeDictionary(IAssemblyProvider assemblyProvider) {
			_assemblyProvider = assemblyProvider;
			_allTypes = GetLoadedTypes();
		}

		/// <summary>Returns an enumerator that iterates through the collection.</summary>
		/// <returns>An enumerator that can be used to iterate through the collection.</returns>
		public IEnumerator<KeyValuePair<Type, IEnumerable<Type>>> GetEnumerator() {
			return _allTypes.Select(GetDerivedTypesKvp).GetEnumerator();
		}

		/// <summary>Returns an enumerator that iterates through a collection.</summary>
		/// <returns>An <see cref="T:System.Collections.IEnumerator" /> object that can be used to iterate through the collection.</returns>
		IEnumerator IEnumerable.GetEnumerator() {
			return GetEnumerator();
		}

		/// <summary>Gets the number of elements in the collection.</summary>
		/// <returns>The number of elements in the collection. </returns>
		public int Count => _allTypes.Count;

		/// <summary>Determines whether the read-only dictionary contains an element that has the specified key.</summary>
		/// <returns>true if the read-only dictionary contains an element that has the specified key; otherwise, false.</returns>
		/// <param name="key">The key to locate.</param>
		/// <exception cref="T:System.ArgumentNullException">
		/// <paramref name="key" /> is null.</exception>
		public bool ContainsKey(Type key) {
			return _allTypes.Contains(key);
		}

		/// <summary>Gets the value that is associated with the specified key.</summary>
		/// <returns>true if the object that implements the <see cref="T:System.Collections.Generic.IReadOnlyDictionary`2" /> interface contains an element that has the specified key; otherwise, false.</returns>
		/// <param name="key">The key to locate.</param>
		/// <param name="value">When this method returns, the value associated with the specified key, if the key is found; otherwise, the default value for the type of the <paramref name="value" /> parameter. This parameter is passed uninitialized.</param>
		/// <exception cref="T:System.ArgumentNullException">
		/// <paramref name="key" /> is null.</exception>
		public bool TryGetValue(Type key, out IEnumerable<Type> value) {
			if (_allTypes.Contains(key)) {
				value = GetDerivedTypes(key);
				return true;
			}
			value = null;
			return false;
		}

		/// <summary>Gets the element that has the specified key in the read-only dictionary.</summary>
		/// <returns>The element that has the specified key in the read-only dictionary.</returns>
		/// <param name="key">The key to locate.</param>
		/// <exception cref="T:System.ArgumentNullException">
		/// <paramref name="key" /> is null.</exception>
		/// <exception cref="T:System.Collections.Generic.KeyNotFoundException">The property is retrieved and <paramref name="key" /> is not found. </exception>
		public IEnumerable<Type> this[Type key] {
			get {
				IEnumerable<Type> value;
				if (TryGetValue(key, out value))
					return value;
				throw new KeyNotFoundException($"${key.AssemblyQualifiedName} is not available in the given AssemblyProvider");
			}
		}

		/// <summary>Gets an enumerable collection that contains the keys in the read-only dictionary. </summary>
		/// <returns>An enumerable collection that contains the keys in the read-only dictionary.</returns>
		public IEnumerable<Type> Keys => _allTypes;

		/// <summary>Gets an enumerable collection that contains the values in the read-only dictionary.</summary>
		/// <returns>An enumerable collection that contains the values in the read-only dictionary.</returns>
		public IEnumerable<IEnumerable<Type>> Values => _allTypes.Select(GetDerivedTypes);
	}
}
