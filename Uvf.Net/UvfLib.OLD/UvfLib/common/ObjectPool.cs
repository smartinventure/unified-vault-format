using System;
using System.Collections.Concurrent;
using System.Threading;

namespace UvfLib.Common
{
    /// <summary>
    /// A pool of reusable objects.
    /// </summary>
    /// <typeparam name="T">The type of objects to pool</typeparam>
    public class ObjectPool<T> where T : class
    {
        private readonly ConcurrentBag<T> _objects;
        private readonly Func<T> _objectFactory;
        
        /// <summary>
        /// Creates a new object pool.
        /// </summary>
        /// <param name="objectFactory">A factory method to create new objects</param>
        public ObjectPool(Func<T> objectFactory)
        {
            _objectFactory = objectFactory ?? throw new ArgumentNullException(nameof(objectFactory));
            _objects = new ConcurrentBag<T>();
        }
        
        /// <summary>
        /// Gets an object from the pool, or creates a new one if none is available.
        /// </summary>
        /// <returns>A lease providing access to the object</returns>
        public Lease<T> Get()
        {
            if (_objects.TryTake(out T? item))
            {
                return new Lease<T>(this, item);
            }
            
            return new Lease<T>(this, _objectFactory());
        }
        
        /// <summary>
        /// Returns an object to the pool.
        /// </summary>
        /// <param name="item">The item to return</param>
        private void Return(T item)
        {
            _objects.Add(item);
        }
        
        /// <summary>
        /// A lease providing access to a pooled object.
        /// </summary>
        /// <typeparam name="TItem">The type of the pooled object</typeparam>
        public class Lease<TItem> : IDisposable where TItem : class
        {
            private readonly ObjectPool<TItem> _pool;
            private TItem? _value;
            
            /// <summary>
            /// Creates a new lease.
            /// </summary>
            /// <param name="pool">The pool this lease is from</param>
            /// <param name="value">The leased object</param>
            internal Lease(ObjectPool<TItem> pool, TItem value)
            {
                _pool = pool;
                _value = value;
            }
            
            /// <summary>
            /// Gets the leased object.
            /// </summary>
            /// <returns>The leased object</returns>
            /// <exception cref="ObjectDisposedException">If the lease has been disposed</exception>
            public TItem Get()
            {
                if (_value == null)
                {
                    throw new ObjectDisposedException("Lease has been disposed");
                }
                
                return _value;
            }
            
            /// <summary>
            /// Disposes the lease, returning the object to the pool.
            /// </summary>
            public void Dispose()
            {
                if (_value != null)
                {
                    TItem? value = Interlocked.Exchange(ref _value, null);
                    if (value != null)
                    {
                        _pool.Return(value);
                    }
                }
            }
        }
    }
} 