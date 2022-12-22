using System;
using System.Buffers;
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

    private readonly Node<T> root;

    private readonly ReaderWriterLockSlim lockSlim = new();
    private bool readOnly;

    internal bool ReadOnly { get => readOnly; set => readOnly = value; }

    /// <summary>
    /// Creates a PrefixLookup of type T.
    /// </summary>
    /// <param name="readOnly">If read and write access should be syncrhonized behind reader writer locks</param>
    public PrefixLookup(bool readOnly)
    {
        root = new Node<T>();
        this.readOnly = readOnly;
    }

    public int NodeCount => root.GetChildrenCount();

    public int Count => root.GetValuesCount();

    public void Set(ReadOnlySpan<char> keyBytes, T value)
    {
        if (readOnly) throw new InvalidOperationException("Cannot set values in a PrefixLookup that is read only");

        if (lockSlim.IsWriteLockHeld)
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
        if (readOnly) return;
        if (lockSlim.IsWriteLockHeld) return;
        lockSlim.EnterWriteLock();
    }

    /// <summary>
    /// Releases the write lock held on this instance if one is held by the current thread.
    /// </summary>
    public void Unlock()
    {
        if (readOnly) return;
        if (lockSlim.IsWriteLockHeld)
        {
            lockSlim.ExitWriteLock();
        }
    }

    public T Get(ReadOnlySpan<char> key)
    {

        if (readOnly || lockSlim.IsWriteLockHeld)
        {
            return root.GetValue(key)?.Value;
        }

        lockSlim.EnterReadLock();
        try
        {
            return root.GetValue(key)?.Value;

        }
        finally
        {
            lockSlim.ExitReadLock();
        }
    }

    public void Clear()
    {

        if (readOnly) throw new InvalidOperationException("Cannot clear a PrefixLookup that is read only");

        if (lockSlim.IsWriteLockHeld)
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

    public PrefixResult<TSelection> GetByPrefix<TSelection>(string keyPrefix) where TSelection : class, T
    {

        PrefixResult<TSelection> result = new(keyPrefix);

        if (readOnly || lockSlim.IsWriteLockHeld)
        {
            if (keyPrefix == string.Empty)
            {
                root.CollectValues(result);
            }
            else
            {
                root.GetValuesByPrefix(keyPrefix, result);
            }
            return result;
        }

        lockSlim.EnterReadLock();
        try
        {
            if (keyPrefix == string.Empty)
            {
                root.CollectValues(result);
            }
            else
            {
                root.GetValuesByPrefix(keyPrefix, result);
            }
            return result;
        }
        finally
        {
            lockSlim.ExitReadLock();
        }


    }

}
