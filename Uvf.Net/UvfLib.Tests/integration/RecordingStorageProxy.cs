using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using StorageLib.Abstractions;

namespace UvfLib.Tests.integration
{
    /// <summary>
    /// Forwards every <see cref="IStorage"/> call to a real storage and records the member names, so a test
    /// can see which storage operations a vault decorator asks of the storage below it.
    /// </summary>
    public class RecordingStorageProxy : DispatchProxy
    {
        private readonly ConcurrentQueue<string> _calls = new();
        private IStorage _target = null!;

        public List<string> Calls => _calls.ToList();

        public static IStorage Wrap(IStorage target, out RecordingStorageProxy recorder)
        {
            ArgumentNullException.ThrowIfNull(target);

            IStorage proxy = Create<IStorage, RecordingStorageProxy>();
            recorder = (RecordingStorageProxy)(object)proxy;
            recorder._target = target;
            return proxy;
        }

        public void ClearCalls() => _calls.Clear();

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            ArgumentNullException.ThrowIfNull(targetMethod);

            _calls.Enqueue(targetMethod.Name);
            try
            {
                return targetMethod.Invoke(_target, args);
            }
            catch (TargetInvocationException exception) when (exception.InnerException is not null)
            {
                ExceptionDispatchInfo.Throw(exception.InnerException);
                throw;
            }
        }
    }
}
