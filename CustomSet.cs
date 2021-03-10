namespace Project.Collection
{
	using System.Diagnostics.CodeAnalysis;



	public interface IReadonlyKeySet<TKey> where TKey : notnull
	{
		int Version{ get;  }
		int Count{ get; }
		bool Contains( TKey key );
		bool Match( IReadonlyKeySet<TKey> set );
		KeyEnumerator<TKey> GetEnumerator();
	}



	/// <summary>
	/// MUST BE USED IN PLACE OF HASHSETS AND SIMILAR COLLECTION WITHIN DETERMINISTIC LOGIC
	/// </summary>
	public class KeySet<TKey> : System.Collections.Generic.ISet<TKey>, IReadonlyKeySet<TKey>, System.Collections.Generic.IReadOnlyCollection<TKey> where TKey : notnull
	{
		public int Count => _internalCollection.Count;
		public int Version => _internalCollection.Version;
		public IDeterministicEqualityComparer<TKey> Comparer => _internalCollection.Comparer;
		readonly KeyValue<TKey, Empty> _internalCollection;



		public struct Empty
		{
		}



		public KeySet( IDeterministicEqualityComparer<TKey> comparer, int capacity = 0 )
		{
			_internalCollection = new KeyValue<TKey, Empty>( comparer, capacity );
		}



		public bool Add( TKey key )
		{
			if( _internalCollection.ContainsKey( key ) )
				return false;
			_internalCollection.Add( key, new Empty() );
			return true;
		}



		public void Clear() => _internalCollection.Clear();
		public bool Remove( TKey key ) => _internalCollection.Remove( key );
		public bool Contains( TKey key ) => _internalCollection.ContainsKey( key );



		public bool Match( IReadonlyKeySet<TKey> b )
		{
			if( Count != b.Count )
				return false;

			foreach( TKey visibleTile in b )
			{
				if( Contains( visibleTile ) == false )
				{
					return false;
				}
			}

			return true;
		}



		public KeyEnumerator<TKey> GetEnumerator() => new KeyEnumerator<TKey>( _internalCollection );
		System.Collections.Generic.IEnumerator<TKey> System.Collections.Generic.IEnumerable<TKey>.GetEnumerator() => GetEnumerator();
		System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();





		bool System.Collections.Generic.ICollection<TKey>.IsReadOnly => false;
		bool System.Collections.Generic.ISet<TKey>.Add( TKey key ) => Add( key );
		void System.Collections.Generic.ISet<TKey>.ExceptWith( System.Collections.Generic.IEnumerable<TKey> other ) => throw new System.NotImplementedException();
		void System.Collections.Generic.ISet<TKey>.IntersectWith( System.Collections.Generic.IEnumerable<TKey> other ) => throw new System.NotImplementedException();
		bool System.Collections.Generic.ISet<TKey>.IsProperSubsetOf( System.Collections.Generic.IEnumerable<TKey> other ) => throw new System.NotImplementedException();
		bool System.Collections.Generic.ISet<TKey>.IsProperSupersetOf( System.Collections.Generic.IEnumerable<TKey> other ) => throw new System.NotImplementedException();
		bool System.Collections.Generic.ISet<TKey>.IsSubsetOf( System.Collections.Generic.IEnumerable<TKey> other ) => throw new System.NotImplementedException();
		bool System.Collections.Generic.ISet<TKey>.IsSupersetOf( System.Collections.Generic.IEnumerable<TKey> other ) => throw new System.NotImplementedException();
		bool System.Collections.Generic.ISet<TKey>.Overlaps( System.Collections.Generic.IEnumerable<TKey> other ) => throw new System.NotImplementedException();
		bool System.Collections.Generic.ISet<TKey>.SetEquals( System.Collections.Generic.IEnumerable<TKey> other ) => throw new System.NotImplementedException();
		void System.Collections.Generic.ISet<TKey>.SymmetricExceptWith( System.Collections.Generic.IEnumerable<TKey> other ) => throw new System.NotImplementedException();
		void System.Collections.Generic.ISet<TKey>.UnionWith( System.Collections.Generic.IEnumerable<TKey> other )
		{
			foreach( TKey key in other )
				Add( key );
		}



		void System.Collections.Generic.ICollection<TKey>.Add( TKey key )
		{
			if( Add( key ) == false )
				throw new System.InvalidOperationException( $"{nameof(key)} '{key}' does not exists and {nameof(System.Collections.Generic.ICollection<TKey>)}'s Add() implies throwing in such case" );
		}
		void System.Collections.Generic.ICollection<TKey>.CopyTo( TKey[] array, int arrayIndex )
		{
			if ( arrayIndex < 0 || arrayIndex > array.Length || array.Length - arrayIndex < Count )
				throw new System.ArgumentOutOfRangeException();

			foreach( var key in GetEnumerator() )
				array[ arrayIndex++ ] = key;
		}
	}



	public struct KeyEnumerator<TKey> :
		System.Collections.Generic.IEnumerator<TKey>,
		// Itself an enumerable, easier if you just want to pass read-only access, don't pass the collection but this instead
		System.Collections.Generic.IEnumerable<TKey> where TKey : notnull
	{
		KeyValue<TKey, KeySet<TKey>.Empty> _source;
		int _version;
		int _index;
		TKey _current;



		public KeyEnumerator( KeyValue<TKey, KeySet<TKey>.Empty> baseKeySetValues )
		{
			_source = baseKeySetValues;
			_version = baseKeySetValues.Version;
			_index = 0;
			_current = default!;
		}



		public bool MoveNext()
		{
			if( _version != _source.Version )
				throw new System.InvalidOperationException( $"{_source.GetType()} changed while iterating over it" );

			var entries = _source._entries;

			// Use unsigned comparison since we set index to dictionary.count+1 when the enumeration ends.
			// dictionary.count+1 could be negative if dictionary.count is Int32.MaxValue
			while( (uint) _index < (uint) _source._populated )
			{
				if( entries[ _index ].HashCode >= 0 )
				{
					ref var entry = ref entries[ _index ];
					_current = entry.Key;
					_index++;
					return true;
				}

				_index++;
			}

			_index = _source._populated + 1;
			return false;
		}



		public int GetCurrentIndex() => _index - 1;



		public TKey Current => _current;
		object System.Collections.IEnumerator.Current => _current!;
		TKey System.Collections.Generic.IEnumerator<TKey>.Current => Current;

		public KeyEnumerator<TKey> GetEnumerator() => this;
		System.Collections.Generic.IEnumerator<TKey> System.Collections.Generic.IEnumerable<TKey>.GetEnumerator() => GetEnumerator();
		System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();



		void System.IDisposable.Dispose()
		{
		}



		void System.Collections.IEnumerator.Reset()
		{
			_version = _source.Version;
			_index = 0;
		}
	}



	/// <summary>
	/// When you inherit from this interface you guarantee that the class will always return deterministic results
	/// that are based on the content of the given key object.
	/// </summary>
	public interface IDeterministicEqualityComparer<in TKey> : System.Collections.Generic.IEqualityComparer<TKey>
	{

	}


	/// <summary> Static class to create <see cref="KeyValues{TKey,TValue}"/> with a couple of default setup to avoid some boilerplate logic </summary>
	public static class KeyValues
	{
		/// <summary> Create a new deterministic instance with unmanaged data as keys, data equality will be ensured by binary comparison </summary>
		public static KeyValues<TKey, TValue> New<TKey, TValue>( int capacity = 0 ) where TKey : unmanaged, System.IEquatable<TKey>
			=> new KeyValues<TKey, TValue>( UnmanagedComparer<TKey>.Instance, capacity );
		/// <summary> Create a new deterministic instance whose key is a string </summary>
		public static KeyValues<string, TValue> NewString<TValue>( int capacity = 0 )
			=> new KeyValues<string, TValue>( StringComparer.Instance, capacity );
		/// <summary> Create a new deterministic instance with a specific equality comparer </summary>
		public static KeyValues<TKey, TValue> NewCustom<TKey, TValue>( IDeterministicEqualityComparer<TKey> comparer, int capacity = 0 ) where TKey : notnull
			=> new KeyValues<TKey, TValue>( comparer, capacity );
	}



	/// <summary>
	/// Key-value dictionary, does not allow duplicate keys.
	/// <para/>
	/// <inheritdoc cref="Project.Collection.BaseKeyValues{TKey,TValue}" />
	/// </summary>
	public class KeyValue<TKey, TValue> : BaseKeyValues<TKey, TValue>, System.Collections.Generic.IDictionary<TKey, TValue> where TKey : notnull
	{
		public TValue this[ TKey key ]
		{
			get
			{
				var entry = FindEntry( key );
				if( entry < 0 )
					throw new System.InvalidOperationException( $"{nameof(key)} '{key}' does not exists" );

				return _entries[ entry ].Value;
			}
			set
			{
				var entry = FindEntry( key );
				if( entry < 0 )
					throw new System.InvalidOperationException( $"{nameof(key)} '{key}' does not exists" );

				_entries[ entry ].Value = value;
			}
		}



		public KeyValue( IDeterministicEqualityComparer<TKey> comparer, int capacity = 0 ) : base(comparer, capacity)
		{
		}



		public override void Add( TKey key, TValue value )
		{
			if( ContainsKey( key ) )
				throw new System.InvalidOperationException( $"{nameof(key)} '{key}' already exists" );
			base.Add( key, value );
		}



		public override void ReplaceContentWith( BaseKeyValues<TKey, TValue> source )
		{
			if( source is KeyValue<TKey,TValue> == false )
				throw new System.ArgumentException( $"{nameof(source)} is not a {nameof(KeyValue<TKey,TValue>)}, potential for duplicate key not allowed" );
			base.ReplaceContentWith( source );
		}



		// ICollection don't make any sense for dictionaries, what the hell dot net ! Dictionaries should be IEnumerable<T1/T2> not ICollection<T1/T2>
		System.Collections.Generic.ICollection<TKey> System.Collections.Generic.IDictionary<TKey, TValue>.Keys => throw new System.NotImplementedException();
		System.Collections.Generic.ICollection<TValue> System.Collections.Generic.IDictionary<TKey, TValue>.Values => throw new System.NotImplementedException();
		void System.Collections.Generic.ICollection<System.Collections.Generic.KeyValuePair<TKey, TValue>>.CopyTo( System.Collections.Generic.KeyValuePair<TKey, TValue>[] array, int arrayIndex )
		{
			if ( arrayIndex < 0 || arrayIndex > array.Length || array.Length - arrayIndex < Count )
				throw new System.ArgumentOutOfRangeException();

			foreach( (TKey key, TValue value) in GetEnumerator() )
				array[ arrayIndex++ ] = new System.Collections.Generic.KeyValuePair<TKey, TValue>( key, value );
		}
		bool System.Collections.Generic.ICollection<System.Collections.Generic.KeyValuePair<TKey, TValue>>.IsReadOnly => false;
		void System.Collections.Generic.ICollection<System.Collections.Generic.KeyValuePair<TKey, TValue>>.Add( System.Collections.Generic.KeyValuePair<TKey, TValue> item ) => Add( item.Key, item.Value );
		bool System.Collections.Generic.ICollection<System.Collections.Generic.KeyValuePair<TKey, TValue>>.Remove( System.Collections.Generic.KeyValuePair<TKey, TValue> item ) => Remove( item.Key );
		bool System.Collections.Generic.ICollection<System.Collections.Generic.KeyValuePair<TKey, TValue>>.Contains( System.Collections.Generic.KeyValuePair<TKey, TValue> item ) => ContainsKey( item.Key );
		System.Collections.Generic.IEnumerator<System.Collections.Generic.KeyValuePair<TKey, TValue>> System.Collections.Generic.IEnumerable<System.Collections.Generic.KeyValuePair<TKey, TValue>>.GetEnumerator() => GetEnumerator();
	}



	/// <summary>
	/// Key : multiple-values dictionary, enumerates all values with the same key through <see cref="KeyValues{TKey, TValue}.this[TKey]"/>
	/// <para/>
	/// <inheritdoc/>
	/// </summary>
	public class KeyValues<TKey, TValue> : BaseKeyValues<TKey, TValue> where TKey : notnull
	{
		public KeyValues( IDeterministicEqualityComparer<TKey> comparer, int capacity = 0 ) : base(comparer, capacity) {}
		public ValuesEnumerator this[ TKey key ] => new ValuesEnumerator( this, key );
		public struct ValuesEnumerator
		{
			KeyValues<TKey, TValue> _coll;
			int _version;
			int _nextI, _currentI, _previousI;
			TKey _key;
			int _hashCode;



			internal ValuesEnumerator( KeyValues<TKey, TValue> coll, TKey key )
			{
				_coll = coll;
				_version = coll.Version;
				_key = key;
				_hashCode = _coll.Comparer.GetHashCode( key ) & 0x7FFFFFFF;
				var buckets = _coll._buckets;
				_nextI = buckets.Length != 0 ? buckets[ _hashCode % buckets.Length ] : -1;
				_previousI = - 1;
				_currentI = - 1;
			}



			public void Reset()
			{
				_version = _coll.Version;
				var buckets = _coll._buckets;
				_nextI = buckets.Length != 0 ? buckets[ _hashCode % buckets.Length ] : -1;
				_previousI = - 1;
				_currentI = - 1;
			}



			public ref TValue Current
			{
				get
				{
					if( _version != _coll.Version )
						throw new System.InvalidOperationException( $"{_coll.GetType()} changed while iterating over it" );

					if( _currentI >= 0 )
						return ref _coll._entries[ _currentI ].Value;

					throw new System.InvalidOperationException( $"Cannot call {nameof( Current )} before {nameof( MoveNext )}" );
				}
			}



			public bool MoveNext()
			{
				if( _version != _coll.Version )
					throw new System.InvalidOperationException( $"{_coll.GetType()} changed while iterating over it" );

				var entries = _coll._entries;
				var comparer = _coll.Comparer;
				while( _nextI >= 0 )
				{
					_previousI = _currentI;
					_currentI = _nextI;
					_nextI = entries[ _nextI ].Next;

					ref var entry = ref entries[ _currentI ];
					if( entry.HashCode == _hashCode && comparer.Equals( entry.Key, _key ) )
						return true;
				}

				_previousI = _currentI = _nextI = -1;
				return false;
			}



			/// <summary>
			/// Remove the current value from the <see cref="BaseKeyValues{TKey,TValue}"/>
			/// </summary>
			public void RemoveCurrent()
			{
				if( _version != _coll.Version )
					throw new System.InvalidOperationException( $"{_coll.GetType()} changed while iterating over it" );

				if( _currentI < 0 )
					throw new System.InvalidOperationException( $"Cannot remove current as {nameof( MoveNext )} has not been called yet" );

				_coll.RemoveUnsafe( _previousI, _currentI, _hashCode );

				_version = _coll.Version;
			}



			public ValuesEnumerator GetEnumerator() => this;
		}
	}



	/// <summary>
	/// Dictionaries, hashsets and other types using GetHashCode() do not guarantee determinism,
	/// this type and its inheritors must be used in place of those types to avoid determinism issues.
	/// <para/>
	/// See project readme for more information.
	/// </summary>
	public abstract class BaseKeyValues<TKey, TValue> : System.Collections.Generic.IEnumerable<(TKey Key, TValue Value)> where TKey : notnull
	{
		public int Count => _populated - _freeCount;
		public int Version{ get; private set; }
		public readonly IDeterministicEqualityComparer<TKey> Comparer;

		// ReSharper disable InconsistentNaming
		protected int[] _buckets = new int[0];
		internal Entry[] _entries = new Entry[0];
		internal int _populated;
		protected int _freeList;
		protected int _freeCount;
		protected readonly bool _keyIsStruct;
		// ReSharper restore InconsistentNaming



		protected BaseKeyValues( IDeterministicEqualityComparer<TKey> comparer, int capacity = 0 )
		{
			if( capacity < 0 )
				throw new System.ArgumentException( nameof( capacity ) );
			if( capacity > 0 )
				Initialize( capacity );
			Comparer = comparer;
			_keyIsStruct = typeof(TKey).IsValueType;
		}



		public virtual void Add( TKey key, TValue value )
		{
			if( _keyIsStruct == false && key == null )
				throw new System.ArgumentException( nameof( key ) );

			if( _buckets.Length == 0 )
				Initialize( 0 );

			int hashCode = Comparer.GetHashCode(key) & 0x7FFFFFFF;
			int targetBucket = hashCode % _buckets.Length;

			int index;
			if( _freeCount > 0 )
			{
				index = _freeList;
				_freeList = _entries[ index ].Next;
				_freeCount--;
			}
			else
			{
				if( _populated == _entries.Length )
				{
					Resize();
					targetBucket = hashCode % _buckets.Length;
				}

				index = _populated;
				_populated++;
			}

			ref var entry = ref _entries[ index ];
			ref var bucket = ref _buckets[ targetBucket ];
			entry.HashCode = hashCode;
			entry.Next = bucket;
			entry.Key = key;
			entry.Value = value;
			bucket = index;
			Version++;
		}



		public void Clear()
		{
			if( _populated <= 0 )
				return;

			for( int i = 0; i < _buckets.Length; i++ )
				_buckets[ i ] = - 1;

			System.Array.Clear( _entries, 0, _populated );
			_freeList = - 1;
			_populated = 0;
			_freeCount = 0;
			Version++;
		}



		public bool TryGetValue( TKey key, [MaybeNullWhen(false)] out TValue value )
		{
			var entry = FindEntry( key );
			if( entry < 0 )
			{
				value = default;
				return false;
			}

			value = _entries[ entry ].Value!;
			return true;
		}



		public bool Remove( TKey key )
		{
			if( _keyIsStruct == false && key == null )
				throw new System.ArgumentException( nameof( key ) );

			if( _buckets.Length == 0 )
				return false;

			bool removed = false;
			int hashCode = Comparer.GetHashCode( key ) & 0x7FFFFFFF;
			int bucket = hashCode % _buckets.Length;
			int last = - 1;
			RESTART:
			for( int i = _buckets[ bucket ]; i >= 0; last = i, i = _entries[ i ].Next )
			{
				// Try to remove the next few items together if they match the key to avoid having to pass through links again
				if( TryRemoveSuite( last, i, hashCode, key ) )
				{
					removed = true;
					goto RESTART;
				}
			}

			return removed;
		}



		public bool ContainsKey( TKey key ) => FindEntry( key ) >= 0;



		public virtual void ReplaceContentWith( BaseKeyValues<TKey,TValue> source )
		{
			if( ReferenceEquals( source.Comparer, Comparer ) == false )
				throw new System.ArgumentException( "Comparer between the two are not the same object, cannot guarantee that generated hashcodes will match" );

			if( _entries.Length == 0 && source._entries.Length == 0 )
				return;

			Clear();
			if( _buckets.Length == 0 )
				Initialize( 0 );
			if( source._entries.Length > _entries.Length )
				Resize( source._entries.Length );
			System.Array.Copy( source._entries, 0, _entries, 0, source._entries.Length );
			System.Array.Copy( source._buckets, 0, _buckets, 0, source._buckets.Length );
			_populated = source._populated;
			_freeList = source._freeList;
			_freeCount = source._freeCount;
			Version++;
		}



		public KeyValueEnumerator<TKey,TValue> GetEnumerator() => new KeyValueEnumerator<TKey,TValue>( this );
		System.Collections.Generic.IEnumerator<(TKey Key, TValue Value)> System.Collections.Generic.IEnumerable<(TKey Key, TValue Value)>.GetEnumerator() => GetEnumerator();
		System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();



		protected int FindEntry( TKey key )
		{
			if( _keyIsStruct == false && key == null )
				throw new System.ArgumentException( nameof( key ) );

			if( _buckets.Length == 0 )
				return -1;

			int hashCode = Comparer.GetHashCode( key ) & 0x7FFFFFFF;
			int i = _buckets[ hashCode % _buckets.Length ];
			while( i >= 0 )
			{
				ref var entryRef = ref _entries[ i ];
				if( entryRef.HashCode == hashCode && Comparer.Equals( entryRef.Key, key ) )
					return i;
				i = entryRef.Next;
			}

			return -1;
		}



		void Initialize( int capacity )
		{
			int size = Utility.GetPrime( capacity );
			_buckets = new int[ size ];
			for( int i = 0; i < _buckets.Length; i++ ) _buckets[ i ] = - 1;
			_entries = new Entry[ size ];
			_freeList = - 1;
		}



		protected void Resize()
		{
			Resize( Utility.ExpandPrime( _populated ) );
		}



		protected void Resize( int newSize )
		{
			int[] newBuckets = new int[ newSize ];
			for( int i = 0; i < newBuckets.Length; i++ )
				newBuckets[ i ] = - 1;
			Entry[] newEntries = new Entry[ newSize ];
			System.Array.Copy( _entries, 0, newEntries, 0, _populated );

			for( int i = 0; i < _populated; i++ )
			{
				ref var entryRef = ref newEntries[ i ];
				if( entryRef.HashCode >= 0 )
				{
					ref var newBucketRef = ref newBuckets[ entryRef.HashCode % newSize ];
					entryRef.Next = newBucketRef;
					newBucketRef = i;
				}
			}

			_buckets = newBuckets;
			_entries = newEntries;
		}



		protected bool TryRemoveSuite( int previousI, int i, int hashCode, TKey key )
		{
			if( i < 0 )
				return false;

			ref var entry = ref _entries[ i ];
			if( entry.HashCode == hashCode && Comparer.Equals( entry.Key, key ) )
			{
				TryRemoveSuite( i, entry.Next, hashCode, key );
				RemoveUnsafe( previousI, i, hashCode );
				return true;
			}

			return false;
		}



		protected void RemoveUnsafe( int previousI, int i, int hashCode )
		{
			ref var entry = ref _entries[ i ];
			if( previousI < 0 )
				_buckets[ hashCode % _buckets.Length ] = entry.Next;
			else
				_entries[ previousI ].Next = entry.Next;

			entry = default;
			entry.HashCode = - 1;
			entry.Next = _freeList;
			_freeList = i;
			_freeCount++;
			Version++;
		}



		internal struct Entry
		{
			public int HashCode; // Lower 31 bits of hash code, -1 if unused
			public int Next; // Index of next entry, -1 if last
			public TKey Key; // Key of entry
			public TValue Value; // Value of entry
		}
	}



	public struct KeyValueEnumerator<TKey, TValue> :
		System.Collections.Generic.IEnumerator<(TKey Key, TValue Value)>,
		System.Collections.Generic.IEnumerator<System.Collections.Generic.KeyValuePair<TKey, TValue>>,
		// Itself an enumerable, easier if you just want to pass read-only access, don't pass the collection but this instead
		System.Collections.Generic.IEnumerable<(TKey Key, TValue Value)> where TKey : notnull
	{
		BaseKeyValues<TKey, TValue> _source;
		int _version;
		int _index;
		(TKey key, TValue value) _current;



		public KeyValueEnumerator( BaseKeyValues<TKey, TValue> baseKeyValues )
		{
			_source = baseKeyValues;
			_version = baseKeyValues.Version;
			_index = 0;
			_current = default;
		}



		public bool MoveNext()
		{
			if( _version != _source.Version )
				throw new System.InvalidOperationException( $"{_source.GetType()} changed while iterating over it" );

			var entries = _source._entries;

			// Use unsigned comparison since we set index to dictionary.count+1 when the enumeration ends.
			// dictionary.count+1 could be negative if dictionary.count is Int32.MaxValue
			while( (uint) _index < (uint) _source._populated )
			{
				ref var entry = ref entries[ _index ];
				if( entry.HashCode >= 0 )
				{
					_current = ( entry.Key, entry.Value );
					_index++;
					return true;
				}

				_index++;
			}

			_index = _source._populated + 1;
			_current = default;
			return false;
		}



		public int GetCurrentIndex() => _index - 1;



		public (TKey Key, TValue Value) Current => _current;
		object System.Collections.IEnumerator.Current => _current;
		System.Collections.Generic.KeyValuePair<TKey, TValue> System.Collections.Generic.IEnumerator<System.Collections.Generic.KeyValuePair<TKey, TValue>>.Current => new System.Collections.Generic.KeyValuePair<TKey, TValue>(Current.Item1, Current.Item2);

		public KeyValueEnumerator<TKey,TValue> GetEnumerator() => this;
		System.Collections.Generic.IEnumerator<(TKey Key, TValue Value)> System.Collections.Generic.IEnumerable<(TKey Key, TValue Value)>.GetEnumerator() => GetEnumerator();
		System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();



		void System.IDisposable.Dispose()
		{
		}



		public void Reset()
		{
			_version = _source.Version;
			_index = 0;
			_current = default;
		}
	}



	static class Utility
	{
		const int HASH_PRIME = 101;

		static readonly int[] PRIMES =
		{
			3, 7, 11, 17, 23, 29, 37, 47, 59, 71, 89, 107, 131, 163, 197, 239, 293, 353, 431, 521, 631, 761, 919,
			1103, 1327, 1597, 1931, 2333, 2801, 3371, 4049, 4861, 5839, 7013, 8419, 10103, 12143, 14591,
			17519, 21023, 25229, 30293, 36353, 43627, 52361, 62851, 75431, 90523, 108631, 130363, 156437,
			187751, 225307, 270371, 324449, 389357, 467237, 560689, 672827, 807403, 968897, 1162687, 1395263,
			1674319, 2009191, 2411033, 2893249, 3471899, 4166287, 4999559, 5999471, 7199369
		};

		const int MAX_PRIME_ARRAY_LENGTH = 0x7FEFFFFD;



		public static int GetPrime( int min )
		{
			if( min < 0 )
				throw new System.ArgumentException( $"{nameof( min )} cannot be negative" );

			for( int i = 0; i < PRIMES.Length; i++ )
			{
				int prime = PRIMES[ i ];
				if( prime >= min ) return prime;
			}

			//outside of our predefined table.
			//compute the hard way.
			for( int i = ( min | 1 ); i < int.MaxValue; i += 2 )
			{
				if( IsPrime( i ) && ( ( i - 1 ) % HASH_PRIME != 0 ) )
					return i;
			}

			return min;
		}



		// Returns size of hashtable to grow to.
		public static int ExpandPrime( int oldSize )
		{
			int newSize = 2 * oldSize;

			// Allow the hashtables to grow to maximum possible size (~2G elements) before encoutering capacity overflow.
			// Note that this check works even when _items.Length overflowed thanks to the (uint) cast
			if( (uint) newSize > MAX_PRIME_ARRAY_LENGTH && MAX_PRIME_ARRAY_LENGTH > oldSize )
				return MAX_PRIME_ARRAY_LENGTH;

			return GetPrime( newSize );
		}



		static bool IsPrime( int candidate )
		{
			if( ( candidate & 1 ) != 0 )
			{
				int limit = (int) System.Math.Sqrt( candidate );
				for( int divisor = 3; divisor <= limit; divisor += 2 )
				{
					if( ( candidate % divisor ) == 0 )
						return false;
				}

				return true;
			}

			return ( candidate == 2 );
		}
	}
}
namespace Project.Collection
{
	public class UnmanagedComparer<TKey> : IDeterministicEqualityComparer<TKey> where TKey : unmanaged, System.IEquatable<TKey>
	{
		// Not safe to compare each bytes as c# / interop does not guarantee that padding bytes are zeroed

		public static UnmanagedComparer<TKey> Instance = new UnmanagedComparer<TKey>();
		public bool Equals( TKey x, TKey y ) => x.Equals( y );
		public int GetHashCode( TKey key ) => key.GetHashCode();
	}


	public class StringComparer : IDeterministicEqualityComparer<string>
	{
		public static StringComparer Instance = new StringComparer();

		public bool Equals( string? x, string? y ) => string.Equals( x, y, System.StringComparison.Ordinal );
		public int GetHashCode( string key )
		{
			return key.GetHashCode();
		}
	}
}