using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualBasic;

namespace VestPocket;

/// <summary>
/// A class for inserting and looking up values
/// based on a key (like a dictionary) that also
/// supports looking up matching values by a key
/// prefix value. Internally implemented with a radix
/// tree where leaf nodes also store entities.
/// </summary>
/// <seealso>https://en.wikipedia.org/wiki/Radix_tree</seealso>
/// 
/// <typeparam name="T"></typeparam>
/// <remarks>
/// This implementation has also had a ReaderWriterLockSlim applied to lock around writes, after it
/// was determined that using such a lock offered better read / prefix performance than queuing the same
/// operations through a Queue/Channel. It may make sense to replace this lock with a different locking scheme 
/// if experimentation proves such an approach can improve performance.
/// </remarks>
internal class PrefixLookup<T> where T : class, IEntity
{

    private Node<T> root;

    private ReaderWriterLockSlim lockSlim = new ReaderWriterLockSlim();
    private readonly bool synchronized;

    /// <summary>
    /// Creates a PrefixLookup of type T.
    /// </summary>
    /// <param name="synchronized">If read and write access should be syncrhonized behind reader writer locks</param>
    public PrefixLookup(bool synchronized)
    {
        root = new Node<T>(null, Node<T>.GetKeyBytes(string.Empty));
        this.synchronized = synchronized;
    }

    public int NodeCount => root.GetChildrenCount();

    public int Count => root.GetValuesCount();

    public void Add(string key, T value)
    {
        Span<byte> keyBytes = Encoding.UTF8.GetBytes(key);

        if (!synchronized || lockSlim.IsWriteLockHeld)
        {
            root.SetValue(keyBytes, value);
            return;
        }

        lockSlim.EnterWriteLock();
        try
        {
            root.SetValue(keyBytes, value);
        }
        finally
        {
            lockSlim.ExitWriteLock();
        }
    }

    /// <summary>
    /// Locks this instance for writing if a write lock is not already held by this thread.
    /// </summary>
    public void Lock()
    {
        if (!synchronized) return;
        if (lockSlim.IsWriteLockHeld) return;
        lockSlim.EnterWriteLock();
    }

    /// <summary>
    /// Releases the write lock held on this instance if one is held by the current thread.
    /// </summary>
    public void Unlock()
    {
        if (!synchronized) return;
        if (lockSlim.IsWriteLockHeld)
        {
            lockSlim.ExitWriteLock();
        }
    }

    public T Get(string key)
    {
        Span<byte> keyBytes = Encoding.UTF8.GetBytes(key);

        if (!synchronized || lockSlim.IsWriteLockHeld)
        {
            return root.GetValue(keyBytes)?.Value;
        }

        lockSlim.EnterReadLock();
        try
        {
            return root.GetValue(keyBytes)?.Value;

        }
        finally
        {
            lockSlim.ExitReadLock();
        }
    }

    public void Clear()
    {
        if (!synchronized || lockSlim.IsWriteLockHeld)
        {
            this.root.Value = null;
            this.root.Children = null;
            return;
        }

        lockSlim.EnterWriteLock();
        try
        {
            this.root.Value = null;
            this.root.Children = null;
        }
        finally
        {
            lockSlim.ExitWriteLock();
        }
    }

    public IEnumerable<TSelection> GetByPrefix<TSelection>(string keyPrefix) where TSelection : class, T
    {
        IEnumerable<TSelection> results;

        Memory<byte> keyPrefixSpan = Encoding.UTF8.GetBytes(keyPrefix);

        if (!synchronized || lockSlim.IsWriteLockHeld)
        {
            if (keyPrefix == string.Empty)
            {
                results = root.GetAllValuesAtOrBelow<TSelection>();
            }
            else
            {
                results = root.GetValuesByPrefix<TSelection>(keyPrefixSpan);
            }
            return results.ToArray();
        }

        lockSlim.EnterReadLock();
        try
        {
            if (keyPrefix == string.Empty)
            {
                results = root.GetAllValuesAtOrBelow<TSelection>();
            }
            else
            {
                results = root.GetValuesByPrefix<TSelection>(keyPrefixSpan);
            }
            return results.ToArray();
        }
        finally
        {
            lockSlim.ExitReadLock();
        }


    }

}
